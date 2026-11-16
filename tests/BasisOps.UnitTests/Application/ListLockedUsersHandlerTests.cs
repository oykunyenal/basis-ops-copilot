using BasisOps.Application.Users;
using BasisOps.Domain.Observations;
using BasisOps.UnitTests.Fakes;

namespace BasisOps.UnitTests.Application;

public class ListLockedUsersHandlerTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeSapGateway _sap = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandleAsync_LockedUsers_ReturnsMostRecentlyLockedFirst()
    {
        _sap.LockedUsers.Add(new LockedUser("CAN", LockReason.Administrator, TestData.Now.AddHours(-5)));
        _sap.LockedUsers.Add(new LockedUser("MEHMET", LockReason.FailedLogons, TestData.Now.AddMinutes(-10)));

        var result = await new ListLockedUsersHandler(_store, _sap).HandleAsync(new ListLockedUsersQuery("PRD"), Ct);

        Assert.Equal(["MEHMET", "CAN"], result.Value.Select(u => u.UserName));
        Assert.Equal(LockReason.FailedLogons, result.Value[0].Reason);
    }

    [Fact]
    public async Task HandleAsync_UnknownSystem_ReturnsNotFound()
    {
        var result = await new ListLockedUsersHandler(_store, _sap).HandleAsync(new ListLockedUsersQuery("XYZ"), Ct);

        Assert.Equal("system_not_found", result.Error?.Code);
    }
}
