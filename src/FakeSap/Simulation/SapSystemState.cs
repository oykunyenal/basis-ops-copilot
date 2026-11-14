using FakeSap.Contracts;

namespace FakeSap.Simulation;

internal enum UnlockOutcome { NotFound, Unlocked, AlreadyUnlocked }

internal enum RestartOutcome { NotFound, NotCanceled, Restarted, AlreadyRestarted }

internal enum ImportOutcome { NotFound, Imported, AlreadyImported }

/// <summary>
/// In-memory state of one simulated SAP system. Data is generated from a fixed seed so every
/// run produces the same landscape shape; timestamps are relative to the start time.
/// All access goes through a single lock; the class is safe to use from requests and the event generator.
/// </summary>
internal sealed class SapSystemState
{
    private static readonly TimeSpan DumpRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan JobRetention = TimeSpan.FromDays(2);

    private readonly Lock _gate = new();
    private readonly Random _random;
    private readonly HashSet<string> _jobCounts = [];
    private readonly List<Job> _jobs = [];
    private readonly List<Dump> _dumps = [];
    private readonly List<SapUser> _users = [];
    private readonly List<Transport> _transports = [];
    private readonly List<Certificate> _certificates = [];
    private readonly List<WorkProcessGroup> _workProcesses = [];
    private readonly (string Name, string Ip)[] _hosts;
    private int _dumpSequence;

    public SapSystemState(string sid, SystemRole role, int seed, int networkOctet, DateTimeOffset now)
    {
        Sid = sid;
        Role = role;
        _random = new Random(seed);
        var prefix = sid.ToLowerInvariant();
        _hosts = [($"{prefix}app01", $"10.10.{networkOctet}.11"), ($"{prefix}app02", $"10.10.{networkOctet}.12")];

        SeedJobs(now);
        SeedDumps(now);
        SeedUsers(now);
        SeedTransports();
        SeedCertificates(now);
        SeedWorkProcesses();
    }

    public string Sid { get; }

    public SystemRole Role { get; }

    public SystemDto ToSystemDto() => new(
        Sid,
        "100",
        Role switch { SystemRole.Production => "P", SystemRole.Quality => "T", _ => "C" },
        _hosts[0].Name);

    // SM37
    public IReadOnlyList<JobDto> GetJobs(char? status)
    {
        lock (_gate)
        {
            return _jobs
                .Where(j => status is null || j.Status == status)
                .OrderByDescending(j => j.Start)
                .Select(j => new JobDto(j.Name, j.Count, j.Status.ToString(), SapFormat.Date(j.Start), SapFormat.Time(j.Start), j.ScheduledBy))
                .ToList();
        }
    }

    // ST22
    public IReadOnlyList<DumpDto> GetDumps(DateTimeOffset since)
    {
        lock (_gate)
        {
            return _dumps
                .Where(d => d.Timestamp >= since)
                .OrderByDescending(d => d.Timestamp)
                .Select(d => new DumpDto(d.Id, SapFormat.Date(d.Timestamp), SapFormat.Time(d.Timestamp), d.User, d.Host, d.RuntimeError, d.Program, d.ShortText))
                .ToList();
        }
    }

    public DumpDetailDto? GetDump(string dumpId)
    {
        lock (_gate)
        {
            var d = _dumps.Find(x => string.Equals(x.Id, dumpId, StringComparison.OrdinalIgnoreCase));
            if (d is null)
            {
                return null;
            }

            var template = SeedData.Dumps.First(t => t.RuntimeError == d.RuntimeError);
            var whatHappened =
                $"Error in the ABAP application program. The current ABAP program \"{d.Program}\" had to be terminated " +
                $"because it came across a statement that cannot be executed. User {d.User}, application server {d.Host} ({d.HostIp}).";

            return new DumpDetailDto(d.Id, SapFormat.Date(d.Timestamp), SapFormat.Time(d.Timestamp), d.User, d.Host,
                d.RuntimeError, d.Program, d.ShortText, whatHappened, template.ErrorAnalysis);
        }
    }

