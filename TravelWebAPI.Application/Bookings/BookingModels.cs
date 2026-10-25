using System.Text.Json.Serialization;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Application.Bookings;

/// <summary>Settings of bookings (<c>Bookings</c> section).</summary>
public sealed class BookingOptions
{
    public const string Section = "Bookings";

    /// <summary>The most bookings one customer can create in an hour.</summary>
    public int MaxPerUserPerHour { get; set; } = 10;
}

/// <summary>Where the "new booking" notice goes (<c>Email:OwnerEmail</c>). When empty the business email of the settings is used.</summary>
public sealed class OwnerEmailOptions
{
    public const string Section = "Email";

    public string OwnerEmail { get; set; } = string.Empty;
}

public sealed record TripStopRequest(string? Id = null, string? Name = null, decimal? Lat = null, decimal? Lng = null);

/// <summary>The Plan My Trip result sent with a booking. Prices are in LKR.</summary>
public sealed record TripPlanRequest(
    IReadOnlyList<string>? DestinationIds = null,
    decimal? Days = null,
    decimal? Travelers = null,
    string? VehicleId = null,
    IReadOnlyList<TripStopRequest>? Stops = null,
    decimal? DistanceKm = null,
    decimal? EstimateLowLkr = null,
    decimal? EstimateHighLkr = null);

public sealed record TripStopDto(string Id, string Name, decimal Lat, decimal Lng);

public sealed record TripPlanDto(
    IReadOnlyList<string> DestinationIds,
    int Days,
    int Travelers,
    string VehicleId,
    IReadOnlyList<TripStopDto> Stops,
    decimal DistanceKm,
    decimal EstimateLowLkr,
    decimal EstimateHighLkr);

/// <summary>What the booking form sends. Counts are decimals so "1.5" reaches the validator and gets the field's message.</summary>
public sealed record BookingRequest(
    string? ServiceId = null,
    string? PackageId = null,
    string? VehicleId = null,
    string? Date = null,
    string? Time = null,
    string? FlightNumber = null,
    string? PickupLocation = null,
    string? DropoffLocation = null,
    decimal? Adults = null,
    decimal? Children = null,
    decimal? Bags = null,
    string? Notes = null,
    string? Name = null,
    string? Email = null,
    string? Whatsapp = null,
    string? Country = null,
    TripPlanRequest? TripPlan = null);

/// <summary>The trimmed booking that is validated. An empty string means "not given".</summary>
public sealed record BookingDraft(
    string ServiceId,
    string PackageId,
    string VehicleId,
    string Date,
    string Time,
    string FlightNumber,
    string PickupLocation,
    string DropoffLocation,
    decimal Adults,
    decimal Children,
    decimal Bags,
    string Notes,
    string Name,
    string Email,
    string Whatsapp,
    string Country,
    TripPlanDto? TripPlan);

/// <summary>The admin's edit. A field that is not sent keeps its value; <c>PriceLkr</c> is read separately because it can be set to null.</summary>
public sealed record BookingUpdateRequest(
    string? Status = null,
    string? AdminNotes = null,
    string? Date = null,
    string? Time = null,
    string? VehicleId = null,
    string? PickupLocation = null,
    string? DropoffLocation = null,
    string? FlightNumber = null,
    PriceChange? PriceLkr = null);

public sealed record CustomerDto(Guid Id, string Name, string Email, string Phone, string Country);

public sealed record PaymentDto(
    Guid Id,
    Guid BookingId,
    decimal Amount,
    string Currency,
    decimal ExchangeRate,
    decimal AmountLkr,
    string Method,
    string Status,
    string PaidAt,
    string Note,
    DateTimeOffset CreatedAt)
{
    public static PaymentDto Of(Payment p) => new(
        p.Id,
        p.BookingId,
        p.Amount,
        EnumWire.ToWire(p.Currency),
        p.ExchangeRate,
        p.AmountLkr,
        EnumWire.ToWire(p.Method),
        EnumWire.ToWire(p.Status),
        BookingFormats.Date(p.PaidAt),
        p.Notes ?? string.Empty,
        p.CreatedAt);
}

/// <summary>
/// A booking. Optional text is <c>""</c> when empty, <c>date</c> is <c>YYYY-MM-DD</c> and <c>time</c> is <c>HH:mm</c>.
/// <c>AdminNotes</c>, <c>Payments</c>, <c>Emails</c> and <c>Customer</c> are only filled for the admin and are left out of the JSON otherwise.
/// </summary>
public sealed record BookingDto(
    Guid Id,
    string Reference,
    Guid UserId,
    string ServiceId,
    string? PackageId,
    string? VehicleId,
    string Date,
    string Time,
    string FlightNumber,
    string PickupLocation,
    string DropoffLocation,
    int Adults,
    int Children,
    int Bags,
    string Notes,
    string Name,
    string Email,
    string Whatsapp,
    string Country,
    TripPlanDto? TripPlan,
    string Status,
    decimal? PriceLkr,
    decimal PaidLkr,
    decimal? BalanceLkr,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdminNotes { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PaymentDto>? Payments { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EmailLogDto>? Emails { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CustomerDto? Customer { get; init; }
}

public sealed record BookingEmailRequest(string? Subject = null, string? Body = null);

/// <summary>A price in an edit: absent (keep), a number (set) or null (clear). Needs the converter below because a plain property cannot tell absent from null.</summary>
[JsonConverter(typeof(PriceChangeConverter))]
public sealed record PriceChange(decimal? Value);

public sealed class PriceChangeConverter : JsonConverter<PriceChange>
{
    public override PriceChange? Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.Null)
        {
            return new PriceChange(null);
        }

        if (reader.TokenType == System.Text.Json.JsonTokenType.Number && reader.TryGetDecimal(out var number))
        {
            return new PriceChange(number);
        }

        // Anything else (text, true, an object) is not a price: read it away safely, then -1 fails the "0 or more"
        // check with the price message. (Skip() cannot be used here: it throws while the body is still streaming in.)
        using var ignored = System.Text.Json.JsonDocument.ParseValue(ref reader);
        return new PriceChange(-1m);
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, PriceChange value, System.Text.Json.JsonSerializerOptions options)
    {
        // The API description generator calls Write with a null value to find the default of the property.
        if (value?.Value is { } number)
        {
            writer.WriteNumberValue(number);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    public override bool HandleNull => true;
}
