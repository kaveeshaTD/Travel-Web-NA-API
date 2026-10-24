using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Domain.Entities;

/// <summary>A customer or an admin. Users are deactivated, never deleted.</summary>
public class User : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Stored trimmed and lower-case.</summary>
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Customer;
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<Booking> Bookings { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
    public List<PasswordResetToken> PasswordResetTokens { get; set; } = [];
}

/// <summary>Backs the long-lived refresh token. Only the SHA-256 hash of the token is stored.</summary>
public class RefreshToken : IHasCreatedAt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Next token in the rotation chain, used to detect reuse.</summary>
    public Guid? ReplacedById { get; set; }

    public string? CreatedByIp { get; set; }
    public string? UserAgent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}

/// <summary>One-time token for the forgot / reset password flow.</summary>
public class PasswordResetToken : IHasCreatedAt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
