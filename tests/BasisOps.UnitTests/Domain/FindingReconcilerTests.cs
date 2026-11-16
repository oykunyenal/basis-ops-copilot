using BasisOps.Domain.Findings;

namespace BasisOps.UnitTests.Domain;

public class FindingReconcilerTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void Reconcile_NewCandidate_OpensFinding()
    {
        var result = FindingReconciler.Reconcile("PRD", [], [Candidate("JOB:PRD:A:1")], Now);

        var opened = Assert.Single(result.Opened);
        Assert.Equal(Now, opened.FirstSeen);
        Assert.Empty(result.Resolved);
    }

    [Fact]
    public void Reconcile_ExistingKeyObservedAgain_UpdatesLastSeenAndSeverity()
    {
        var existing = Finding.Open("PRD", Candidate("DUMP:PRD:TIME_OUT", Severity.Warning), Now.AddHours(-1));

        var result = FindingReconciler.Reconcile("PRD", [existing], [Candidate("DUMP:PRD:TIME_OUT", Severity.Critical)], Now);

        Assert.Empty(result.Opened);
        Assert.Same(existing, Assert.Single(result.Updated));
        Assert.Equal(Now, existing.LastSeen);
        Assert.Equal(Now.AddHours(-1), existing.FirstSeen);
        Assert.Equal(Severity.Critical, existing.Severity);
    }

    [Fact]
    public void Reconcile_KeyNoLongerObserved_ResolvesFinding()
    {
        var existing = Finding.Open("PRD", Candidate("USER:PRD:MEHMET"), Now.AddHours(-1));

        var result = FindingReconciler.Reconcile("PRD", [existing], [], Now);

        Assert.Same(existing, Assert.Single(result.Resolved));
        Assert.Equal(Now, existing.ResolvedAt);
    }

    [Fact]
    public void Reconcile_SameKeyFromTwoRules_OpensOneFindingWithHighestSeverity()
    {
        var result = FindingReconciler.Reconcile("PRD", [], [Candidate("K", Severity.Info), Candidate("K", Severity.Critical)], Now);

        Assert.Equal(Severity.Critical, Assert.Single(result.Opened).Severity);
    }

    private static FindingCandidate Candidate(string key, Severity severity = Severity.Warning) =>
        new(FindingCategory.Job, severity, key, "title", "detail");
}
