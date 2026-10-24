using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Api.Auth;

/// <summary>Reads the caller from the validated access token of the current request.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
        => IsAuthenticated && Guid.TryParse(Principal!.FindFirst("sub")?.Value, out var id) ? id : null;

    public string? Email => IsAuthenticated ? Principal!.FindFirst("email")?.Value : null;

    public UserRole? Role
        => IsAuthenticated && EnumWire.TryFromWire<UserRole>(Principal!.FindFirst("role")?.Value, out var role) ? role : null;

    public bool IsAdmin => Role == UserRole.Admin;
}
