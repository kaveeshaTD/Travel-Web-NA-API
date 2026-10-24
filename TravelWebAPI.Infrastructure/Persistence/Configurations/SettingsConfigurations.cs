using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Persistence.Configurations;

internal sealed class SiteSettingsConfiguration : IEntityTypeConfiguration<SiteSettings>
{
    public void Configure(EntityTypeBuilder<SiteSettings> builder)
    {
        builder.ToTable("site_settings", t => t.HasCheckConstraint("ck_site_settings_singleton", "id = 1"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Tagline).HasMaxLength(250);
        builder.Property(x => x.DriverName).HasMaxLength(120);
        builder.Property(x => x.DriverRole).HasMaxLength(120);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.WhatsappNumber).HasMaxLength(20);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.Address).HasMaxLength(250);
        builder.Property(x => x.TrustRating).HasPrecision(2, 1);
        builder.Property(x => x.TrustLicensed).HasMaxLength(120);
        builder.Property(x => x.Languages).HasJsonbConversion();
        builder.Property(x => x.Licenses).HasJsonbConversion();
        builder.Property(x => x.Socials).HasJsonbConversion();
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

internal sealed class TravelTipsConfiguration : IEntityTypeConfiguration<TravelTips>
{
    public void Configure(EntityTypeBuilder<TravelTips> builder)
    {
        builder.ToTable("travel_tips", t => t.HasCheckConstraint("ck_travel_tips_singleton", "id = 1"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.RegionsJson).HasColumnName("regions").HasColumnType("jsonb");
        builder.Property(x => x.InfoJson).HasColumnName("info").HasColumnType("jsonb");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");
    }
}

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        // LKR is the base currency and never has a row.
        builder.ToTable("exchange_rates", t => t.HasEnumCheck("exchange_rates", "currency", CurrencyCode.Usd, CurrencyCode.Eur, CurrencyCode.Gbp));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.Currency).HasWireConversion(3);
        builder.Property(x => x.RateLkr).HasPrecision(14, 6);
        builder.Property(x => x.Source).HasMaxLength(60);
        builder.Property(x => x.FetchedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => new { x.Currency, x.FetchedAt });
    }
}

internal sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
    public void Configure(EntityTypeBuilder<MediaFile> builder)
    {
        builder.ToTable("media_files", t => t.HasCheckConstraint("ck_media_files_size_bytes", "size_bytes <= 5242880"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.FileName).HasMaxLength(120);
        builder.Property(x => x.OriginalName).HasMaxLength(255);
        builder.Property(x => x.ContentType).HasMaxLength(50);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.FileName).IsUnique();
        builder.HasIndex(x => x.UploadedBy);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UploadedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
