using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Audit;

namespace BasisOps.Application.Audit;

public sealed record GetAuditLogQuery(string? Target, int Limit = 100);

public sealed record AuditEntryDto(
    Guid Id,
    DateTimeOffset Timestamp,
    string Actor,
    string Action,
    string EntityType,
    Guid EntityId,
    string Target,
    string? FromState,
    string? ToState,
    string? Detail)
{
    public static AuditEntryDto From(AuditEntry e) =>
        new(e.Id, e.Timestamp, e.Actor, e.Action, e.EntityType, e.EntityId, e.Target, e.FromState, e.ToState, e.Detail);
}

public sealed class GetAuditLogHandler(IAuditLog auditLog) : IQueryHandler<GetAuditLogQuery, IReadOnlyList<AuditEntryDto>>
{
    public async Task<Result<IReadOnlyList<AuditEntryDto>>> HandleAsync(GetAuditLogQuery query, CancellationToken cancellationToken)
    {
        if (query.Limit is < 1 or > 500)
        {
            return new ValidationErrors().Add("limit", "Limit must be between 1 and 500.").ToError();
        }

        var entries = await auditLog.ListAsync(query.Target, query.Limit, cancellationToken);
        return entries.Select(AuditEntryDto.From).ToList();
    }
}
