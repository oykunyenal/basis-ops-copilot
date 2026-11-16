namespace BasisOps.Domain.Observations;

// Domain view of what a health scan observes in an SAP system.
// These types use domain language; SAP field names (JOBNAME, BNAME, TRKORR...) are mapped in Infrastructure.

public enum JobRunStatus
{
    Scheduled,
    Running,
    Finished,
    Canceled,
}

// SM37
public sealed record JobRun(string JobName, string RunId, JobRunStatus Status, DateTimeOffset StartedAt, string Owner);

// ST22
public sealed record ShortDump(
    string Id,
    string RuntimeError,
    string Program,
    string User,
    string Host,
    DateTimeOffset OccurredAt,
    string ShortText);

// ST22 detail view, input for AI analysis.
public sealed record ShortDumpDetail(ShortDump Dump, string WhatHappened, string ErrorAnalysis);

public enum LockReason
{
    FailedLogons,
    Administrator,
}

// SU01
public sealed record LockedUser(string UserName, LockReason Reason, DateTimeOffset LockedAt);

public enum TransportImportStatus
{
    Waiting,
    Imported,
}

// STMS
public sealed record TransportRequest(
    string Number,
    string Owner,
    string Description,
    TransportImportStatus Status,
    int? ReturnCode);

// STRUST
public sealed record Certificate(string Store, string Subject, DateTimeOffset ValidTo);

public enum WorkProcessType
{
    Dialog,
    Background,
    Update,
    Update2,
    Spool,
}

// SM50
public sealed record WorkProcessUsage(WorkProcessType Type, int Total, int Busy)
{
    public double Utilization => Total == 0 ? 0 : (double)Busy / Total;
}
