using Microsoft.Extensions.Options;
using TravelWebAPI.Api.Configuration;
using TravelWebAPI.Api.Errors;
using TravelWebAPI.Api.Json;
using TravelWebAPI.Infrastructure;

namespace TravelWebAPI.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicyName = "frontends";

    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddOptions<CorsOptions>().BindConfiguration(CorsOptions.Section);
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section);
        services.AddOptions<SeedingOptions>().BindConfiguration(SeedingOptions.Section);
        services.AddOptions<LimitsOptions>().BindConfiguration(LimitsOptions.Section);
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.Section);

        // Connection string is read when the DbContext is first created, so test and deployment overrides apply.
        services.AddInfrastructure(sp => sp.GetRequiredService<IConfiguration>()[ConfigurationValidator.ConnectionStringKey] ?? string.Empty);

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
                    .WithExposedHeaders("X-Correlation-Id", "Location")));
    }
}
