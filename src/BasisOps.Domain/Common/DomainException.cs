namespace BasisOps.Domain.Common;

/// <summary>Base type for violations of domain rules.</summary>
public abstract class DomainException(string message) : Exception(message);

/// <summary>A state machine transition that is not allowed from the current state. Maps to 409 Conflict.</summary>
public sealed class InvalidStateTransitionException(string entity, string from, string to)
    : DomainException($"{entity} cannot move from {from} to {to}.")
{
    public string From { get; } = from;

    public string To { get; } = to;
}

/// <summary>The requester tried to approve their own request on a production system. Maps to 403 Forbidden.</summary>
public sealed class FourEyesViolationException(string sid, string user)
    : DomainException($"{user} cannot approve their own request on production system {sid} (four-eyes principle).");
