using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using TodayEcommerce.Core.Application.Abstractions;

namespace TodayEcommerce.Core.WebApi.Authentication;

public static class AuthenticationSetup
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);
        var jwt = section.Get<JwtOptions>() ?? new JwtOptions();

        // Fail at startup, not on the first request: a missing key is a deployment mistake.
        if (string.IsNullOrWhiteSpace(jwt.Authority) && string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            throw new InvalidOperationException(
                "Configure Jwt:Authority (identity provider) or Jwt:SigningKey (development). See appsettings.Development.example.json.");
        }

        services.Configure<JwtOptions>(section);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep claim names as issued ("sub", "role") instead of the long legacy WS-* URIs.
                options.MapInboundClaims = false;

                if (!string.IsNullOrWhiteSpace(jwt.Authority))
                {
                    options.Authority = jwt.Authority;
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.SigningKey is null ? null : CreateSigningKey(jwt.SigningKey),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30), // default is 5 minutes: expired tokens would live too long
                };
            });

        services.AddAuthorizationBuilder()
            // Secure by default: every endpoint requires a signed-in user unless it says [AllowAnonymous].
            // Forgetting an attribute then closes an endpoint instead of opening it.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        return services;
    }

    internal static SymmetricSecurityKey CreateSigningKey(string signingKey)
    {
        var bytes = Encoding.UTF8.GetBytes(signingKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes for HMAC-SHA256.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}
