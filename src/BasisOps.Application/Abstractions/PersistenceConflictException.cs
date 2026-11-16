namespace BasisOps.Application.Abstractions;

/// <summary>
/// The data changed concurrently (optimistic concurrency) or a unique rule was hit by a parallel request.
/// The Api returns 409; the client can reload and retry.
/// </summary>
public sealed class PersistenceConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
