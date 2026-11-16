namespace BasisOps.Application.Abstractions;

// Minimal CQRS contracts (instead of MediatR, which moved to a commercial license).
// Handlers are resolved directly from DI; validation happens at the start of each handler.

public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
