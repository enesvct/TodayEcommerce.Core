namespace TodayEcommerce.Core.WebApi.Authentication;

/// <summary>
/// How incoming access tokens are validated. The API never stores passwords or logs users in:
/// it only checks that a token was issued by someone it trusts (a "resource server").
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// A real identity provider (e.g. Keycloak, Auth0). When set, its public signing keys are
    /// downloaded from the provider's metadata — switching to one is a configuration change only.
    /// </summary>
    public string? Authority { get; init; }

    /// <summary>
    /// Shared secret for tokens issued by this API itself (development and tests only). At least 32 bytes.
    /// </summary>
    public string? SigningKey { get; init; }
}
