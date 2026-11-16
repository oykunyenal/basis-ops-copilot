using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Observations;

namespace BasisOps.Application.Users;

public sealed record LockedUserDto(string UserName, LockReason Reason, DateTimeOffset LockedAt);

// SU01 / SU10: locked users, read live from the system. Small on purpose: the self-service agent only needs this.
public sealed record ListLockedUsersQuery(string Sid);

public sealed class ListLockedUsersHandler(ISystemRegistry systems, ISapGateway sap)
    : IQueryHandler<ListLockedUsersQuery, IReadOnlyList<LockedUserDto>>
{
    public const int MaxItems = 200;

    public async Task<Result<IReadOnlyList<LockedUserDto>>> HandleAsync(ListLockedUsersQuery query, CancellationToken cancellationToken)
    {
        if (await systems.FindAsync(query.Sid, cancellationToken) is not { } system)
        {
            return Error.NotFound("system_not_found", $"System {query.Sid} is not managed.");
        }

        var users = await sap.GetLockedUsersAsync(system.Sid, cancellationToken);
        List<LockedUserDto> items =
        [
            .. users
                .OrderByDescending(u => u.LockedAt)
                .ThenBy(u => u.UserName, StringComparer.Ordinal)
                .Take(MaxItems)
                .Select(u => new LockedUserDto(u.UserName, u.Reason, u.LockedAt)),
        ];
        return items;
    }
}
