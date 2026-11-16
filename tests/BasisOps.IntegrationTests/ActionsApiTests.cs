using System.Net;
using System.Net.Http.Json;
using BasisOps.Application.Actions;
using BasisOps.Application.Audit;
using BasisOps.Domain.Actions;

namespace BasisOps.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ActionsApiTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateAction_NewKey_Returns202WithLocation()
    {
        using var client = factory.CreateApiClient();

        using var response = await CreateUnlockAsync(client, NewKey(), "PRD", "USER01", "alice");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var action = await response.ReadAsync<ActionRequestDto>(Ct);
        Assert.Equal(ActionStatus.Requested, action.Status);
        Assert.Equal($"/api/v1/actions/{action.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateAction_SameKeySameContent_ReturnsSameActionAsReplay()
    {
        using var client = factory.CreateApiClient();
        var key = NewKey();

        using var first = await CreateUnlockAsync(client, key, "PRD", "USER02", "alice", "first reason");
        using var second = await CreateUnlockAsync(client, key, "PRD", "USER02", "alice", "reworded reason");

        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal((await first.ReadAsync<ActionRequestDto>(Ct)).Id, (await second.ReadAsync<ActionRequestDto>(Ct)).Id);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
    }

    [Fact]
    public async Task CreateAction_SameKeyDifferentContent_Returns409()
    {
        using var client = factory.CreateApiClient();
        var key = NewKey();
        using var first = await CreateUnlockAsync(client, key, "PRD", "USER03", "alice");

        using var second = await CreateUnlockAsync(client, key, "PRD", "OTHER03", "alice");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("idempotency_key_reused", await second.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task CreateAction_MissingIdempotencyKey_Returns400()
    {
        using var client = factory.CreateApiClient();

        using var response = await CreateUnlockAsync(client, idempotencyKey: null, "PRD", "USER04", "alice");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await response.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task CreateAction_UnknownActionType_Returns400()
    {
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/actions")
        {
            Content = JsonContent.Create(new { type = "DeleteSystem", sid = "PRD", target = "USER05", requestedBy = "alice" }),
        };
        request.Headers.Add("Idempotency-Key", NewKey());

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAction_UnknownSystem_Returns404()
    {
        using var client = factory.CreateApiClient();

        using var response = await CreateUnlockAsync(client, NewKey(), "XYZ", "USER06", "alice");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("system_not_found", await response.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task ApproveAction_SelfApprovalOnProduction_Returns403()
    {
        using var client = factory.CreateApiClient();
        var action = await CreateUnlockAndReadAsync(client, "PRD", "USER07", "alice");

        using var response = await client.PostAsJsonAsync($"/api/v1/actions/{action.Id}/approve", new { approvedBy = "ALICE" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("four_eyes_violation", await response.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task ApproveAction_SelfApprovalOnQuality_Returns200()
    {
        using var client = factory.CreateApiClient();
        var action = await CreateUnlockAndReadAsync(client, "QAS", "USER08", "alice");

        using var response = await client.PostAsJsonAsync($"/api/v1/actions/{action.Id}/approve", new { approvedBy = "alice" }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RequestApproveGet_SecondPersonApproves_StatusApprovedWithAuditTrail()
    {
        using var client = factory.CreateApiClient();
        var action = await CreateUnlockAndReadAsync(client, "PRD", "USER09", "alice");

        using var approve = await client.PostAsJsonAsync($"/api/v1/actions/{action.Id}/approve", new { approvedBy = "bob", comment = "ok" }, Ct);
        var fetched = await client.GetFromJsonAsync<ActionRequestDto>($"/api/v1/actions/{action.Id}", Json.Options, Ct);
        var audit = await client.GetFromJsonAsync<List<AuditEntryDto>>("/api/v1/audit?target=prd:user09", Json.Options, Ct);

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(ActionStatus.Approved, fetched!.Status);
        Assert.Equal("bob", fetched.DecidedBy);
        Assert.Equal(["ActionApproved", "ActionRequested"], audit!.Select(e => e.Action));
    }

    [Fact]
    public async Task RejectAction_AlreadyApproved_Returns409()
    {
        using var client = factory.CreateApiClient();
        var action = await CreateUnlockAndReadAsync(client, "PRD", "USER10", "alice");
        using var approve = await client.PostAsJsonAsync($"/api/v1/actions/{action.Id}/approve", new { approvedBy = "bob" }, Ct);

        using var reject = await client.PostAsJsonAsync($"/api/v1/actions/{action.Id}/reject", new { rejectedBy = "carol", reason = "too late" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, reject.StatusCode);
        Assert.Equal("invalid_state_transition", await reject.ProblemCodeAsync(Ct));
    }

    [Fact]
    public async Task ApproveAction_TwoApproversInParallel_OneWinsOtherGets409()
    {
        using var client = factory.CreateApiClient();
        var action = await CreateUnlockAndReadAsync(client, "PRD", "USER11", "alice");

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/v1/actions/{action.Id}/approve", new { approvedBy = "bob" }, Ct),
            client.PostAsJsonAsync($"/api/v1/actions/{action.Id}/approve", new { approvedBy = "carol" }, Ct));

        Assert.Equal(
            [HttpStatusCode.OK, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order());
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task GetAction_UnknownId_Returns404()
    {
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync($"/api/v1/actions/{Guid.CreateVersion7()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListActions_FilterByStatus_ReturnsOnlyThatStatusNewestFirst()
    {
        using var client = factory.CreateApiClient();
        var older = await CreateUnlockAndReadAsync(client, "DEV", "USER12", "alice");
        var newer = await CreateUnlockAndReadAsync(client, "DEV", "USER13", "alice");

        var list = await client.GetFromJsonAsync<List<ActionRequestDto>>("/api/v1/actions?status=Requested&limit=200", Json.Options, Ct);

        Assert.All(list!, a => Assert.Equal(ActionStatus.Requested, a.Status));
        Assert.True(list!.FindIndex(a => a.Id == newer.Id) < list.FindIndex(a => a.Id == older.Id));
    }

    private static string NewKey() => $"test-{Guid.NewGuid():N}";

    private static async Task<ActionRequestDto> CreateUnlockAndReadAsync(HttpClient client, string sid, string user, string requestedBy)
    {
        using var response = await CreateUnlockAsync(client, NewKey(), sid, user, requestedBy);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.ReadAsync<ActionRequestDto>(Ct);
    }

    private static async Task<HttpResponseMessage> CreateUnlockAsync(
        HttpClient client, string? idempotencyKey, string sid, string user, string requestedBy, string? reason = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/actions")
        {
            Content = JsonContent.Create(new { type = "UnlockUser", sid, target = user, requestedBy, reason }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, Ct);
    }
}
