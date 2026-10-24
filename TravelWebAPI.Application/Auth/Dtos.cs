using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Auth;

public sealed record RegisterRequest
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Password { get; init; }
    public string? Phone { get; init; }
    public string? Country { get; init; }
}

public sealed record LoginRequest
{
    public string? Email { get; init; }
    public string? Password { get; init; }
    public bool? Remember { get; init; }
}

/// <summary>Only the fields that are sent are changed. A new password needs the current one.</summary>
public sealed record UpdateProfileRequest
{
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public string? Country { get; init; }
    public string? CurrentPassword { get; init; }
    public string? NewPassword { get; init; }
}

public sealed record ForgotPasswordRequest
{
    public string? Email { get; init; }
}

public sealed record ResetPasswordRequest
{
    public string? Token { get; init; }
    public string? NewPassword { get; init; }
}

/// <summary>The user as the API returns it. Never contains password data.</summary>
public sealed record UserDto(
    Guid Id,
    string Name,
    string Email,
    UserRole Role,
    string Phone,
    string Country,
    UserStatus Status,
    DateTimeOffset CreatedAt)
{
    public static UserDto From(User user) => new(
        user.Id,
        user.Name,
        user.Email,
        user.Role,
        user.Phone ?? string.Empty,
        user.Country ?? string.Empty,
        user.Status,
        user.CreatedAt);
}

/// <summary>What the API sends back as <c>{ token, user }</c>.</summary>
public sealed record AuthResponse(string Token, UserDto User);

/// <summary>A signed-in session: the response body plus the refresh token the controller puts in the cookie.</summary>
public sealed record AuthSession(AuthResponse Response, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// <summary>Outcome of a refresh. When there is no session the controller answers 401, and clears the cookie if asked to.</summary>
public sealed record RefreshResult(AuthSession? Session, bool ClearCookie);

/// <summary>Who is calling, for the refresh-token audit columns.</summary>
public sealed record ClientInfo(string? IpAddress, string? UserAgent);
