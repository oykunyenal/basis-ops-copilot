using BasisOps.Application.Abstractions;
using BasisOps.Application.Ports;
using BasisOps.Domain.Actions;

namespace BasisOps.Application.Actions;

public sealed record GetActionQuery(Guid ActionId);

public sealed record ListActionsQuery(ActionStatus? Status, int Limit = 50);

public sealed class GetActionHandler(IActionRequestRepository actions) : IQueryHandler<GetActionQuery, ActionRequestDto>
{
    public async Task<Result<ActionRequestDto>> HandleAsync(GetActionQuery query, CancellationToken cancellationToken) =>
        await actions.GetAsync(query.ActionId, cancellationToken) is { } request
            ? ActionRequestDto.From(request)
            : Error.NotFound("action_not_found", $"Action {query.ActionId} does not exist.");
}

public sealed class ListActionsHandler(IActionRequestRepository actions) : IQueryHandler<ListActionsQuery, IReadOnlyList<ActionRequestDto>>
{
    public async Task<Result<IReadOnlyList<ActionRequestDto>>> HandleAsync(ListActionsQuery query, CancellationToken cancellationToken)
    {
        if (query.Limit is < 1 or > 200)
        {
            return new ValidationErrors().Add("limit", "Limit must be between 1 and 200.").ToError();
        }

        var list = await actions.ListAsync(query.Status, query.Limit, cancellationToken);
        return list.Select(ActionRequestDto.From).ToList();
    }
}
