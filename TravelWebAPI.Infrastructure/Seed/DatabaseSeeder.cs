using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;

namespace TravelWebAPI.Infrastructure.Seed;

public sealed record SeedOptions(bool IncludeSampleActivity = true);

/// <summary>
/// Loads the website content, demo accounts and (optionally) sample bookings from the embedded
/// <c>seed.json</c>. It only fills tables that are empty, so running it again never duplicates or overwrites
/// anything an admin has edited.
/// </summary>
public sealed class DatabaseSeeder(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    ILogger<DatabaseSeeder> logger)
{
    /// <summary>Fixed rate used only to turn the USD prices in the seed file into placeholder LKR prices.</summary>
    public const decimal SeedRateLkrPerUsd = 300m;

    /// <summary>Per-kilometre price (LKR) given to every seeded vehicle.</summary>
    public const decimal SeedPricePerKmLkr = 30m;

    private const string CustomerPassword = "Customer123";

    public async Task SeedAsync(SeedOptions options, CancellationToken cancellationToken = default)
    {
        var seed = Load();

        var users = await SeedUsersAsync(seed, cancellationToken);
        await SeedSiteSettingsAsync(seed, cancellationToken);
        await SeedTravelTipsAsync(seed, cancellationToken);
        await SeedServicesAsync(seed, cancellationToken);
        await SeedVehiclesAsync(seed, cancellationToken);
        await SeedDestinationsAsync(seed, cancellationToken);
        await SeedPackagesAsync(seed, cancellationToken);
        await SeedGalleryAsync(seed, cancellationToken);
        await SeedReviewsAsync(seed, cancellationToken);
        await SeedFaqsAsync(seed, cancellationToken);

        if (options.IncludeSampleActivity)
        {
            await SeedSampleActivityAsync(seed, users, cancellationToken);
        }

        logger.LogInformation("Database seeding finished");
    }

