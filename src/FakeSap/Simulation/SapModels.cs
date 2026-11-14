namespace FakeSap.Simulation;

// Internal, mutable simulator state. Never serialized directly; see Contracts for the SAP-shaped DTOs.

internal enum SystemRole { Production, Quality, Development }

internal static class JobStatus
{
    public const char Canceled = 'A';
    public const char Finished = 'F';
    public const char Active = 'R';
    public const char Released = 'S';
}

internal static class LockFlags
{
    public const int None = 0;
    public const int Admin = 64;
    public const int FailedLogons = 128;
}

internal sealed class Job
{
    public required string Name { get; init; }
    public required string Count { get; init; }
    public required char Status { get; set; }
    public required DateTimeOffset Start { get; init; }
    public required string ScheduledBy { get; init; }
    public string? RestartedAs { get; set; }
}

internal sealed class Dump
{
    public required string Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string User { get; init; }
    public required string Host { get; init; }
    public required string HostIp { get; init; }
    public required string RuntimeError { get; init; }
    public required string Program { get; init; }
    public required string ShortText { get; init; }
}

internal sealed class SapUser
{
    public required string Name { get; init; }
    public required char Type { get; init; }
    public required string Group { get; init; }
    public int LockFlag { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public bool IsLocked => LockFlag != LockFlags.None;
}

internal enum TransportStatus { Waiting, Imported }

internal sealed class Transport
{
    public required string Number { get; init; }
    public required string Owner { get; init; }
    public required string Description { get; init; }
    public required char Function { get; init; }
    public TransportStatus Status { get; set; }
    public int? ReturnCode { get; set; }
}

internal sealed record Certificate(string PseName, string Subject, string Issuer, DateTimeOffset ValidFrom, DateTimeOffset ValidTo);

internal sealed class WorkProcessGroup
{
    public required string Type { get; init; }
    public required int Total { get; init; }
    public int Busy { get; set; }
}

internal sealed record JobDefinition(string Name, int IntervalHours, string ScheduledBy);

internal sealed record DumpTemplate(string RuntimeError, string Program, string ShortText, string ErrorAnalysis);
