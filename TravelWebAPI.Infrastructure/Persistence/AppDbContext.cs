using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IClock clock) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceDetail> ServiceDetails => Set<ServiceDetail>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<DestinationImage> DestinationImages => Set<DestinationImage>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageDestination> PackageDestinations => Set<PackageDestination>();
    public DbSet<ItineraryDay> ItineraryDays => Set<ItineraryDay>();
    public DbSet<PackageInterest> PackageInterests => Set<PackageInterest>();
    public DbSet<PackageImage> PackageImages => Set<PackageImage>();
    public DbSet<PackageItem> PackageItems => Set<PackageItem>();
    public DbSet<GalleryImage> GalleryImages => Set<GalleryImage>();
    public DbSet<Faq> Faqs => Set<Faq>();
    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();

    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<TravelTips> TravelTips => Set<TravelTips>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Fills <c>created_at</c> on insert (unless a value such as seed history was supplied)
    /// and <c>updated_at</c> on every insert and update, always in UTC.
    /// </summary>
    private void StampTimestamps()
    {
        var now = clock.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            if (entry is { State: EntityState.Added, Entity: IHasCreatedAt created } && created.CreatedAt == default)
            {
                created.CreatedAt = now;
            }

            if (entry.Entity is IHasUpdatedAt updated)
            {
                if (entry.State == EntityState.Modified || updated.UpdatedAt == default)
                {
                    updated.UpdatedAt = now;
                }
            }
        }
    }
}
