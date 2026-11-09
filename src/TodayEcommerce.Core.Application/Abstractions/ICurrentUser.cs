namespace TodayEcommerce.Core.Application.Abstractions;

/// <summary>
/// The signed-in user making the current request. Use cases take identity from here — never from
/// the request body, which the caller controls ("order as someone else" would be one field away).
/// </summary>
/// <remarks>
/// Implemented in WebApi from the validated token. Application does not know about HTTP or JWT,
/// so the same handlers work behind any other entry point (gRPC, a message consumer, tests).
/// </remarks>
public interface ICurrentUser
{
    /// <summary>The user's id (the token's "sub" claim). Throws when nobody is signed in.</summary>
    Guid UserId { get; }
}
