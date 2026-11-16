using System.Globalization;
using BasisOps.Application.Ports;
using BasisOps.Domain.Observations;

namespace BasisOps.Infrastructure.Sap;

/// <summary>Translates SAP structures into domain types. All SAP dates and times are treated as UTC.</summary>
internal static class SapTranslator
{
    public static JobRun ToJobRun(SapJob job) => new(
        job.JobName,
        job.JobCount,
        job.Status switch
        {
            "A" => JobRunStatus.Canceled,
            "F" => JobRunStatus.Finished,
            "R" => JobRunStatus.Running,
            _ => JobRunStatus.Scheduled,
        },
        ToTimestamp(job.StartDate, job.StartTime),
        job.ScheduledBy);

    public static ShortDump ToShortDump(SapDump dump) => new(
        dump.DumpId,
        dump.RuntimeError,
        dump.Program,
        dump.User,
        dump.Host,
        ToTimestamp(dump.Date, dump.Time),
        dump.ShortText);

    public static ShortDumpDetail ToShortDumpDetail(SapDump dump) =>
        new(ToShortDump(dump), dump.WhatHappened ?? string.Empty, dump.ErrorAnalysis ?? string.Empty);

    public static LockedUser ToLockedUser(SapLockedUser user) => new(
        user.UserName,
        (user.LockFlag & 128) != 0 ? LockReason.FailedLogons : LockReason.Administrator,
        ToTimestamp(user.LockDate, user.LockTime));

    public static TransportRequest ToTransportRequest(SapTransport transport) => new(
        transport.TransportNumber,
        transport.Owner,
        transport.Description,
        string.Equals(transport.Status, "IMPORTED", StringComparison.OrdinalIgnoreCase)
            ? TransportImportStatus.Imported
            : TransportImportStatus.Waiting,
        transport.ReturnCode);

    // VALID_TO is a date; the start of that day is used so that expiry warnings come early rather than late.
    public static Certificate ToCertificate(SapCertificate certificate) =>
        new(certificate.PseName, certificate.Subject, ToTimestamp(certificate.ValidTo, "000000"));

    /// <summary>Returns null for work process types the domain does not track (e.g. ENQ).</summary>
    public static WorkProcessUsage? ToWorkProcessUsage(SapWorkProcess wp) => wp.Type.ToUpperInvariant() switch
    {
        "DIA" => new(WorkProcessType.Dialog, wp.Total, wp.Busy),
        "BTC" => new(WorkProcessType.Background, wp.Total, wp.Busy),
        "UPD" => new(WorkProcessType.Update, wp.Total, wp.Busy),
        "UP2" => new(WorkProcessType.Update2, wp.Total, wp.Busy),
        "SPO" => new(WorkProcessType.Spool, wp.Total, wp.Busy),
        _ => null,
    };

    /// <summary>BAPIRET2: S/W/I mean the change is in place (W = it already was); E/A mean SAP refused it.</summary>
    public static SapActionOutcome ToOutcome(SapBapiReturn bapiReturn) => bapiReturn.Type switch
    {
        "S" or "W" or "I" => new(SapActionStatus.Succeeded, bapiReturn.Message),
        _ => new(SapActionStatus.Rejected, bapiReturn.Message),
    };

    // DATS (yyyyMMdd) + TIMS (HHmmss).
    public static DateTimeOffset ToTimestamp(string date, string time)
    {
        var value = DateTime.ParseExact(date + time, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None);
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