    // SU01
    public IReadOnlyList<LockedUserDto> GetLockedUsers()
    {
        lock (_gate)
        {
            return _users
                .Where(u => u.IsLocked)
                .OrderBy(u => u.Name, StringComparer.Ordinal)
                .Select(u => new LockedUserDto(u.Name, u.Type.ToString(), u.Group, u.LockFlag,
                    SapFormat.Date(u.LockedAt!.Value), SapFormat.Time(u.LockedAt.Value)))
                .ToList();
        }
    }

    // STMS
    public IReadOnlyList<TransportDto> GetTransportQueue()
    {
        lock (_gate)
        {
            return _transports
                .Select(t => new TransportDto(t.Number, t.Owner, t.Description, t.Function.ToString(),
                    t.Status == TransportStatus.Waiting ? "WAITING" : "IMPORTED", t.ReturnCode))
                .ToList();
        }
    }

    // STRUST
    public IReadOnlyList<CertificateDto> GetCertificates()
    {
        lock (_gate)
        {
            return _certificates
                .Select(c => new CertificateDto(c.PseName, c.Subject, c.Issuer, SapFormat.Date(c.ValidFrom), SapFormat.Date(c.ValidTo)))
                .ToList();
        }
    }

    // SM50
    public IReadOnlyList<WorkProcessDto> GetWorkProcesses()
    {
        lock (_gate)
        {
            return _workProcesses.Select(w => new WorkProcessDto(w.Type, w.Total, w.Busy)).ToList();
        }
    }

    // BAPI_USER_UNLOCK. Idempotent: unlocking an unlocked user is a warning, not an error.
    public UnlockOutcome UnlockUser(string userName)
    {
        lock (_gate)
        {
            var user = _users.Find(u => string.Equals(u.Name, userName, StringComparison.OrdinalIgnoreCase));
            if (user is null)
            {
                return UnlockOutcome.NotFound;
            }

            if (!user.IsLocked)
            {
                return UnlockOutcome.AlreadyUnlocked;
            }

            user.LockFlag = LockFlags.None;
            user.LockedAt = null;
            return UnlockOutcome.Unlocked;
        }
    }

    // SM37 "Repeat scheduling": copies the canceled job and starts the copy immediately.
    // Idempotent: a second restart of the same run returns the copy created the first time.
    public (RestartOutcome Outcome, string? NewJobCount) RestartJob(string jobName, string jobCount, DateTimeOffset now)
    {
        lock (_gate)
        {
            var job = _jobs.Find(j => string.Equals(j.Name, jobName, StringComparison.OrdinalIgnoreCase) && j.Count == jobCount);
            if (job is null)
            {
                return (RestartOutcome.NotFound, null);
            }

            if (job.RestartedAs is not null)
            {
                return (RestartOutcome.AlreadyRestarted, job.RestartedAs);
            }

            if (job.Status != JobStatus.Canceled)
            {
                return (RestartOutcome.NotCanceled, null);
            }

            var copy = AddJob(job.Name, job.ScheduledBy, now, JobStatus.Finished);
            job.RestartedAs = copy.Count;
            return (RestartOutcome.Restarted, copy.Count);
        }
    }

    // STMS import. Idempotent: re-importing a transport that already imported cleanly (RC <= 4) is a no-op.
    public (ImportOutcome Outcome, int ReturnCode) ImportTransport(string transportNumber)
    {
        lock (_gate)
        {
            var transport = _transports.Find(t => string.Equals(t.Number, transportNumber, StringComparison.OrdinalIgnoreCase));
            if (transport is null)
            {
                return (ImportOutcome.NotFound, 0);
            }

            if (transport is { Status: TransportStatus.Imported, ReturnCode: <= 4 })
            {
                return (ImportOutcome.AlreadyImported, transport.ReturnCode.Value);
            }

            transport.Status = TransportStatus.Imported;
            transport.ReturnCode = 0;
            return (ImportOutcome.Imported, 0);
        }
    }

