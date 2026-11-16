using System.Text.Json.Serialization;

namespace BasisOps.Infrastructure.Sap;

// Wire format of the SAP side. SAP field names stay here and never leave Infrastructure (anti-corruption layer).
// Kept as a copy on purpose: BasisOps does not reference the simulator, it only speaks its protocol.

// SM37 (TBTCO). STATUS: A = canceled, F = finished, R = active, S = released, P = scheduled, Y = ready.
internal sealed record SapJob(
    [property: JsonPropertyName("JOBNAME")] string JobName,
    [property: JsonPropertyName("JOBCOUNT")] string JobCount,
    [property: JsonPropertyName("STATUS")] string Status,
    [property: JsonPropertyName("STRTDATE")] string StartDate,
    [property: JsonPropertyName("STRTTIME")] string StartTime,
    [property: JsonPropertyName("SDLUNAME")] string ScheduledBy);

// ST22 (SNAP).
internal sealed record SapDump(
    [property: JsonPropertyName("DUMPID")] string DumpId,
    [property: JsonPropertyName("DATUM")] string Date,
    [property: JsonPropertyName("UZEIT")] string Time,
    [property: JsonPropertyName("UNAME")] string User,
    [property: JsonPropertyName("AHOST")] string Host,
    [property: JsonPropertyName("ERRORID")] string RuntimeError,
    [property: JsonPropertyName("PROGRAM")] string Program,
    [property: JsonPropertyName("SHORT_TEXT")] string ShortText,
    [property: JsonPropertyName("WHAT_HAPPENED")] string? WhatHappened = null,
    [property: JsonPropertyName("ERROR_ANALYSIS")] string? ErrorAnalysis = null);

// SU01 (USR02). UFLAG: 64 = locked by administrator, 128 = locked after failed logons.
internal sealed record SapLockedUser(
    [property: JsonPropertyName("BNAME")] string UserName,
    [property: JsonPropertyName("UFLAG")] int LockFlag,
    [property: JsonPropertyName("LOCK_DATE")] string LockDate,
    [property: JsonPropertyName("LOCK_TIME")] string LockTime);

// STMS import queue.
internal sealed record SapTransport(
    [property: JsonPropertyName("TRKORR")] string TransportNumber,
    [property: JsonPropertyName("AS4USER")] string Owner,
    [property: JsonPropertyName("AS4TEXT")] string Description,
    [property: JsonPropertyName("STATUS")] string Status,
    [property: JsonPropertyName("RETCODE")] int? ReturnCode);

// STRUST.
internal sealed record SapCertificate(
    [property: JsonPropertyName("PSE_NAME")] string PseName,
    [property: JsonPropertyName("SUBJECT")] string Subject,
    [property: JsonPropertyName("VALID_TO")] string ValidTo);

// SM50 aggregated per type.
internal sealed record SapWorkProcess(
    [property: JsonPropertyName("TYP")] string Type,
    [property: JsonPropertyName("TOTAL")] int Total,
    [property: JsonPropertyName("BUSY")] int Busy);

// BAPIRET2. TYPE: S = success, W = warning, I = info, E = error, A = abort.
internal sealed record SapBapiReturn(
    [property: JsonPropertyName("TYPE")] string Type,
    [property: JsonPropertyName("MESSAGE")] string Message);

// Common shape of the action responses (BAPI_USER_UNLOCK, job restart, transport import).
internal sealed record SapActionResponse(
    [property: JsonPropertyName("RETURN")] SapBapiReturn Return);
