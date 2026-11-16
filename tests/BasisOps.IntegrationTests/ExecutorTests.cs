using System.Net;
using System.Net.Http.Json;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Actions;
using BasisOps.Application.Audit;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;
using BasisOps.Domain.Systems;
using Microsoft.Extensions.DependencyInjection;

namespace BasisOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ExecutorTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RequestApproveExecute_ViaApi_EndsSucceededWithFullAuditTrail()
    {
        using var client = factory.CreateApiClient();
        var id = await RequestAndApproveViaApiAsync(client, "FLOW01");

        await DrainAsync(executors: 1);

        var action = await client.GetFromJsonAsync<ActionRequestDto>($"/api/v1/actions/{id}", Json.Options, Ct);
        var audit = await client.GetFromJsonAsync<List<AuditEntryDto>>("/api/v1/audit?target=PRD:FLOW01", Json.Options, Ct);
        Assert.Equal(ActionStatus.Succeeded, action!.Status);
        Assert.Equal("unlocked", action.ResultMessage);
        Assert.Equal(
            ["ActionSucceeded", "ActionExecutionStarted", "ActionApproved", "ActionRequested"],
            audit!.Select(e => e.Action));
    }

    [Fact]
    public async Task ExecuteNext_FourExecutorsInParallel_AppliesEachActionOnce()
    {
        using var client = factory.CreateApiClient();
        string[] users = ["PAR01", "PAR02", "PAR03", "PAR04", "PAR05", "PAR06"];
        var ids = new List<Guid>();
        foreach (var user in users)
        {
            ids.Add(await RequestAndApproveViaApiAsync(client, user));
        }

        await DrainAsync(executors: 4);

        foreach (var user in users)
        {
            Assert.Equal(1, factory.Sap.UnlockCalls[$"PRD:{user}"]);
        }

        foreach (var id in ids)
        {
            var action = await client.GetFromJsonAsync<ActionRequestDto>($"/api/v1/actions/{id}", Json.Options, Ct);
            Assert.Equal(ActionStatus.Succeeded, action!.Status);
            Assert.Equal(1, action.Attempts);
        }
    }

    [Fact]
    public async Task ExecuteNext_ActionAbandonedInExecuting_IsRecoveredAfterLease()
    {
        // An executor claimed the action ten minutes ago and died before recording a result.
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var (request, _) = ActionRequest.Create(
            ActionType.UnlockUser, new SapSystem("PRD", SystemRole.Production), "STALE01", "alice", null, $"test-{Guid.NewGuid():N}", startedAt);
        request.Approve("bob", null, startedAt);
        request.StartExecution(startedAt);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IActionRequestRepository>().Add(request);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);
        }

        await DrainAsync(executors: 1);

        await using var check = factory.Services.CreateAsyncScope();
        var recovered = await check.ServiceProvider.GetRequiredService<IActionRequestRepository>().GetAsync(request.Id, Ct);
        Assert.Equal(ActionStatus.Succeeded, recovered!.Status);
        Assert.Equal(2, recovered.Attempts);
    }

    private async Task<Guid> RequestAndApproveViaApiAsync(HttpClient client, string user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/actions")
        {
            Content = JsonContent.Create(new { type = "UnlockUser", sid = "PRD", target = user, requestedBy = "alice" }),
        };
        request.Headers.Add("Idempotency-Key", $"test-{Guid.NewGuid():N}");
        using var created = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var id = (await created.ReadAsync<ActionRequestDto>(Ct)).Id;

        using var approved = await client.PostAsJsonAsync($"/api/v1/actions/{id}/approve", new { approvedBy = "bob" }, Ct);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        return id;
    }

    // Runs executors (each with its own scope and DbContext, like separate worker instances) until nothing is due.
    private async Task DrainAsync(int executors)
    {
        await Task.WhenAll(Enumerable.Range(0, executors).Select(_ => Task.Run(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<ActionExecutor>();
            while (await executor.ExecuteNextAsync(Ct))
            {
            }
        }, Ct)));
    }
}
