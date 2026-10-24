using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TravelWebAPI.Tests.Integration.Support;

/// <summary>
/// Starts the real API in memory. The default environment is <c>Staging</c>, which needs no database
/// and has the strict Production checks switched off, so error-contract tests stay fast.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly IDictionary<string, string?>? _settings;

    /// <summary>The single public constructor, as xUnit requires for fixtures.</summary>
    public ApiFactory() : this("Staging", null)
    {
    }

    private ApiFactory(string environment, IDictionary<string, string?>? settings)
    {
        _environment = environment;
        _settings = settings;
    }

    public static ApiFactory With(IDictionary<string, string?> settings, string environment = "Staging")
        => new(environment, settings);

    public CapturingLogSink Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var defaults = new Dictionary<string, string?>
            {
                ["Seed:Enabled"] = "false",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
            };

            configuration.AddInMemoryCollection(defaults);
            if (_settings is not null)
            {
                configuration.AddInMemoryCollection(_settings);
            }
        });

        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ErrorProbeController).Assembly);
            services.AddSingleton<Serilog.Core.ILogEventSink>(Logs);
        });
    }

    /// <summary>Settings that satisfy the Production start-up checks.</summary>
    public static Dictionary<string, string?> ProductionSettings(string connectionString) => new()
    {
        ["ConnectionStrings:Default"] = connectionString,
        ["Jwt:Key"] = "0123456789-0123456789-0123456789-0123",
        ["Cors:AllowedOrigins:0"] = "https://ceylonroads.example",
    };
}
