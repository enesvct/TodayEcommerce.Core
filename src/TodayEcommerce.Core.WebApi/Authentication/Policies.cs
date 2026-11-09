namespace TodayEcommerce.Core.WebApi.Authentication;

/// <summary>Role names as they appear in the token's "role" claim.</summary>
public static class Roles
{
    public const string Admin = "admin";
}

/// <summary>Authorization policy names used in [Authorize(Policy = ...)].</summary>
public static class Policies
{
    /// <summary>Catalog management: creating products, changing prices.</summary>
    public const string Admin = "admin";
}
