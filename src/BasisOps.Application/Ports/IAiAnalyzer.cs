using BasisOps.Domain.Findings;
using BasisOps.Domain.Observations;

namespace BasisOps.Application.Ports;

public sealed record DumpAnalysis(
    string ProbableCause,
    string Impact,
    IReadOnlyList<string> FirstSteps,
    IReadOnlyList<string> RelatedTransactions);

/// <summary>
/// LLM-backed analysis. Implementations mask user names, hosts and IPs before sending text to the model.
/// Callers must treat any exception as "AI unavailable" and continue without AI.
/// </summary>
public interface IAiAnalyzer
{
    Task<DumpAnalysis> AnalyzeDumpAsync(ShortDumpDetail dump, CancellationToken cancellationToken);

    Task<string> SummarizeDailyAsync(string sid, IReadOnlyList<Finding> openFindings, CancellationToken cancellationToken);
}
