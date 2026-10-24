using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Persistence.Configurations;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(120);
        builder.Property(x => x.Icon).HasMaxLength(40);
        builder.Property(x => x.Image).HasMaxLength(500);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.SortOrder);

        builder.HasMany(x => x.Details)
            .WithOne()
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ServiceDetailConfiguration : IEntityTypeConfiguration<ServiceDetail>
{
    public void Configure(EntityTypeBuilder<ServiceDetail> builder)
    {
        builder.ToTable("service_details");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.ServiceId).HasMaxLength(100);
        builder.Property(x => x.Detail).HasMaxLength(300);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);

        builder.HasIndex(x => new { x.ServiceId, x.SortOrder });
    }
}

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles", t =>
        {
            t.HasEnumCheck<Vehicle, VehicleType>("vehicles", "type");
            t.HasCheckConstraint("ck_vehicles_seats", "seats > 0");
            t.HasCheckConstraint("ck_vehicles_luggage", "luggage >= 0");
            t.HasCheckConstraint("ck_vehicles_price_per_km_lkr", "price_per_km_lkr >= 0");
            t.HasCheckConstraint("ck_vehicles_price_per_day_lkr", "price_per_day_lkr >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(x => x.Type).HasWireConversion();
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Image).HasMaxLength(500);
        builder.Property(x => x.PricePerKmLkr).HasPrecision(10, 2);
        builder.Property(x => x.PricePerDayLkr).HasPrecision(12, 2);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.SortOrder);
    }
}

internal sealed class DestinationConfiguration : IEntityTypeConfiguration<Destination>
{
    public void Configure(EntityTypeBuilder<Destination> builder)
    {
        builder.ToTable("destinations", t =>
        {
            t.HasCheckConstraint("ck_destinations_lat", "lat BETWEEN 5 AND 10");
            t.HasCheckConstraint("ck_destinations_lng", "lng BETWEEN 79 AND 82.5");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Region).HasMaxLength(80);
        builder.Property(x => x.Lat).HasPrecision(9, 6);
        builder.Property(x => x.Lng).HasPrecision(9, 6);
        builder.Property(x => x.BestTime).HasMaxLength(120);
        builder.Property(x => x.TravelTimeFromAirport).HasMaxLength(80);
        builder.Property(x => x.Featured).HasDefaultValue(false);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.Featured);
        builder.HasIndex(x => x.SortOrder);

        builder.HasMany(x => x.Images)
            .WithOne()
            .HasForeignKey(x => x.DestinationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DestinationImageConfiguration : IEntityTypeConfiguration<DestinationImage>
{
    public void Configure(EntityTypeBuilder<DestinationImage> builder)
    {
        builder.ToTable("destination_images");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.DestinationId).HasMaxLength(100);
        builder.Property(x => x.Src).HasMaxLength(500);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);

        builder.HasIndex(x => new { x.DestinationId, x.SortOrder });
    }
}

internal sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.ToTable("packages", t =>
        {
            t.HasCheckConstraint("ck_packages_duration_days", "duration_days BETWEEN 1 AND 60");
            t.HasCheckConstraint("ck_packages_price_lkr", "price_lkr > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(150);
        builder.Property(x => x.Tagline).HasMaxLength(250);
        builder.Property(x => x.PriceLkr).HasPrecision(12, 2);
        builder.Property(x => x.Image).HasMaxLength(500);
        builder.Property(x => x.Featured).HasDefaultValue(false);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => new { x.Active, x.SortOrder });
        builder.HasIndex(x => x.Featured);

        builder.HasMany(x => x.Destinations)
            .WithOne(x => x.Package)
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.ItineraryDays)
            .WithOne()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Interests)
            .WithOne()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Images)
            .WithOne()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PackageDestinationConfiguration : IEntityTypeConfiguration<PackageDestination>
{
    public void Configure(EntityTypeBuilder<PackageDestination> builder)
    {
        builder.ToTable("package_destinations");

        builder.HasKey(x => new { x.PackageId, x.DestinationId });
        builder.Property(x => x.PackageId).HasMaxLength(100);
        builder.Property(x => x.DestinationId).HasMaxLength(100);

        builder.HasIndex(x => new { x.PackageId, x.Position }).IsUnique();
        builder.HasIndex(x => x.DestinationId);

        builder.HasOne(x => x.Destination)
            .WithMany()
            .HasForeignKey(x => x.DestinationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ItineraryDayConfiguration : IEntityTypeConfiguration<ItineraryDay>
{
    public void Configure(EntityTypeBuilder<ItineraryDay> builder)
    {
        builder.ToTable("itinerary_days");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.PackageId).HasMaxLength(100);
        builder.Property(x => x.Title).HasMaxLength(150);

        builder.HasIndex(x => new { x.PackageId, x.Day }).IsUnique();
    }
}

internal sealed class PackageInterestConfiguration : IEntityTypeConfiguration<PackageInterest>
{
    public void Configure(EntityTypeBuilder<PackageInterest> builder)
    {
        builder.ToTable("package_interests", t => t.HasEnumCheck<PackageInterest, InterestType>("package_interests", "interest"));

        builder.HasKey(x => new { x.PackageId, x.Interest });
        builder.Property(x => x.PackageId).HasMaxLength(100);
        builder.Property(x => x.Interest).HasWireConversion();
    }
}

internal sealed class PackageImageConfiguration : IEntityTypeConfiguration<PackageImage>
{
    public void Configure(EntityTypeBuilder<PackageImage> builder)
    {
        builder.ToTable("package_images");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.PackageId).HasMaxLength(100);
        builder.Property(x => x.Src).HasMaxLength(500);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);

        builder.HasIndex(x => new { x.PackageId, x.SortOrder });
    }
}

