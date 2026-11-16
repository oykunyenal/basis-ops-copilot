using BasisOps.Api.Http;
using BasisOps.Application.Abstractions;
using BasisOps.Application.Actions;
using BasisOps.Domain.Actions;
using Microsoft.AspNetCore.Mvc;

namespace BasisOps.Api.Endpoints;

public sealed record CreateActionBody(ActionType Type, string Sid, string Target, string RequestedBy, string? Reason);

public sealed record ApproveActionBody(string ApprovedBy, string? Comment);

public sealed record RejectActionBody(string RejectedBy, string Reason);

internal static class ActionEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    public static void MapActionEndpoints(this RouteGroupBuilder api)
    {
        var actions = api.MapGroup("/actions").WithTags("Actions");

        actions.MapPost("/", CreateAsync)
            .WithSummary("Requests an action. Nothing runs before approval. Requires an Idempotency-Key header.")
            .Produces<ActionRequestDto>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        actions.MapPost("/{id:guid}/approve", async (Guid id, ApproveActionBody body, ICommandHandler<ApproveActionCommand, ActionRequestDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ApproveActionCommand(id, body.ApprovedBy, body.Comment), ct)).ToHttpResult())
            .WithSummary("Approves a requested action. On production the requester cannot approve their own request.")
            .Produces<ActionRequestDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        actions.MapPost("/{id:guid}/reject", async (Guid id, RejectActionBody body, ICommandHandler<RejectActionCommand, ActionRequestDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RejectActionCommand(id, body.RejectedBy, body.Reason), ct)).ToHttpResult())
            .WithSummary("Rejects a requested action.")
            .Produces<ActionRequestDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        actions.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetActionQuery, ActionRequestDto> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetActionQuery(id), ct)).ToHttpResult())
            .WithName("GetAction")
            .WithSummary("One action with its current status.")
            .Produces<ActionRequestDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        actions.MapGet("/", async (ActionStatus? status, int? limit, IQueryHandler<ListActionsQuery, IReadOnlyList<ActionRequestDto>> handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListActionsQuery(status, limit ?? 50), ct)).ToHttpResult())
            .WithSummary("Latest actions, optionally filtered by status.")
            .Produces<IReadOnlyList<ActionRequestDto>>()
            .ProducesValidationProblem();
    }

    // 202 + Location for a new request. The same key with the same content answers the same way (replay).
    private static async Task<IResult> CreateAsync(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CreateActionBody body,
        ICommandHandler<RequestActionCommand, RequestActionResult> handler,
        HttpContext httpContext,
        LinkGenerator links,
        CancellationToken ct)
    {
        var command = new RequestActionCommand(
            body.Type, ApiRoutes.NormalizeSid(body.Sid ?? string.Empty), body.Target, body.RequestedBy, body.Reason, idempotencyKey?.Trim() ?? string.Empty);

        var result = await handler.HandleAsync(command, ct);
        return result.ToHttpResult(created =>
        {
            if (!created.Created)
            {
                httpContext.Response.Headers[ReplayedHeader] = "true";
            }

            var location = links.GetPathByName(httpContext, "GetAction", new { id = created.Action.Id });
            return TypedResults.Accepted(location, created.Action);
        });
    }
}
