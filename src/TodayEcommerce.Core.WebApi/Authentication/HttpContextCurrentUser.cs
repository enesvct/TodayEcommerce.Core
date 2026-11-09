using Microsoft.IdentityModel.JsonWebTokens;
using TodayEcommerce.Core.Application.Abstractions;

namespace TodayEcommerce.Core.WebApi.Authentication;

/// <summary>Reads the current user from the token that the authentication middleware already validated.</summary>
internal sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId
    {
        get
        {
            var sub = _httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            // Endpoints that need a user are protected by [Authorize]; reaching this without one is a bug,
            // so fail loudly (500 + log) instead of quietly ordering as an empty/unknown customer.
            return Guid.TryParse(sub, out var id)
                ? id
                : throw new InvalidOperationException("No authenticated user with a valid 'sub' claim in this request.");
        }
    }
}
