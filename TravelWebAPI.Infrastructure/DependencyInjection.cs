using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Application.Rates;
using TravelWebAPI.Application.Reviews;
using TravelWebAPI.Domain.Entities;
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

        services.AddScoped<IContentRepository<Faq>, FaqRepository>();
        services.AddScoped<IContentRepository<GalleryImage>, GalleryImageRepository>();
        services.AddScoped<IContentRepository<Vehicle>, VehicleRepository>();
        services.AddScoped<IContentRepository<Service>, ServiceRepository>();
        services.AddScoped<IContentRepository<Destination>, DestinationRepository>();
        services.AddScoped<IContentRepository<Package>, PackageRepository>();
        services.AddScoped<IPackageRepository, PackageRepository>();

        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IContactMessageRepository, ContactMessageRepository>();
        services.AddScoped<IEmailLogRepository, EmailLogRepository>();

        services.AddScoped<IExchangeRateRepository, ExchangeRateRepository>();
        services.AddHttpClient<PrimaryExchangeRateProvider>(ExchangeRateHttpClient.Configure)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, MaxAutomaticRedirections = 3 });
        services.AddHttpClient<FallbackExchangeRateProvider>(ExchangeRateHttpClient.Configure)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseCookies = false, MaxAutomaticRedirections = 3 });

        // The order matters: the primary source is asked first.
        services.AddTransient<IExchangeRateProvider>(sp => sp.GetRequiredService<PrimaryExchangeRateProvider>());
        services.AddTransient<IExchangeRateProvider>(sp => sp.GetRequiredService<FallbackExchangeRateProvider>());

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
