namespace TravelWebAPI.Application.Auth;

/// <summary>Tunable auth rules. Defaults match the approved design.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public int MaxFailedAttempts { get; set; } = 5;

    public int LockoutMinutes { get; set; } = 15;

    public int RefreshDays { get; set; } = 1;

    public int RefreshDaysRemember { get; set; } = 30;

    /// <summary>A rotated refresh token used again within this many seconds is a harmless double request.</summary>
    public int RefreshGraceSeconds { get; set; } = 10;

    public int ResetTokenMinutes { get; set; } = 60;

    /// <summary>Refresh tokens that expired more than this many days ago are deleted at sign in.</summary>
    public int CleanupAfterExpiryDays { get; set; } = 30;

    /// <summary>Base address of the website, used in the reset link (<c>App:WebBaseUrl</c>).</summary>
    public string WebBaseUrl { get; set; } = "http://localhost:5173";
}