    private static SeedFile Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("seed.json")
            ?? throw new InvalidOperationException("The embedded seed.json was not found.");
        return SeedFile.Load(stream);
    }

    private static decimal ToLkr(decimal usd) => Math.Round(usd * SeedRateLkrPerUsd);

    private static decimal ToLkrRoundedTo100(decimal usd) => Math.Round(usd * SeedRateLkrPerUsd / 100m) * 100m;

    private async Task<Dictionary<string, Guid>> SeedUsersAsync(SeedFile seed, CancellationToken ct)
    {
        var existing = await db.Users.ToDictionaryAsync(u => u.Email, u => u.Id, ct);
        var idMap = new Dictionary<string, Guid>();

        foreach (var item in seed.Users)
        {
            var email = item.Email.Trim().ToLowerInvariant();
            if (existing.TryGetValue(email, out var existingId))
            {
                idMap[item.Id] = existingId;
                continue;
            }

            var user = new User
            {
                Id = Guid.CreateVersion7(),
                Name = item.Name,
                Email = email,
                PasswordHash = passwordHasher.Hash(item.Password),
                Role = EnumWire.FromWire<UserRole>(item.Role),
                Status = EnumWire.FromWire<UserStatus>(item.Status),
                Phone = item.Phone,
                Country = item.Country,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.CreatedAt,
            };
            db.Users.Add(user);
            idMap[item.Id] = user.Id;
            existing[email] = user.Id;
        }

        await db.SaveChangesAsync(ct);
        return idMap;
    }

    private async Task SeedSiteSettingsAsync(SeedFile seed, CancellationToken ct)
    {
        if (await db.SiteSettings.AnyAsync(ct))
        {
            return;
        }

        var s = seed.Settings;
        db.SiteSettings.Add(new SiteSettings
        {
            Name = s.Name,
            Tagline = s.Tagline,
            DriverName = s.DriverName,
            DriverRole = s.DriverRole,
            Phone = s.Phone,
            WhatsappNumber = s.WhatsappNumber,
            Email = s.Email,
            Address = s.Address,
            TrustYears = s.Trust.YearsExperience,
            TrustTrips = s.Trust.TripsCompleted,
            TrustRating = s.Trust.Rating,
            TrustLicensed = s.Trust.Licensed,
            Languages = s.Languages,
            Licenses = s.Licenses,
            Socials = s.Socials,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedTravelTipsAsync(SeedFile seed, CancellationToken ct)
    {
        if (await db.TravelTips.AnyAsync(ct))
        {
            return;
        }

        db.TravelTips.Add(new TravelTips
        {
            RegionsJson = seed.Tips.Regions.GetRawText(),
            InfoJson = seed.Tips.Info.GetRawText(),
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedServicesAsync(SeedFile seed, CancellationToken ct)
    {
        if (await db.Services.AnyAsync(ct))
        {
            return;
        }

        db.Services.AddRange(seed.Services.Select((s, index) => new Service
        {
            Id = s.Id,
            Title = s.Title,
            Description = s.Description,
            Icon = s.Icon,
            Image = s.Image,
            SortOrder = index,
            Details = s.Details.Select((d, i) => new ServiceDetail { Detail = d, SortOrder = i }).ToList(),
        }));
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedVehiclesAsync(SeedFile seed, CancellationToken ct)
    {
        if (await db.Vehicles.AnyAsync(ct))
        {
            return;
        }

        db.Vehicles.AddRange(seed.Vehicles.Select((v, index) => new Vehicle
        {
            Id = v.Id,
            Type = EnumWire.FromWire<VehicleType>(v.Type),
            Name = v.Name,
            Description = v.Description,
            Image = v.Image,
            Seats = v.Seats,
            Luggage = v.Luggage,
            Ac = v.Ac,
            PricePerKmLkr = SeedPricePerKmLkr,
            PricePerDayLkr = ToLkrRoundedTo100(v.PricePerDayUsd),
            SortOrder = index,
        }));
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedDestinationsAsync(SeedFile seed, CancellationToken ct)
    {
        if (await db.Destinations.AnyAsync(ct))
        {
            return;
        }

        db.Destinations.AddRange(seed.Destinations.Select((d, index) => new Destination
        {
            Id = d.Id,
            Name = d.Name,
            Region = d.Region,
            Lat = d.Lat,
            Lng = d.Lng,
            Description = d.Description,
            BestTime = d.BestTime,
            TravelTimeFromAirport = d.TravelTimeFromAirport,
            Featured = d.Featured,
            SortOrder = index,
            Images = d.Images.Select((src, i) => new DestinationImage { Src = src, SortOrder = i }).ToList(),
        }));
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedPackagesAsync(SeedFile seed, CancellationToken ct)
    {
        if (await db.Packages.AnyAsync(ct))
        {
            return;
        }

        foreach (var (p, index) in seed.Packages.Select((p, i) => (p, i)))
        {
            var package = new Package
            {
                Id = p.Id,
                Name = p.Name,
                Tagline = p.Tagline,
                DurationDays = p.DurationDays,
                PriceLkr = ToLkrRoundedTo100(p.PriceUsd),
                Image = p.Image,
                Featured = p.Featured,
                Active = p.Active,
                SortOrder = index,
                Destinations = p.DestinationIds.Select((id, i) => new PackageDestination { DestinationId = id, Position = i }).ToList(),
                ItineraryDays = p.Itinerary.Select(d => new ItineraryDay { Day = d.Day, Title = d.Title, Description = d.Description }).ToList(),
                Interests = p.Interests.Select(i => new PackageInterest { Interest = EnumWire.FromWire<InterestType>(i) }).ToList(),
                Images = p.Gallery.Select((src, i) => new PackageImage { Src = src, SortOrder = i }).ToList(),
            };

            AddItems(package, PackageItemKind.Highlight, p.Highlights);
            AddItems(package, PackageItemKind.Included, p.Included);
            AddItems(package, PackageItemKind.Excluded, p.Excluded);
            db.Packages.Add(package);
        }

        await db.SaveChangesAsync(ct);

        static void AddItems(Package package, PackageItemKind kind, List<string> lines)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                package.Items.Add(new PackageItem { Kind = kind, Content = lines[i], SortOrder = i });
            }
        }
    }

    private async Task SeedGalleryAsync(SeedFile seed, CancellationToken ct)
    {
        if (await db.GalleryImages.AnyAsync(ct))
        {
            return;
        }

        db.GalleryImages.AddRange(seed.Gallery.Select((g, index) => new GalleryImage
        {
            Id = g.Id,
            Src = g.Src,
            Alt = g.Alt,
            Caption = g.Caption,
            Aspect = g.Aspect,
            SortOrder = index,
        }));
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedReviewsAsync(SeedFile seed, CancellationToken ct)
    {
        if (await db.Reviews.AnyAsync(ct))
        {
            return;
        }

        db.Reviews.AddRange(seed.Reviews.Select(r => new Review
        {
            Name = r.Name,
            Country = r.Country,
            CountryCode = r.CountryCode,
            Rating = r.Rating,
            Photo = r.Photo,
            TripType = r.TripType,
            Comment = r.Comment,
            ReviewDate = r.Date,
            Status = EnumWire.FromWire<ReviewStatus>(r.Status),
        }));
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedFaqsAsync(SeedFile seed, CancellationToken ct)
    {
        if (await db.Faqs.AnyAsync(ct))
        {
            return;
        }

        db.Faqs.AddRange(seed.Faqs.Select((f, index) => new Faq
        {
            Id = f.Id,
            Question = f.Question,
            Answer = f.Answer,
            SortOrder = index,
        }));
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedSampleActivityAsync(SeedFile seed, Dictionary<string, Guid> seedUserIds, CancellationToken ct)
    {
        if (await db.Bookings.AnyAsync(ct) || await db.ContactMessages.AnyAsync(ct))
        {
            return;
        }

        // Guests cannot book, so the seed bookings that had no account get a customer account by email.
        var usersByEmail = await db.Users.ToDictionaryAsync(u => u.Email, u => u.Id, ct);
        var bookingIds = new Dictionary<string, Guid>();

        foreach (var b in seed.Bookings)
        {
            Guid userId;
            if (b.UserId is not null && seedUserIds.TryGetValue(b.UserId, out var mapped))
            {
                userId = mapped;
            }
            else
            {
                var email = b.Email.Trim().ToLowerInvariant();
                if (!usersByEmail.TryGetValue(email, out userId))
                {
                    var customer = new User
                    {
                        Id = Guid.CreateVersion7(),
                        Name = b.Name,
                        Email = email,
                        PasswordHash = passwordHasher.Hash(CustomerPassword),
                        Role = UserRole.Customer,
                        Status = UserStatus.Active,
                        Country = b.Country,
                        CreatedAt = b.CreatedAt,
                        UpdatedAt = b.CreatedAt,
                    };
                    db.Users.Add(customer);
                    userId = customer.Id;
                    usersByEmail[email] = userId;
                }
            }

            var booking = new Booking
            {
                Id = Guid.CreateVersion7(),
                Reference = b.Reference,
                UserId = userId,
                ServiceId = b.ServiceId,
                PackageId = b.PackageId,
                VehicleId = b.VehicleId,
                TravelDate = b.Date,
                TravelTime = b.Time,
                FlightNumber = Blank(b.FlightNumber),
                PickupLocation = Blank(b.PickupLocation),
                DropoffLocation = Blank(b.DropoffLocation),
                Adults = b.Adults,
                Children = b.Children,
                Bags = b.Bags,
                Notes = Blank(b.Notes),
                Name = b.Name,
                Email = b.Email,
                Whatsapp = b.Whatsapp,
                Country = b.Country,
                Status = EnumWire.FromWire<BookingStatus>(b.Status),
                PriceLkr = b.PriceUsd is { } usd ? ToLkr(usd) : null,
                AdminNotes = Blank(b.AdminNotes),
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
            };
            db.Bookings.Add(booking);
            bookingIds[b.Id] = booking.Id;
        }

        foreach (var p in seed.Payments.Where(p => bookingIds.ContainsKey(p.BookingId)))
        {
            var currency = EnumWire.FromWire<CurrencyCode>(p.Currency);
            var rate = currency == CurrencyCode.Lkr ? 1m : SeedRateLkrPerUsd;

            db.Payments.Add(new Payment
            {
                BookingId = bookingIds[p.BookingId],
                Amount = p.Amount,
                Currency = currency,
                ExchangeRate = rate,
                AmountLkr = Math.Round(p.Amount * rate, 2),
                Method = EnumWire.FromWire<PaymentMethod>(p.Method),
                Status = EnumWire.FromWire<PaymentStatus>(p.Status),
                PaidAt = p.PaidAt,
                Notes = Blank(p.Note),
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.CreatedAt,
            });
        }

        var messageIds = new Dictionary<string, Guid>();
        foreach (var m in seed.Messages)
        {
            var message = new ContactMessage
            {
                Id = Guid.CreateVersion7(),
                Name = m.Name,
                Email = m.Email,
                Message = m.Message,
                Status = EnumWire.FromWire<MessageStatus>(m.Status),
                CreatedAt = m.CreatedAt,
                RepliedAt = m.RepliedAt,
            };
            db.ContactMessages.Add(message);
            messageIds[m.Id] = message.Id;
        }

        foreach (var e in seed.Emails)
        {
            db.EmailLogs.Add(new EmailLog
            {
                BookingId = e.BookingId is not null && bookingIds.TryGetValue(e.BookingId, out var bookingId) ? bookingId : null,
                ContactMessageId = e.MessageId is not null && messageIds.TryGetValue(e.MessageId, out var messageId) ? messageId : null,
                Kind = EnumWire.FromWire<EmailKind>(e.Kind),
                FromAddress = e.From,
                ToAddress = e.To,
                Subject = e.Subject,
                Body = e.Body,
                DeliveryStatus = EmailDeliveryStatus.Sent,
                SentAt = e.SentAt,
                CreatedAt = e.SentAt,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
