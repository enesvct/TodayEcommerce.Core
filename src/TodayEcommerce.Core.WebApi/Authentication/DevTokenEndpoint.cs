using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TodayEcommerce.Core.WebApi.Authentication;

/// <summary>
/// DEVELOPMENT ONLY: hands out tokens for a few demo users so the API can be tried from Swagger
/// without an identity provider. In any other environment this endpoint is not mapped at all.
/// </summary>
public static class DevTokenEndpoint
{
    public sealed record DemoUser(Guid Id, string Name, string[] Roles);

    public sealed record DevTokenRequest(string User);

    public sealed record DevTokenResponse(string AccessToken, DateTime ExpiresAtUtc, Guid UserId, string[] Roles);

    // Fixed ids, so "my orders" still belong to the same demo user after a restart.
    public static readonly IReadOnlyDictionary<string, DemoUser> Users = new Dictionary<string, DemoUser>
    {
        ["ayse"] = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Ayşe", []),
        ["mehmet"] = new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Mehmet", []),
        ["admin"] = new(Guid.Parse("99999999-9999-9999-9999-999999999999"), "Admin", [Roles.Admin]),
    };

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public static IEndpointRouteBuilder MapDevTokenEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/token", (DevTokenRequest request, IOptions<JwtOptions> options) =>
            {
                if (!Users.TryGetValue(request.User.Trim().ToLowerInvariant(), out var user))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(request.User)] = [$"Unknown demo user. Use one of: {string.Join(", ", Users.Keys)}."],
                    });
                }

                var jwt = options.Value;
                var now = DateTime.UtcNow;
                var claims = new Dictionary<string, object>
                {
                    [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                    ["name"] = user.Name,
                };
                if (user.Roles.Length > 0)
                {
                    claims["role"] = user.Roles;
                }

                var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                {
                    Issuer = jwt.Issuer,
                    Audience = jwt.Audience,
                    Claims = claims,
                    IssuedAt = now,
                    NotBefore = now,
                    Expires = now + Lifetime,
                    SigningCredentials = new SigningCredentials(
                        AuthenticationSetup.CreateSigningKey(jwt.SigningKey!), SecurityAlgorithms.HmacSha256),
                });

                return Results.Ok(new DevTokenResponse(token, now + Lifetime, user.Id, user.Roles));
            })
            .AllowAnonymous()
            .WithTags("Development")
            .WithSummary("Get a token for a demo user: ayse, mehmet or admin")
            .Produces<DevTokenResponse>()
            .ProducesValidationProblem();

        return app;
    }
}
