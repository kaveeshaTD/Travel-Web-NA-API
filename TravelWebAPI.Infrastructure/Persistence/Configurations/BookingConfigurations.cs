using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", t =>
        {
            t.HasEnumCheck<Booking, BookingStatus>("bookings", "status");
            t.HasCheckConstraint("ck_bookings_adults", "adults >= 1");
            t.HasCheckConstraint("ck_bookings_children", "children >= 0");
            t.HasCheckConstraint("ck_bookings_bags", "bags >= 0");
            t.HasCheckConstraint("ck_bookings_price_lkr", "price_lkr IS NULL OR price_lkr >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.Reference).HasMaxLength(12);
        builder.Property(x => x.ServiceId).HasMaxLength(100);
        builder.Property(x => x.PackageId).HasMaxLength(100);
        builder.Property(x => x.VehicleId).HasMaxLength(100);
        builder.Property(x => x.FlightNumber).HasMaxLength(20);
        builder.Property(x => x.PickupLocation).HasMaxLength(250);
        builder.Property(x => x.DropoffLocation).HasMaxLength(250);
        builder.Property(x => x.Children).HasDefaultValue(0);
        builder.Property(x => x.Bags).HasDefaultValue(0);
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.Whatsapp).HasMaxLength(40);
        builder.Property(x => x.Country).HasMaxLength(80);
        builder.Property(x => x.TripPlanJson).HasColumnName("trip_plan").HasColumnType("jsonb");
        builder.Property(x => x.Status).HasWireConversion().HasDefaultValue(BookingStatus.Pending);
        builder.Property(x => x.PriceLkr).HasPrecision(12, 2);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.Reference).IsUnique();
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
        builder.HasIndex(x => x.TravelDate);
        builder.HasIndex(x => x.Email);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Bookings)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Service)
            .WithMany()
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Package)
            .WithMany()
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Vehicle)
            .WithMany()
            .HasForeignKey(x => x.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t =>
        {
            t.HasEnumCheck<Payment, CurrencyCode>("payments", "currency");
            t.HasEnumCheck<Payment, PaymentMethod>("payments", "method");
            t.HasEnumCheck<Payment, PaymentStatus>("payments", "status");
            t.HasCheckConstraint("ck_payments_amount", "amount > 0");
            t.HasCheckConstraint("ck_payments_exchange_rate", "exchange_rate > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.Property(x => x.Currency).HasWireConversion(3);
        builder.Property(x => x.ExchangeRate).HasPrecision(14, 6);
        builder.Property(x => x.AmountLkr).HasPrecision(12, 2);
        builder.Property(x => x.Method).HasWireConversion();
        builder.Property(x => x.Status).HasWireConversion();
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.Gateway).HasMaxLength(40);
        builder.Property(x => x.GatewayReference).HasMaxLength(120);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.BookingId);
        builder.HasIndex(x => x.PaidAt);
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Booking)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> builder)
    {
        builder.ToTable("contact_messages", t => t.HasEnumCheck<ContactMessage, MessageStatus>("contact_messages", "status"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.Status).HasWireConversion().HasDefaultValue(MessageStatus.New);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}

internal sealed class EmailLogConfiguration : IEntityTypeConfiguration<EmailLog>
{
    public void Configure(EntityTypeBuilder<EmailLog> builder)
    {
        builder.ToTable("email_logs", t =>
        {
            t.HasEnumCheck<EmailLog, EmailKind>("email_logs", "kind");
            t.HasEnumCheck<EmailLog, EmailDeliveryStatus>("email_logs", "delivery_status");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.Kind).HasWireConversion();
        builder.Property(x => x.FromAddress).HasMaxLength(254);
        builder.Property(x => x.ToAddress).HasMaxLength(254);
        builder.Property(x => x.Subject).HasMaxLength(250);
        builder.Property(x => x.DeliveryStatus).HasWireConversion().HasDefaultValue(EmailDeliveryStatus.Queued);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(120);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.BookingId);
        builder.HasIndex(x => x.ContactMessageId);
        builder.HasIndex(x => new { x.DeliveryStatus, x.CreatedAt });
        builder.HasIndex(x => x.CreatedAt);

        builder.HasOne(x => x.Booking)
            .WithMany(x => x.EmailLogs)
            .HasForeignKey(x => x.BookingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ContactMessage)
            .WithMany(x => x.EmailLogs)
            .HasForeignKey(x => x.ContactMessageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
