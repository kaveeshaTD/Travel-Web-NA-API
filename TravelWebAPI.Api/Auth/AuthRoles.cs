namespace TravelWebAPI.Api.Auth;

/// <summary>Role names used in <c>[Authorize(Roles = ...)]</c>. They match the <c>role</c> claim of the access token.</summary>
public static class AuthRoles
{
    public const string Admin = "admin";
    public const string Customer = "customer";
}

public static class RateLimitPolicies
{
    /// <summary>Limit shared by register, login, forgot, reset and refresh.</summary>
    public const string Auth = "auth";

    /// <summary>Limit shared by the public forms: submitting a review and sending a contact message.</summary>
    public const string Submit = "submit";
}
