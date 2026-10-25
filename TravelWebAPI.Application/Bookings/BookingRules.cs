using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Bookings;

/// <summary>The unique, readable code of a booking, for example <c>BK-7K2M9Q</c>.</summary>
public static class BookingReference
{
    public const string Prefix = "BK-";

    /// <summary>Letters and digits without the look-alikes 0, O, 1 and I, so a reference can be read out over the phone.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int Length = 6;

    public static string New()
    {
        Span<char> chars = stackalloc char[Length];
        for (var index = 0; index < Length; index++)
        {
            chars[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return Prefix + new string(chars);
    }

    /// <summary>Whether the text looks like a reference (so a lookup can try the reference column instead of the id).</summary>
    public static bool Looks(string text) => text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}

public static class BookingMoney
{
    /// <summary>Paid payments minus refunds, in LKR. Pending payments are not counted.</summary>
    public static decimal Paid(IEnumerable<Payment> payments)
        => Round(payments.Sum(p => p.Status switch
        {
            PaymentStatus.Paid => p.AmountLkr,
            PaymentStatus.Refunded => -p.AmountLkr,
            _ => 0m,
        }));

    /// <summary>The price minus what was paid, never below zero; null when no price is agreed.</summary>
    public static decimal? Balance(decimal? price, decimal paid)
        => price is null ? null : Round(Math.Max(price.Value - paid, 0m));

    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The price a new booking starts with: the package price; otherwise the middle of the trip plan estimate rounded to the
    /// nearest 500 LKR; otherwise none.
    /// </summary>
    public static decimal? DefaultPrice(decimal? packagePrice, TripPlanDto? plan)
    {
        if (packagePrice is not null)
        {
            return packagePrice;
        }

        if (plan is null)
        {
            return null;
        }

        var middle = (plan.EstimateLowLkr + plan.EstimateHighLkr) / 2m;
        var rounded = decimal.Round(middle / 500m, MidpointRounding.AwayFromZero) * 500m;
        return rounded > 0 ? rounded : null;
    }

    /// <summary>"LKR 99,000", with two decimals only when there are some.</summary>
    public static string Text(decimal amount)
        => "LKR " + amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", CultureInfo.InvariantCulture);
}

public static class TripPlanRules
{
    public const string Message =
        "Check the trip plan: 1 to 60 days, 1 to 40 travellers, distance up to 10,000 km, up to 30 stops, and a low estimate that is not above the high one";

    public static bool IsValid(TripPlanDto plan)
        => plan.Days is >= 1 and <= 60
            && plan.Travelers is >= 1 and <= 40
            && plan.DistanceKm is >= 0 and <= 10_000
            && plan.EstimateLowLkr >= 0
            && plan.EstimateHighLkr <= ContentRules.MaxMoney
            && plan.EstimateLowLkr <= plan.EstimateHighLkr
            && plan.VehicleId.Length <= 100
            && plan.DestinationIds.Count <= 30
            && plan.DestinationIds.All(id => id.Length is > 0 and <= 100)
            && plan.Stops.Count <= 30
            && plan.Stops.All(stop => stop.Id.Length <= 100
                && stop.Name.Length is > 0 and <= 120
                && stop.Lat is >= -90 and <= 90
                && stop.Lng is >= -180 and <= 180);
}

public sealed partial class BookingDraftValidator : AbstractValidator<BookingDraft>
{
    /// <summary>The service that needs a flight number. It is the id (slug) of the airport pickup service.</summary>
    public const string AirportServiceId = "airport";

    public BookingDraftValidator(IClock clock)
    {
        RuleFor(x => x.ServiceId).MinLength(1, "Please choose a service");
        RuleFor(x => x.ServiceId).MaxLength(100, "Please choose a service");
        RuleFor(x => x.PackageId).MaxLength(100, "Unknown package");
        RuleFor(x => x.VehicleId).MaxLength(100, "Unknown vehicle");

        RuleFor(x => x.Date).Must(date => BookingFormats.TryDate(date, out _)).WithMessage("Choose a valid date");
        RuleFor(x => x.Date)
            .Must(date => !BookingFormats.TryDate(date, out var parsed) || parsed >= clock.TodayInSriLanka)
            .WithMessage("The date cannot be in the past");
        RuleFor(x => x.Time).Must(time => BookingFormats.TryTime(time, out _)).WithMessage("Choose a time");

        RuleFor(x => x.FlightNumber).MinLength(1, "Enter your flight number so we can track it").When(x => x.ServiceId == AirportServiceId);
        RuleFor(x => x.FlightNumber).MaxLength(20, "The flight number is too long");
        RuleFor(x => x.PickupLocation).MaxLength(250, "The pickup place is too long");
        RuleFor(x => x.DropoffLocation).MaxLength(250, "The drop-off place is too long");
        RuleFor(x => x.Notes).MaxLength(2000, "The notes are too long");

        RuleFor(x => x.Adults).Must(count => IsWhole(count, 1, 40)).WithMessage("Adults must be 1 to 40");
        RuleFor(x => x.Children).Must(count => IsWhole(count, 0, 40)).WithMessage("Children must be 0 to 40");
        RuleFor(x => x.Bags).Must(count => IsWhole(count, 0, 60)).WithMessage("Bags must be 0 to 60");

        RuleFor(x => x.Name).MinLength(2, "Enter your full name");
        RuleFor(x => x.Name).MaxLength(120, "Your name is too long");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Whatsapp).Must(number => PhonePattern().IsMatch(number)).WithMessage("Enter a phone number with country code");
        RuleFor(x => x.Country).MinLength(2, "Enter your country");
        RuleFor(x => x.Country).MaxLength(80, "The country is too long");

        RuleFor(x => x.TripPlan!).Must(TripPlanRules.IsValid).WithMessage(TripPlanRules.Message).When(x => x.TripPlan is not null);
    }

    private static bool IsWhole(decimal value, int min, int max) => value >= min && value <= max && value == decimal.Truncate(value);

    [GeneratedRegex(@"^\+?[0-9][0-9\s-]{6,19}$")]
    private static partial Regex PhonePattern();
}

public static class BookingFormats
{
    public static bool TryDate(string? text, out DateOnly date)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static bool TryTime(string? text, out TimeOnly time)
        => TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);
}
