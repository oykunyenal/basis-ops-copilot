using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;
using BasisOps.Domain.Findings;
using BasisOps.Domain.Systems;
using BasisOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BasisOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PersistenceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveChanges_ActionChangedByAnotherContext_ThrowsPersistenceConflict()
    {
        var now = DateTimeOffset.UtcNow;
        var (request, _) = ActionRequest.Create(
            ActionType.UnlockUser, new SapSystem("PRD", SystemRole.Production), "XMIN01", "alice", null, $"test-{Guid.NewGuid():N}", now);
        await using (var setup = factory.Services.CreateAsyncScope())
        {
            setup.ServiceProvider.GetRequiredService<IActionRequestRepository>().Add(request);
            await setup.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();
        var firstCopy = (await first.ServiceProvider.GetRequiredService<IActionRequestRepository>().GetAsync(request.Id, Ct))!;
        var secondCopy = (await second.ServiceProvider.GetRequiredService<IActionRequestRepository>().GetAsync(request.Id, Ct))!;

        firstCopy.Approve("bob", null, now);
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);

        secondCopy.Reject("carol", "no", now);
        await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task SaveChanges_SecondOpenFindingWithSameKey_ThrowsPersistenceConflict()
    {
        var now = DateTimeOffset.UtcNow;
        var candidate = new FindingCandidate(FindingCategory.Certificate, Severity.Warning, $"CERT:QAS:{Guid.NewGuid():N}", "t", "d");
        await using var scope = factory.Services.CreateAsyncScope();
        var findings = scope.ServiceProvider.GetRequiredService<IFindingRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        findings.AddRange([Finding.Open("QAS", candidate, now)]);
        await unitOfWork.SaveChangesAsync(Ct);
        findings.AddRange([Finding.Open("QAS", candidate, now)]);

        await Assert.ThrowsAsync<PersistenceConflictException>(() => unitOfWork.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task SaveChanges_ResolvedAndNewFindingWithSameKey_KeepsHistory()
    {
        var now = DateTimeOffset.UtcNow;
        var key = $"CERT:QAS:{Guid.NewGuid():N}";
        var candidate = new FindingCandidate(FindingCategory.Certificate, Severity.Warning, key, "t", "d");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BasisOpsDbContext>();
        var resolved = Finding.Open("QAS", candidate, now.AddHours(-2));
        resolved.Resolve(now.AddHours(-1));
        db.Findings.AddRange(resolved, Finding.Open("QAS", candidate, now));

        await db.SaveChangesAsync(Ct);

        Assert.Equal(2, await db.Findings.CountAsync(f => f.Key == key, Ct));
    }
}
