namespace TodayEcommerce.Core.Application.Common.Exceptions;

/// <summary>
/// The client sent an Idempotency-Key it already used, but with a different request.
/// Returning the old result would silently mislead it, so this is an error (HTTP 422).
/// </summary>
public sealed class IdempotencyKeyReusedException : Exception
{
    public IdempotencyKeyReusedException(string key)
        : base($"Idempotency-Key '{key}' was already used for a different request. Use a new key for a new request.")
    {
    }
}

/// <summary>
/// Saving failed because a concurrent request with the same (user, Idempotency-Key) committed first.
/// Thrown by Infrastructure (translated from the database's unique violation); handled by the use case,
/// which then answers with the winner's result.
/// </summary>
public sealed class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException(Exception innerException)
        : base("A request with the same Idempotency-Key was saved concurrently.", innerException)
    {
    }
}
