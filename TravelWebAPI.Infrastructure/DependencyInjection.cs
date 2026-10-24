using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Infrastructure.Seed;
using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Func<IServiceProvider, string> connectionString)
    {
        services.AddDbContext<AppDbContext>((sp, options) => options.UseDatabase(connectionString(sp)));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();

        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<ITokenGenerator, TokenGenerator>();
        services.AddSingleton<ILoginAttemptTracker, MemoryLoginAttemptTracker>();

        // Replaced by the SendGrid implementation once an account and key are configured.
        services.AddSingleton<IEmailSender, LogOnlyEmailSender>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();

        services.AddScoped<DatabaseSeeder>();

        return services;
    }
}
