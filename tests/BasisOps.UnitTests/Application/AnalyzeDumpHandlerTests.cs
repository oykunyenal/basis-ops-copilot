using BasisOps.Application.Abstractions;
using BasisOps.Application.Dumps;
using BasisOps.Domain.Observations;
using BasisOps.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BasisOps.UnitTests.Application;

public class AnalyzeDumpHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeSapGateway _sap = new();
    private readonly FakeAiAnalyzer _ai = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandleAsync_AiAvailable_ReturnsDumpWithAnalysis()
    {
        SeedDump("PRD-1");

        var result = await Handler().HandleAsync(new AnalyzeDumpCommand("PRD", "PRD-1"), Ct);

        Assert.True(result.Value.AiAvailable);
        Assert.Equal(_ai.Analysis, result.Value.Analysis);
        Assert.Equal("TIME_OUT", result.Value.Dump.RuntimeError);
    }

    [Fact]
    public async Task HandleAsync_AiThrows_ReturnsDumpWithoutAnalysis()
    {
        SeedDump("PRD-1");
        _ai.Failure = new TimeoutException("The model did not answer within 120 seconds.");

        var result = await Handler().HandleAsync(new AnalyzeDumpCommand("PRD", "PRD-1"), Ct);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AiAvailable);
        Assert.Null(result.Value.Analysis);
    }

    [Fact]
    public async Task HandleAsync_UnknownDump_ReturnsNotFound()
    {
        var result = await Handler().HandleAsync(new AnalyzeDumpCommand("PRD", "PRD-404"), Ct);

        Assert.Equal("dump_not_found", result.Error?.Code);
    }

    [Fact]
    public async Task HandleAsync_UnknownSystem_ReturnsNotFound()
    {
        var result = await Handler().HandleAsync(new AnalyzeDumpCommand("XYZ", "1"), Ct);

        Assert.Equal("system_not_found", result.Error?.Code);
    }

    [Fact]
    public async Task ListDumps_HoursOutOfRange_ReturnsValidationError()
    {
        var handler = new ListDumpsHandler(_store, _sap, new FakeTimeProvider(TestData.Now));

        var result = await handler.HandleAsync(new ListDumpsQuery("PRD", Hours: 0), Ct);

        Assert.Equal(ErrorKind.Validation, result.Error?.Kind);
    }

    [Fact]
    public async Task ListDumps_ReturnsNewestFirst()
    {
        _sap.Dumps.Add(TestData.Dump("TIME_OUT", TimeSpan.FromHours(2)));
        _sap.Dumps.Add(TestData.Dump("MESSAGE_TYPE_X", TimeSpan.FromMinutes(5)));
        var handler = new ListDumpsHandler(_store, _sap, new FakeTimeProvider(TestData.Now));

        var result = await handler.HandleAsync(new ListDumpsQuery("PRD"), Ct);

        Assert.Equal(["MESSAGE_TYPE_X", "TIME_OUT"], result.Value.Select(d => d.RuntimeError));
    }

    private AnalyzeDumpHandler Handler() => new(_store, _sap, _ai, NullLogger<AnalyzeDumpHandler>.Instance);

    private void SeedDump(string id) =>
        _sap.DumpDetails[id] = new ShortDumpDetail(
            new ShortDump(id, "TIME_OUT", "Z_SALES_REPORT", "MEHMET", "prdapp01", TestData.Now, "Time limit exceeded."),
            "What happened",
            "Error analysis");
}
