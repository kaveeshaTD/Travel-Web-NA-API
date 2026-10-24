using System.Text.Json;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Infrastructure.Seed;

/// <summary>Shape of <c>seed.json</c>, which was exported from the front-end mock API. Prices in it are USD.</summary>
internal sealed class SeedFile
{
    public SeedSiteSettings Settings { get; set; } = new();
    public SeedTips Tips { get; set; } = new();
    public List<SeedUser> Users { get; set; } = [];
    public List<SeedService> Services { get; set; } = [];
    public List<SeedVehicle> Vehicles { get; set; } = [];
    public List<SeedDestination> Destinations { get; set; } = [];
    public List<SeedPackage> Packages { get; set; } = [];
    public List<SeedGalleryImage> Gallery { get; set; } = [];
    public List<SeedReview> Reviews { get; set; } = [];
    public List<SeedFaq> Faqs { get; set; } = [];
    public List<SeedBooking> Bookings { get; set; } = [];
    public List<SeedPayment> Payments { get; set; } = [];
    public List<SeedMessage> Messages { get; set; } = [];
    public List<SeedEmail> Emails { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static SeedFile Load(Stream stream)
        => JsonSerializer.Deserialize<SeedFile>(stream, Options)
            ?? throw new InvalidOperationException("seed.json is empty.");
}

internal sealed class SeedSiteSettings
{
    public string Name { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string DriverRole { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string WhatsappNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public List<SocialLink> Socials { get; set; } = [];
    public SeedTrust Trust { get; set; } = new();
    public List<string> Languages { get; set; } = [];
    public List<string> Licenses { get; set; } = [];
}

internal sealed class SeedTrust
{
    public int YearsExperience { get; set; }
    public int TripsCompleted { get; set; }
    public decimal Rating { get; set; }
    public string Licensed { get; set; } = string.Empty;
}

internal sealed class SeedTips
{
    public JsonElement Regions { get; set; }
    public JsonElement Info { get; set; }
}

internal sealed class SeedUser
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class SeedService
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Details { get; set; } = [];
    public string Icon { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
}

internal sealed class SeedVehicle
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public int Seats { get; set; }
    public int Luggage { get; set; }
    public bool Ac { get; set; }
    public decimal PricePerDayUsd { get; set; }
}

internal sealed class SeedDestination
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
    public string Description { get; set; } = string.Empty;
    public string BestTime { get; set; } = string.Empty;
    public string TravelTimeFromAirport { get; set; } = string.Empty;
    public List<string> Images { get; set; } = [];
    public bool Featured { get; set; }
}

internal sealed class SeedPackage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public List<string> Highlights { get; set; } = [];
    public List<string> Interests { get; set; } = [];
    public decimal PriceUsd { get; set; }
    public string Image { get; set; } = string.Empty;
    public List<string> Gallery { get; set; } = [];
    public List<SeedItineraryDay> Itinerary { get; set; } = [];
    public List<string> Included { get; set; } = [];
    public List<string> Excluded { get; set; } = [];
    public bool Featured { get; set; }
    public List<string> DestinationIds { get; set; } = [];
    public bool Active { get; set; }
}

internal sealed class SeedItineraryDay
{
    public int Day { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

internal sealed class SeedGalleryImage
{
    public string Id { get; set; } = string.Empty;
    public string Src { get; set; } = string.Empty;
    public string Alt { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public decimal Aspect { get; set; }
}

internal sealed class SeedReview
{
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public short Rating { get; set; }
    public string? Photo { get; set; }
    public string TripType { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string Status { get; set; } = string.Empty;
}

internal sealed class SeedFaq
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

internal sealed class SeedBooking
{
    public string Id { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public string? PackageId { get; set; }
    public string? VehicleId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly Time { get; set; }
    public string? FlightNumber { get; set; }
    public string? PickupLocation { get; set; }
    public string? DropoffLocation { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public int Bags { get; set; }
    public string? Notes { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Whatsapp { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? PriceUsd { get; set; }
    public string? AdminNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class SeedPayment
{
    public string BookingId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly PaidAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class SeedMessage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }
}

internal sealed class SeedEmail
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? BookingId { get; set; }
    public string? MessageId { get; set; }
    public DateTimeOffset SentAt { get; set; }
}
