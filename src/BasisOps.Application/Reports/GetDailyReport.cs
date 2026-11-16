using BasisOps.Application.Abstractions;
using BasisOps.Application.Findings;
using BasisOps.Application.Ports;
using Microsoft.Extensions.Logging;

namespace BasisOps.Application.Reports;

public sealed record GetDailyReportQuery(string Sid);

/// <param name="AiAvailable">False when the LLM could not be reached; the rule-based list is still complete.</param>
public sealed record DailyReportDto(
    string Sid,
    DateTimeOffset GeneratedAt,
    HealthStatus Status,
    SeverityCounts Counts,
    IReadOnlyList<FindingDto> Findings,
    string? AiSummary,
    bool AiAvailable);

public sealed partial class GetDailyReportHandler(
    ISystemRegistry systems,
    IFindingRepository findings,
    IAiAnalyzer aiAnalyzer,
    TimeProvider timeProvider,
    ILogger<GetDailyReportHandler> logger) : IQueryHandler<GetDailyReportQuery, DailyReportDto>
{
    public async Task<Result<DailyReportDto>> HandleAsync(GetDailyReportQuery query, CancellationToken cancellationToken)
    {
        if (await systems.FindAsync(query.Sid, cancellationToken) is not { } system)
        {
            return Error.NotFound("system_not_found", $"System {query.Sid} is not managed.");
        }

        var open = (await findings.GetOpenAsync(system.Sid, cancellationToken))
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Category)
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .ToList();

        string? summary = null;
        try
        {
            summary = await aiAnalyzer.SummarizeDailyAsync(system.Sid, open, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The report must never depend on the LLM: fall back to the rule-based list.
            LogAiUnavailable(ex, system.Sid);
        }

        var counts = SeverityCounts.From(open);
        return new DailyReportDto(
            system.Sid,
            timeProvider.GetUtcNow(),
            counts.ToHealthStatus(),
            counts,
            [.. open.Select(FindingDto.From)],
            string.IsNullOrWhiteSpace(summary) ? null : summary,
            AiAvailable: !string.IsNullOrWhiteSpace(summary));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI summary for {Sid} unavailable, returning rule-based report")]
    private partial void LogAiUnavailable(Exception exception, string sid);
}
