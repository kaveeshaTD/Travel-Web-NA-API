namespace TravelWebAPI.Api.Configuration;

/// <summary>
/// Checks required settings at startup. In Production a missing or placeholder value stops the app,
/// naming the setting (never its value).
/// </summary>
public static class ConfigurationValidator
{
    public const string ConnectionStringKey = "ConnectionStrings:Default";
    public const string JwtKeyKey = $"{JwtOptions.Section}:{nameof(JwtOptions.Key)}";
    public const string CorsOriginsKey = $"{CorsOptions.Section}:{nameof(CorsOptions.AllowedOrigins)}";
    public const string MaxBodyKey = $"{LimitsOptions.Section}:{nameof(LimitsOptions.MaxRequestBodyBytes)}";

    private static readonly string[] PlaceholderMarkers = ["CHANGE_ME", "CHANGEME", "YOUR_", "DESIGN-TIME-ONLY"];

    public static void EnsureValid(IConfiguration configuration, IHostEnvironment environment)
    {
        var problems = Validate(configuration, environment);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Invalid configuration: " + string.Join("; ", problems));
        }
    }

    public static List<string> Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        var problems = new List<string>();

        var limits = configuration.GetSection(LimitsOptions.Section).Get<LimitsOptions>() ?? new LimitsOptions();
        if (limits.MaxRequestBodyBytes <= 0)
        {
            problems.Add($"{MaxBodyKey} must be greater than 0");
        }

        if (!environment.IsProduction())
        {
            return problems;
        }

        var connection = configuration[ConnectionStringKey];
        if (IsMissing(connection))
        {
            problems.Add($"{ConnectionStringKey} is missing or still a placeholder");
        }

        var jwtKey = configuration[JwtKeyKey];
        if (IsMissing(jwtKey))
        {
            problems.Add($"{JwtKeyKey} is missing or still a placeholder");
        }
        else if (jwtKey!.Length < JwtOptions.MinimumKeyLength)
        {
            problems.Add($"{JwtKeyKey} must be at least {JwtOptions.MinimumKeyLength} characters");
        }

        var origins = configuration.GetSection(CorsOptions.Section).Get<CorsOptions>()?.AllowedOrigins ?? [];
        if (origins.Length == 0 || origins.Any(o => string.IsNullOrWhiteSpace(o) || o.Contains('*', StringComparison.Ordinal)))
        {
            problems.Add($"{CorsOriginsKey} must list the exact allowed origins (no wildcard)");
        }

        return problems;
    }

    private static bool IsMissing(string? value)
        => string.IsNullOrWhiteSpace(value)
           || PlaceholderMarkers.Any(m => value.Contains(m, StringComparison.OrdinalIgnoreCase));
}
