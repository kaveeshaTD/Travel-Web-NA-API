using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Infrastructure.Services;
using TravelWebAPI.Tests.Unit;

namespace TravelWebAPI.Tests.Integration.Support;

/// <summary>
/// Starts the real API in memory. The default environment is <c>Staging</c>, which needs no database
/// and has the strict Production checks switched off, so error-contract tests stay fast.
/// Time is controlled through <see cref="Clock"/> and password hashing is cheap unless a test asks for the real one.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string JwtKey = "test-signing-key-0123456789-abcdefghijklmnop";

    private readonly string _environment;
    private readonly IDictionary<string, string?>? _settings;
    private readonly bool _realHasher;

    /// <summary>The single public constructor, as xUnit requires for fixtures.</summary>
    public ApiFactory() : this("Staging", null, realHasher: false)
    {
    }

    private ApiFactory(string environment, IDictionary<string, string?>? settings, bool realHasher)
    {
        _environment = environment;
        _settings = settings;
        _realHasher = realHasher;
    }

    public CapturingLogSink Logs { get; } = new();

    /// <summary>The emails the API handed to the email provider, with their real text (the database copy has secrets removed).</summary>
    public CapturingEmailSender Emails { get; } = new();

    /// <summary>Starts at the real current time so tokens stay valid; tests move it forward to test expiry.</summary>
    public FakeClock Clock { get; } = new(DateTimeOffset.UtcNow);

    public static ApiFactory With(IDictionary<string, string?> settings, string environment = "Staging", bool realHasher = false)
        => new(environment, settings, realHasher);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var defaults = new Dictionary<string, string?>
            {
                ["Seed:Enabled"] = "false",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["Jwt:Key"] = JwtKey,
                ["App:WebBaseUrl"] = "http://localhost:5173",

                // Tests share one client address, so the limit is only low in the tests about the limit.
                ["RateLimiting:AuthPermitLimit"] = "100000",
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

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(Clock);

            if (!_realHasher)
            {
                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton<IPasswordHasher>(new PasswordHasher(iterations: 1_000));
            }
        });
    }

    /// <summary>Settings that satisfy the Production start-up checks.</summary>
    public static Dictionary<string, string?> ProductionSettings(string connectionString) => new()
    {
        ["ConnectionStrings:Default"] = connectionString,
        ["Jwt:Key"] = "0123456789-0123456789-0123456789-0123",
        ["Cors:AllowedOrigins:0"] = "https://ceylonroads.example",
        ["App:WebBaseUrl"] = "https://ceylonroads.example",
    };
}
