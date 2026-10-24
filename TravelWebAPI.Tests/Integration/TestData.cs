using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Tests.Integration;

/// <summary>Minimal valid entities for tests.</summary>
internal static class TestData
{
    public static User User(string email = "tester@example.com") => new()
    {
        Name = "Test User",
        Email = email,
        PasswordHash = "hash",
    };

    public static Service Service(string id = "airport") => new()
    {
        Id = id,
        Title = "Airport transfer",
        Description = "Pick-up",
        Icon = "plane",
        Image = "img:airport",
    };

    public static Destination Destination(string id = "sigiriya") => new()
    {
        Id = id,
        Name = "Sigiriya",
        Region = "Cultural Triangle",
        Lat = 7.95m,
        Lng = 80.76m,
        Description = "Rock fortress",
        BestTime = "Jan-Apr",
        TravelTimeFromAirport = "4 h",
    };

    public static Package Package(string id = "cultural-triangle") => new()
    {
        Id = id,
        Name = "Cultural Triangle",
        Tagline = "Ancient kingdoms",
        DurationDays = 3,
        PriceLkr = 100_000m,
        Image = "img:sigiriya",
    };

    public static Booking Booking(Guid userId, string serviceId, string reference = "BK-ABC123") => new()
    {
        Reference = reference,
        UserId = userId,
        ServiceId = serviceId,
        TravelDate = new DateOnly(2030, 1, 15),
        TravelTime = new TimeOnly(9, 30),
        Adults = 2,
        Name = "Test User",
        Email = "tester@example.com",
        Whatsapp = "+94771234567",
        Country = "Sri Lanka",
        Status = BookingStatus.Pending,
    };

    public static Payment Payment(Guid bookingId) => new()
    {
        BookingId = bookingId,
        Amount = 100m,
        Currency = CurrencyCode.Lkr,
        ExchangeRate = 1m,
        AmountLkr = 100m,
        Method = PaymentMethod.Cash,
        Status = PaymentStatus.Paid,
        PaidAt = new DateOnly(2030, 1, 1),
    };
}
