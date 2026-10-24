namespace TravelWebAPI.Api.Configuration;

public sealed class CorsOptions
{
    public const string Section = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

/// <summary>Auth settings. The signing key is only validated here; the auth change uses it to issue tokens.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinimumKeyLength = 32;

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "TravelWebAPI";
    public string Audience { get; set; } = "TravelWebAPI";
    public int AccessTokenMinutes { get; set; } = 15;
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

/// <summary>Email settings. Left empty until a SendGrid account exists; the log-only sender is used meanwhile.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public string SendGridApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
}
