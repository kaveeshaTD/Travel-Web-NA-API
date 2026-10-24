using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Api.Configuration;
using TravelWebAPI.Api.Errors;
using TravelWebAPI.Api.Json;
using TravelWebAPI.Application;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth;
using TravelWebAPI.Infrastructure;
using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicyName = "frontends";

    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddOptions<CorsOptions>().BindConfiguration(CorsOptions.Section);
        services.AddOptions<SeedingOptions>().BindConfiguration(SeedingOptions.Section);
        services.AddOptions<LimitsOptions>().BindConfiguration(LimitsOptions.Section);
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.Section);
        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.Section);
        services.AddOptions<HostingOptions>().BindConfiguration(HostingOptions.Section);
        services.AddOptions<JwtSettings>().BindConfiguration(JwtSettings.Section);
        services.AddOptions<EmailOutboxOptions>().BindConfiguration(EmailOutboxOptions.Section);
        services.AddOptions<AuthOptions>()
            .BindConfiguration(AuthOptions.Section)
            .Configure<IConfiguration>((options, configuration) =>
            {
                var webBaseUrl = configuration[ConfigurationValidator.WebBaseUrlKey];
                if (!string.IsNullOrWhiteSpace(webBaseUrl))
                {
                    options.WebBaseUrl = webBaseUrl;
                }
            });

        // Connection string is read when the DbContext is first created, so test and deployment overrides apply.
        services.AddInfrastructure(sp => sp.GetRequiredService<IConfiguration>()[ConfigurationValidator.ConnectionStringKey] ?? string.Empty);
        services.AddApplication();

        services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new WireEnumJsonConverterFactory()))
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = InvalidModelStateResponse.Create;

                // Empty 4xx results are shaped by UseStatusCodePages instead of becoming ProblemDetails.
                options.SuppressMapClientErrors = true;
            });

        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new WireEnumJsonConverterFactory()));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddApiCors();
        services.AddApiAuthentication();
        services.AddApiRateLimiting();
        services.AddApiForwardedHeaders();
        services.AddApiHealthChecks();
        services.AddOpenApi();

        return services;
    }

    private static void AddApiCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
            .Configure<IOptions<CorsOptions>>((cors, settings) =>
                cors.AddPolicy(CorsPolicyName, policy => policy
                    .WithOrigins(settings.Value.AllowedOrigins)
                    .AllowCredentials()
                    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                    .WithHeaders("Authorization", "Content-Type", "X-Correlation-Id")
                    .WithExposedHeaders("X-Correlation-Id", "Location", "Retry-After")));
    }

    /// <summary>Bearer-token authentication. Without <c>[Authorize]</c> a missing or bad token simply means "anonymous".</summary>
    private static void AddApiAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<RefreshCookie>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.MapInboundClaims = false);

        // Read when the first request needs it, so the key from user-secrets or the environment is used.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwt) =>
            {
                var settings = jwt.Value;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = settings.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = JwtTokenService.RoleClaim,
                    NameClaimType = JwtTokenService.NameClaim,
                };
            });

        services.AddAuthorization();
    }

    private static void AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Auth, context =>
            {
                var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(settings.AuthWindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await ErrorResponse.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    ErrorResponse.DefaultMessage(StatusCodes.Status429TooManyRequests));
            };
        });
    }

    private static void AddApiForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<HostingOptions>>((options, hosting) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;

                // Loopback stays trusted by default; add the reverse proxies named in configuration.
                foreach (var proxy in hosting.Value.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }
            });
    }
}
