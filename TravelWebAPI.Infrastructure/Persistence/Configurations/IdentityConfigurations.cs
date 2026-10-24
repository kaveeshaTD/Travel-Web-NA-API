using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t =>
        {
            t.HasEnumCheck<User, UserRole>("users", "role");
            t.HasEnumCheck<User, UserStatus>("users", "status");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.Name).HasMaxLength(120);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.PasswordHash).HasMaxLength(255);
        builder.Property(x => x.Role).HasWireConversion().HasDefaultValue(UserRole.Customer);
        builder.Property(x => x.Status).HasWireConversion().HasDefaultValue(UserStatus.Active);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.Country).HasMaxLength(80);
        builder.Property(x => x.FailedLoginCount).HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasIndex(x => x.Role);
        builder.HasIndex(x => x.Status);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.TokenHash).HasMaxLength(128);
        builder.Property(x => x.CreatedByIp).HasMaxLength(45);
        builder.Property(x => x.UserAgent).HasMaxLength(255);
        builder.Property(x => x.Remember).HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ExpiresAt);

        builder.HasOne(x => x.User)
            .WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(x => x.ReplacedById)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasUuidKey();
        builder.Property(x => x.TokenHash).HasMaxLength(128);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.User)
            .WithMany(x => x.PasswordResetTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
