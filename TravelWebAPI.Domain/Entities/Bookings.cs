using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Domain.Entities;

public class Booking : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }

    /// <summary>Shown to the customer, format BK-XXXXXX.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Guests cannot book, so a customer is always required.</summary>
    public Guid UserId { get; set; }

    public string ServiceId { get; set; } = string.Empty;
    public string? PackageId { get; set; }
    public string? VehicleId { get; set; }
    public DateOnly TravelDate { get; set; }
    public TimeOnly TravelTime { get; set; }
    public string? FlightNumber { get; set; }
    public string? PickupLocation { get; set; }
    public string? DropoffLocation { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public int Bags { get; set; }
    public string? Notes { get; set; }

    // Contact snapshot at booking time.
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Whatsapp { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;

    /// <summary>Raw JSON from the Plan My Trip tool (route, days, travellers, distance, estimate).</summary>
    public string? TripPlanJson { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    /// <summary>Agreed price in LKR.</summary>
    public decimal? PriceLkr { get; set; }

    /// <summary>Internal, never sent to customers.</summary>
    public string? AdminNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public Service? Service { get; set; }
    public Package? Package { get; set; }
    public Vehicle? Vehicle { get; set; }
    public List<Payment> Payments { get; set; } = [];
    public List<EmailLog> EmailLogs { get; set; } = [];
}

public class Payment : IHasCreatedAt, IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }

    /// <summary>In the currency the customer paid.</summary>
    public decimal Amount { get; set; }

    public CurrencyCode Currency { get; set; }

    /// <summary>LKR per 1 unit of <see cref="Currency"/> at entry time (1 for LKR).</summary>
    public decimal ExchangeRate { get; set; } = 1m;

    /// <summary>Amount times rate. This is what counts toward the booking balance.</summary>
    public decimal AmountLkr { get; set; }

    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;
    public DateOnly PaidAt { get; set; }
    public string? Notes { get; set; }
    public string? Gateway { get; set; }
    public string? GatewayReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Booking? Booking { get; set; }
}

public class ContactMessage : IHasCreatedAt
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public MessageStatus Status { get; set; } = MessageStatus.New;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RepliedAt { get; set; }

    public List<EmailLog> EmailLogs { get; set; } = [];
}

/// <summary>Audit trail of every email the system sends.</summary>
public class EmailLog : IHasCreatedAt
{
    public Guid Id { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? ContactMessageId { get; set; }
    public EmailKind Kind { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public EmailDeliveryStatus DeliveryStatus { get; set; } = EmailDeliveryStatus.Queued;
    public string? ProviderMessageId { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Booking? Booking { get; set; }
    public ContactMessage? ContactMessage { get; set; }
}