    /// <summary>Advances the simulation: new dumps, failed logons, job runs and work process load.</summary>
    public void Tick(DateTimeOffset now)
    {
        lock (_gate)
        {
            var dumpChance = Role switch { SystemRole.Production => 0.6, SystemRole.Quality => 0.3, _ => 0.4 };
            if (_random.NextDouble() < dumpChance)
            {
                AddDump(now, SeedData.Dumps[_random.Next(SeedData.Dumps.Length)]);
            }

            if (_random.NextDouble() < 0.15)
            {
                var candidates = _users.Where(u => u is { IsLocked: false, Type: 'A' }).ToList();
                if (candidates.Count > 0)
                {
                    var user = candidates[_random.Next(candidates.Count)];
                    user.LockFlag = LockFlags.FailedLogons;
                    user.LockedAt = now;
                }
            }

            var definition = SeedData.Jobs[_random.Next(SeedData.Jobs.Length)];
            AddJob(definition.Name, definition.ScheduledBy, now, _random.NextDouble() < 0.1 ? JobStatus.Canceled : JobStatus.Finished);

            foreach (var job in _jobs.Where(j => j.Status == JobStatus.Active && j.Start < now.AddMinutes(-10)))
            {
                job.Status = JobStatus.Finished;
            }

            RefreshWorkProcessLoad();

            _dumps.RemoveAll(d => d.Timestamp < now - DumpRetention);
            _jobs.RemoveAll(j => j.Start < now - JobRetention && j.Status != JobStatus.Released);
        }
    }

    private void SeedJobs(DateTimeOffset now)
    {
        foreach (var definition in SeedData.Jobs)
        {
            var interval = TimeSpan.FromHours(definition.IntervalHours);
            var start = now.AddHours(-24).AddMinutes(_random.Next(0, 30));
            for (; start <= now; start += interval)
            {
                var status = start > now.AddMinutes(-10) ? JobStatus.Active
                    : _random.NextDouble() < 0.03 ? JobStatus.Canceled
                    : JobStatus.Finished;
                AddJob(definition.Name, definition.ScheduledBy, start, status);
            }

            AddJob(definition.Name, definition.ScheduledBy, start, JobStatus.Released);
        }

        // Demo guarantee: every system has a canceled run; in production it is the critical billing job.
        var (canceledJob, owner) = Role switch
        {
            SystemRole.Production => ("Z_BILLING_DAILY", "BATCH_ADM"),
            SystemRole.Quality => ("Z_MM_STOCK_SYNC", "BATCH_ADM"),
            _ => ("SAP_REORG_SPOOL", "DDIC"),
        };
        AddJob(canceledJob, owner, now.AddHours(-3), JobStatus.Canceled);
    }

    private void SeedDumps(DateTimeOffset now)
    {
        var count = Role switch { SystemRole.Production => 15, SystemRole.Quality => 6, _ => 10 };
        for (var i = 0; i < count; i++)
        {
            AddDump(now.AddMinutes(-_random.Next(60, 24 * 60)), SeedData.Dumps[_random.Next(SeedData.Dumps.Length)]);
        }

        // Demo guarantee: a burst of TIME_OUT dumps in the last hour in production.
        if (Role == SystemRole.Production)
        {
            var timeout = SeedData.Dumps.First(t => t.RuntimeError == "TIME_OUT");
            for (var i = 0; i < 6; i++)
            {
                AddDump(now.AddMinutes(-_random.Next(1, 55)), timeout);
            }
        }

        _dumps.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
    }

    private void SeedUsers(DateTimeOffset now)
    {
        foreach (var (name, type, group) in SeedData.Users)
        {
            var user = new SapUser { Name = name, Type = type, Group = group };
            if (type == 'A' && _random.NextDouble() < 0.15)
            {
                user.LockFlag = LockFlags.FailedLogons;
                user.LockedAt = now.AddMinutes(-_random.Next(5, 600));
            }

            _users.Add(user);
        }

        // Demo guarantees: one user locked after failed logons, one locked by an administrator.
        if (Role == SystemRole.Production)
        {
            Lock("MEHMET", LockFlags.FailedLogons, now.AddMinutes(-12));
            Lock("JDOE", LockFlags.Admin, now.AddDays(-2));
        }

        void Lock(string name, int flag, DateTimeOffset at)
        {
            var user = _users.First(u => u.Name == name);
            user.LockFlag = flag;
            user.LockedAt = at;
        }
    }

