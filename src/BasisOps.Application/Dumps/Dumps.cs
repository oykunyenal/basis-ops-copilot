using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Observations;
using Microsoft.Extensions.Logging;

namespace BasisOps.Application.Dumps;

public sealed record DumpDto(
    string Id,
    string RuntimeError,
    string Program,
    string User,
    string Host,
    DateTimeOffset OccurredAt,
    string ShortText)
{
    public static DumpDto From(ShortDump dump) =>
        new(dump.Id, dump.RuntimeError, dump.Program, dump.User, dump.Host, dump.OccurredAt, dump.ShortText);
}

// ST22 list
public sealed record ListDumpsQuery(string Sid, int Hours = 24);

public sealed class ListDumpsHandler(ISystemRegistry systems, ISapGateway sap, TimeProvider timeProvider)
    : IQueryHandler<ListDumpsQuery, IReadOnlyList<DumpDto>>
{
    public const int MaxHours = 7 * 24;
    public const int MaxItems = 200;

    public async Task<Result<IReadOnlyList<DumpDto>>> HandleAsync(ListDumpsQuery query, CancellationToken cancellationToken)
    {
        if (query.Hours is < 1 or > MaxHours)
        {
            var errors = new ValidationErrors();
            errors.Add("hours", $"Hours must be between 1 and {MaxHours}.");
            return errors.ToError();
        }

        if (await systems.FindAsync(query.Sid, cancellationToken) is not { } system)
        {
            return Error.NotFound("system_not_found", $"System {query.Sid} is not managed.");
        }

        var since = timeProvider.GetUtcNow().AddHours(-query.Hours);
        var dumps = await sap.GetShortDumpsAsync(system.Sid, since, cancellationToken);
        List<DumpDto> items = [.. dumps.OrderByDescending(d => d.OccurredAt).ThenBy(d => d.Id, StringComparer.Ordinal).Take(MaxItems).Select(DumpDto.From)];
        return items;
    }
}

// ST22 detail + LLM analysis
public sealed record AnalyzeDumpCommand(string Sid, string DumpId);

/// <param name="Analysis">Null when the LLM could not be reached or gave no usable answer.</param>
public sealed record DumpAnalysisDto(string Sid, DumpDto Dump, DumpAnalysis? Analysis, bool AiAvailable);

public sealed partial class AnalyzeDumpHandler(
    ISystemRegistry systems,
    ISapGateway sap,
    IAiAnalyzer aiAnalyzer,
    ILogger<AnalyzeDumpHandler> logger) : ICommandHandler<AnalyzeDumpCommand, DumpAnalysisDto>
{
    public const int MaxDumpIdLength = 64;

    public async Task<Result<DumpAnalysisDto>> HandleAsync(AnalyzeDumpCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.DumpId) || command.DumpId.Length > MaxDumpIdLength)
        {
            var errors = new ValidationErrors();
            errors.Add("dumpId", $"Dump id is required and at most {MaxDumpIdLength} characters.");
            return errors.ToError();
        }

        if (await systems.FindAsync(command.Sid, cancellationToken) is not { } system)
        {
            return Error.NotFound("system_not_found", $"System {command.Sid} is not managed.");
        }

        if (await sap.GetShortDumpAsync(system.Sid, command.DumpId.Trim(), cancellationToken) is not { } detail)
        {
            return Error.NotFound("dump_not_found", $"Dump {command.DumpId} does not exist in {system.Sid}.");
        }

        DumpAnalysis? analysis = null;
        try
        {
            analysis = await aiAnalyzer.AnalyzeDumpAsync(detail, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The dump itself is still useful without the LLM.
            LogAiUnavailable(ex, system.Sid, detail.Dump.Id);
        }

        return new DumpAnalysisDto(system.Sid, DumpDto.From(detail.Dump), analysis, AiAvailable: analysis is not null);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI analysis of dump {DumpId} on {Sid} unavailable, returning the dump without analysis")]
    private partial void LogAiUnavailable(Exception exception, string sid, string dumpId);
}
