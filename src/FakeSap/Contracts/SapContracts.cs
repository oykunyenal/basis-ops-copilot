using System.Text.Json.Serialization;

namespace FakeSap.Contracts;

// SAP field names are kept on purpose (uppercase, as in the SAP tables/BAPIs).
// Translating them into domain language is the job of the anti-corruption layer in BasisOps.Infrastructure.

// T000 / STMS landscape. CCCATEGORY: P = production, T = test, C = customizing.
public sealed record SystemDto(
    [property: JsonPropertyName("SID")] string Sid,
    [property: JsonPropertyName("MANDT")] string Client,
    [property: JsonPropertyName("CCCATEGORY")] string ClientCategory,
    [property: JsonPropertyName("HOST")] string Host);

// SM37 (TBTCO). STATUS: A = canceled, F = finished, R = active, S = released.
public sealed record JobDto(
    [property: JsonPropertyName("JOBNAME")] string JobName,
    [property: JsonPropertyName("JOBCOUNT")] string JobCount,
    [property: JsonPropertyName("STATUS")] string Status,
    [property: JsonPropertyName("STRTDATE")] string StartDate,
    [property: JsonPropertyName("STRTTIME")] string StartTime,
    [property: JsonPropertyName("SDLUNAME")] string ScheduledBy);

// ST22 (SNAP) list entry.
public sealed record DumpDto(
    [property: JsonPropertyName("DUMPID")] string DumpId,
    [property: JsonPropertyName("DATUM")] string Date,
    [property: JsonPropertyName("UZEIT")] string Time,
    [property: JsonPropertyName("UNAME")] string User,
    [property: JsonPropertyName("AHOST")] string Host,
    [property: JsonPropertyName("ERRORID")] string RuntimeError,
    [property: JsonPropertyName("PROGRAM")] string Program,
    [property: JsonPropertyName("SHORT_TEXT")] string ShortText);

// ST22 detail view ("What happened?" / "Error analysis").
public sealed record DumpDetailDto(
    [property: JsonPropertyName("DUMPID")] string DumpId,
    [property: JsonPropertyName("DATUM")] string Date,
    [property: JsonPropertyName("UZEIT")] string Time,
    [property: JsonPropertyName("UNAME")] string User,
    [property: JsonPropertyName("AHOST")] string Host,
    [property: JsonPropertyName("ERRORID")] string RuntimeError,
    [property: JsonPropertyName("PROGRAM")] string Program,
    [property: JsonPropertyName("SHORT_TEXT")] string ShortText,
    [property: JsonPropertyName("WHAT_HAPPENED")] string WhatHappened,
    [property: JsonPropertyName("ERROR_ANALYSIS")] string ErrorAnalysis);

// SU01 (USR02). USTYP: A = dialog, B = system. UFLAG: 64 = locked by admin, 128 = locked after failed logons.
public sealed record LockedUserDto(
    [property: JsonPropertyName("BNAME")] string UserName,
    [property: JsonPropertyName("USTYP")] string UserType,
    [property: JsonPropertyName("CLASS")] string UserGroup,
    [property: JsonPropertyName("UFLAG")] int LockFlag,
    [property: JsonPropertyName("LOCK_DATE")] string LockDate,
    [property: JsonPropertyName("LOCK_TIME")] string LockTime);

// STMS import queue. TRFUNCTION: K = workbench, W = customizing. RETCODE: 0, 4, 8, 12 (null = not imported yet).
public sealed record TransportDto(
    [property: JsonPropertyName("TRKORR")] string TransportNumber,
    [property: JsonPropertyName("AS4USER")] string Owner,
    [property: JsonPropertyName("AS4TEXT")] string Description,
    [property: JsonPropertyName("TRFUNCTION")] string Function,
    [property: JsonPropertyName("STATUS")] string Status,
    [property: JsonPropertyName("RETCODE")] int? ReturnCode);

// STRUST.
public sealed record CertificateDto(
    [property: JsonPropertyName("PSE_NAME")] string PseName,
    [property: JsonPropertyName("SUBJECT")] string Subject,
    [property: JsonPropertyName("ISSUER")] string Issuer,
    [property: JsonPropertyName("VALID_FROM")] string ValidFrom,
    [property: JsonPropertyName("VALID_TO")] string ValidTo);

// SM50, aggregated per work process type (DIA, BTC, UPD, UP2, SPO).
public sealed record WorkProcessDto(
    [property: JsonPropertyName("TYP")] string Type,
    [property: JsonPropertyName("TOTAL")] int Total,
    [property: JsonPropertyName("BUSY")] int Busy);

// BAPIRET2. TYPE: S = success, W = warning, E = error.
public sealed record BapiReturn(
    [property: JsonPropertyName("TYPE")] string Type,
    [property: JsonPropertyName("ID")] string Id,
    [property: JsonPropertyName("NUMBER")] string Number,
    [property: JsonPropertyName("MESSAGE")] string Message);

// BAPI_USER_UNLOCK.
public sealed record UserUnlockResponse(
    [property: JsonPropertyName("RETURN")] BapiReturn Return);

// BAPI_XBP_JOB_COPY + BAPI_XBP_JOB_START_IMMEDIATELY.
public sealed record JobRestartResponse(
    [property: JsonPropertyName("RETURN")] BapiReturn Return,
    [property: JsonPropertyName("NEW_JOBCOUNT")] string NewJobCount);

// TMS_MGR_IMPORT_TR_REQUEST.
public sealed record TransportImportResponse(
    [property: JsonPropertyName("RETURN")] BapiReturn Return,
    [property: JsonPropertyName("RETCODE")] int ReturnCode);
