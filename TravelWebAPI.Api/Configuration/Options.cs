namespace TravelWebAPI.Api.Configuration;

public sealed class CorsOptions
{
    public const string Section = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class SeedingOptions
{
    public const string Section = "Seed";

    public bool Enabled { get; set; }
    public bool IncludeSampleActivity { get; set; } = true;
}

public sealed class LimitsOptions
{
    public const string Section = "Limits";
    public const long DefaultMaxRequestBodyBytes = 1_048_576;

    public long MaxRequestBodyBytes { get; set; } = DefaultMaxRequestBodyBytes;
}

/// <summary>Rate limit of the sensitive auth routes (register, login, forgot, reset, refresh).</summary>
public sealed class RateLimitingOptions
{
    public const string Section = "RateLimiting";

    public int AuthPermitLimit { get; set; } = 10;

    public int AuthWindowSeconds { get; set; } = 60;

    /// <summary>Public forms (a review, a contact message): requests per window per client address.</summary>
    public int SubmitPermitLimit { get; set; } = 5;

    public int SubmitWindowSeconds { get; set; } = 600;
}

/// <summary>Settings for running behind a reverse proxy. Off by default so a spoofed <c>X-Forwarded-For</c> is ignored.</summary>
public sealed class HostingOptions
{
    public const string Section = "Hosting";

    public bool UseForwardedHeaders { get; set; }

    public string[] KnownProxies { get; set; } = [];
}

/// <summary>Email settings. Left empty until a SendGrid account exists; the log-only sender is used meanwhile.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string SendGridApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
}