internal sealed class PackageItemConfiguration : IEntityTypeConfiguration<PackageItem>
{
    public void Configure(EntityTypeBuilder<PackageItem> builder)
    {
        builder.ToTable("package_items", t => t.HasEnumCheck<PackageItem, PackageItemKind>("package_items", "kind"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.PackageId).HasMaxLength(100);
        builder.Property(x => x.Kind).HasWireConversion();
        builder.Property(x => x.Content).HasMaxLength(300);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);

        builder.HasIndex(x => new { x.PackageId, x.Kind, x.SortOrder });
    }
}

internal sealed class GalleryImageConfiguration : IEntityTypeConfiguration<GalleryImage>
{
    public void Configure(EntityTypeBuilder<GalleryImage> builder)
    {
        builder.ToTable("gallery_images");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(x => x.Src).HasMaxLength(500);
        builder.Property(x => x.Alt).HasMaxLength(250);
        builder.Property(x => x.Caption).HasMaxLength(250);
        builder.Property(x => x.Aspect).HasPrecision(5, 3);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.SortOrder);
    }
}

internal sealed class FaqConfiguration : IEntityTypeConfiguration<Faq>
{
    public void Configure(EntityTypeBuilder<Faq> builder)
    {
        builder.ToTable("faqs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(100).ValueGeneratedNever();
        builder.Property(x => x.Question).HasMaxLength(300);
        builder.Property(x => x.SortOrder).HasDefaultValue(0);

        builder.HasIndex(x => x.SortOrder);
    }
}

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", t =>
        {
            t.HasEnumCheck<Review, ReviewStatus>("reviews", "status");
            t.HasCheckConstraint("ck_reviews_rating", "rating BETWEEN 1 AND 5");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Country).HasMaxLength(80);
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength();
        builder.Property(x => x.Photo).HasMaxLength(500);
        builder.Property(x => x.TripType).HasMaxLength(80);
        builder.Property(x => x.Status).HasWireConversion().HasDefaultValue(ReviewStatus.Pending);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.Status, x.ReviewDate });

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