    private void SeedTransports()
    {
        // DEV is the export system; only QAS and PRD have an import queue.
        if (Role == SystemRole.Development)
        {
            return;
        }

        int?[] returnCodes = Role == SystemRole.Production ? [0, 0, 4, 0, 8, null, null] : [0, 4, 0, 12, 0, 4, null];
        var firstNumber = _random.Next(100, 900);
        for (var i = 0; i < returnCodes.Length; i++)
        {
            _transports.Add(new Transport
            {
                Number = $"DEVK9{firstNumber + i:D5}",
                Owner = SeedData.Developers[_random.Next(SeedData.Developers.Length)],
                Description = SeedData.TransportTexts[i % SeedData.TransportTexts.Length],
                Function = _random.NextDouble() < 0.7 ? 'K' : 'W',
                Status = returnCodes[i] is null ? TransportStatus.Waiting : TransportStatus.Imported,
                ReturnCode = returnCodes[i],
            });
        }
    }

    private void SeedCertificates(DateTimeOffset now)
    {
        var host = _hosts[0].Name;
        var issuer = "CN=Example Internal CA, O=Example Corp, C=TR";

        // Days until expiry per PSE; production has one critical and one warning certificate.
        var (serverDays, clientDays) = Role switch
        {
            SystemRole.Production => (5, 25),
            SystemRole.Quality => (20, 340),
            _ => (300, 410),
        };

        _certificates.Add(new Certificate("System PSE", $"CN={Sid}, OU=SAP", $"CN={Sid}, OU=SAP", now.AddYears(-3), now.AddYears(7)));
        _certificates.Add(new Certificate("SSL server Standard", $"CN={host}.example.internal", issuer, now.AddDays(serverDays - 365), now.AddDays(serverDays)));
        _certificates.Add(new Certificate("SSL client Standard", $"CN={Sid} SSL client", issuer, now.AddDays(clientDays - 730), now.AddDays(clientDays)));
        _certificates.Add(new Certificate("SSL client Anonymous", "CN=anonymous", issuer, now.AddYears(-1), now.AddYears(4)));
    }

    private void SeedWorkProcesses()
    {
        (string Type, int Total)[] layout = Role switch
        {
            SystemRole.Production => [("DIA", 20), ("BTC", 10), ("UPD", 4), ("UP2", 2), ("SPO", 2)],
            SystemRole.Quality => [("DIA", 10), ("BTC", 6), ("UPD", 2), ("UP2", 1), ("SPO", 1)],
            _ => [("DIA", 6), ("BTC", 4), ("UPD", 1), ("UP2", 1), ("SPO", 1)],
        };

        foreach (var (type, total) in layout)
        {
            _workProcesses.Add(new WorkProcessGroup { Type = type, Total = total });
        }

        RefreshWorkProcessLoad();
    }

    private void RefreshWorkProcessLoad()
    {
        foreach (var group in _workProcesses)
        {
            // Production dialog load occasionally spikes close to saturation.
            var load = Role == SystemRole.Production && group.Type == "DIA" && _random.NextDouble() < 0.25
                ? 0.85 + (_random.NextDouble() * 0.15)
                : _random.NextDouble() * 0.7;
            group.Busy = Math.Min(group.Total, (int)Math.Round(group.Total * load));
        }
    }

    private Job AddJob(string name, string scheduledBy, DateTimeOffset start, char status)
    {
        string count;
        do
        {
            count = _random.Next(10_000_000, 100_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        while (!_jobCounts.Add(count));

        var job = new Job { Name = name, Count = count, Status = status, Start = start, ScheduledBy = scheduledBy };
        _jobs.Add(job);
        return job;
    }

    private void AddDump(DateTimeOffset at, DumpTemplate template)
    {
        var (host, ip) = _hosts[_random.Next(_hosts.Length)];
        var dialogUsers = SeedData.Users.Where(u => u.Type == 'A').ToArray();
        var user = template.Program.StartsWith("Z_", StringComparison.Ordinal) && _random.NextDouble() < 0.5
            ? "BATCH_ADM"
            : dialogUsers[_random.Next(dialogUsers.Length)].Name;

        _dumps.Add(new Dump
        {
            Id = $"{Sid}-{SapFormat.Date(at)}{SapFormat.Time(at)}-{++_dumpSequence:D4}",
            Timestamp = at,
            User = user,
            Host = host,
            HostIp = ip,
            RuntimeError = template.RuntimeError,
            Program = template.Program,
            ShortText = template.ShortText,
        });
    }
}
