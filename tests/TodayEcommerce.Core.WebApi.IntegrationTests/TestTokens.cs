using System.Net.Http.Headers;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TodayEcommerce.Core.WebApi.IntegrationTests;

/// <summary>
/// Plays the identity provider in tests: issues tokens with the key the API under test trusts.
/// Deliberately independent of POST /dev/token, which exists only in Development.
/// </summary>
public static class TestTokens
{
    public const string SigningKey = "test-only-signing-key-0123456789-abcdefghijklmnop";
    private const string Issuer = "todayecommerce";      // as in appsettings.json
    private const string Audience = "todayecommerce-api";

    public static string For(Guid userId, string? role = null, DateTime? expiresAtUtc = null, string signingKey = SigningKey)
    {
        var expires = expiresAtUtc ?? DateTime.UtcNow.AddHours(1);
        var claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = userId.ToString() };
        if (role is not null)
        {
            claims["role"] = role;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = claims,
            IssuedAt = expires.AddHours(-2),
            NotBefore = expires.AddHours(-2),
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
        });
    }

    public static HttpClient As(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
