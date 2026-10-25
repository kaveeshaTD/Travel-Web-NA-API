using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Tests.Unit;

public sealed class BookingRulesTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero));
    private readonly BookingDraftValidator _validator;

    public BookingRulesTests() => _validator = new BookingDraftValidator(_clock);

    private static BookingDraft Valid() => new(
        "private-tour", string.Empty, string.Empty, "2026-12-01", "09:30", string.Empty, "Colombo", "Kandy",
        2, 1, 3, "Quiet driver please", "Emma Wilson", "emma@example.com", "+44 7700 900123", "United Kingdom", null);

    private Dictionary<string, string> Check(BookingDraft draft) => _validator.Check(draft);

    private static TripPlanDto Plan() => new(["sigiriya", "kandy"], 4, 2, "toyota-prius", [new TripStopDto("sigiriya", "Sigiriya", 7.95m, 80.76m)], 640m, 40_000m, 50_000m);

    // ---------------------------------------------------------------- reference

    [Fact]
    public void References_have_the_prefix_and_six_characters_from_the_readable_alphabet()
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var reference = BookingReference.New();

            Assert.Matches("^BK-[A-HJ-NP-Z2-9]{6}$", reference);
        }
    }

    [Fact]
    public void References_do_not_repeat_in_practice()
        => Assert.Equal(1000, Enumerable.Range(0, 1000).Select(_ => BookingReference.New()).Distinct().Count());

    [Theory]
    [InlineData("BK-7K2M9Q", true)]
    [InlineData("bk-7k2m9q", true)]
    [InlineData("01a1721a-9660-7553-ab59-115a4175b3f9", false)]
    [InlineData("", false)]
    public void A_reference_is_told_apart_from_an_id(string text, bool expected)
        => Assert.Equal(expected, BookingReference.Looks(text));

    // ---------------------------------------------------------------- validation

    [Fact]
    public void A_valid_draft_has_no_errors()
        => Assert.Empty(Check(Valid()));

    [Fact]
    public void Every_message_is_the_one_in_the_contract()
    {
        var fields = Check(new BookingDraft(
            string.Empty, string.Empty, string.Empty, "2026-10-24", string.Empty, string.Empty, string.Empty, string.Empty,
            0, 41, 61, string.Empty, "E", "nope", "12", "U", null));

        Assert.Equal("Please choose a service", fields["serviceId"]);
        Assert.Equal("The date cannot be in the past", fields["date"]);
        Assert.Equal("Choose a time", fields["time"]);
        Assert.Equal("Adults must be 1 to 40", fields["adults"]);
        Assert.Equal("Children must be 0 to 40", fields["children"]);
        Assert.Equal("Bags must be 0 to 60", fields["bags"]);
        Assert.Equal("Enter your full name", fields["name"]);
        Assert.Equal("Enter a valid email address", fields["email"]);
        Assert.Equal("Enter a phone number with country code", fields["whatsapp"]);
        Assert.Equal("Enter your country", fields["country"]);
    }

    [Theory]
    [InlineData("2026-10-25", true)]
    [InlineData("2026-10-26", true)]
    [InlineData("2027-02-28", true)]
    [InlineData("2026-10-24", false)]
    [InlineData("2020-01-01", false)]
    public void Today_and_later_are_allowed_and_earlier_dates_are_not(string date, bool allowed)
    {
        var fields = Check(Valid() with { Date = date });

        Assert.Equal(allowed, !fields.ContainsKey("date"));
        if (!allowed)
        {
            Assert.Equal("The date cannot be in the past", fields["date"]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("2026-13-01")]
    [InlineData("2026-02-30")]
    [InlineData("01/12/2026")]
    [InlineData("2026-1-5")]
    public void A_date_that_is_not_a_date_gets_the_valid_date_message(string date)
        => Assert.Equal("Choose a valid date", Check(Valid() with { Date = date })["date"]);

    [Theory]
    [InlineData("00:00", true)]
    [InlineData("09:30", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", false)]
    [InlineData("9:30", false)]
    [InlineData("09:60", false)]
    [InlineData("noon", false)]
    [InlineData("", false)]
    public void The_time_is_hh_mm(string time, bool valid)
        => Assert.Equal(valid, !Check(Valid() with { Time = time }).ContainsKey("time"));

    [Fact]
    public void The_airport_service_needs_a_flight_number_and_other_services_do_not()
    {
        var airport = Check(Valid() with { ServiceId = "airport" });
        var withFlight = Check(Valid() with { ServiceId = "airport", FlightNumber = "UL504" });
        var other = Check(Valid() with { ServiceId = "private-tour" });

        Assert.Equal("Enter your flight number so we can track it", airport["flightNumber"]);
        Assert.Empty(withFlight);
        Assert.Empty(other);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(0.5)]
    [InlineData(-1)]
    [InlineData(41)]
    public void Adults_must_be_a_whole_number_from_1_to_40(double adults)
        => Assert.Equal("Adults must be 1 to 40", Check(Valid() with { Adults = (decimal)adults })["adults"]);

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void Adults_at_the_limits_are_accepted(double adults)
        => Assert.Empty(Check(Valid() with { Adults = (decimal)adults }));

    [Theory]
    [InlineData("+94 77 123 4567", true)]
    [InlineData("0771234567", true)]
    [InlineData("+44-7700-900123", true)]
    [InlineData("12345", false)]
    [InlineData("call me", false)]
    [InlineData("+", false)]
    [InlineData("+94 77 123 4567 ext 1 and more digits", false)]
    public void The_whatsapp_number_needs_digits_and_a_sensible_shape(string number, bool valid)
        => Assert.Equal(valid, !Check(Valid() with { Whatsapp = number }).ContainsKey("whatsapp"));

    [Fact]
    public void Over_long_text_is_a_field_message()
    {
        var fields = Check(Valid() with
        {
            Name = new string('n', 121),
            Notes = new string('x', 2001),
            PickupLocation = new string('p', 251),
            DropoffLocation = new string('d', 251),
            FlightNumber = new string('f', 21),
            Country = new string('c', 81),
        });

        Assert.Equal("Your name is too long", fields["name"]);
        Assert.Equal("The notes are too long", fields["notes"]);
        Assert.Equal("The pickup place is too long", fields["pickupLocation"]);
        Assert.Equal("The drop-off place is too long", fields["dropoffLocation"]);
        Assert.Equal("The flight number is too long", fields["flightNumber"]);
        Assert.Equal("The country is too long", fields["country"]);
    }

    // ---------------------------------------------------------------- trip plan

    [Fact]
    public void A_valid_trip_plan_is_accepted()
        => Assert.Empty(Check(Valid() with { TripPlan = Plan() }));

    [Fact]
    public void A_trip_plan_with_a_low_estimate_above_the_high_one_is_rejected()
        => Assert.Equal(TripPlanRules.Message, Check(Valid() with { TripPlan = Plan() with { EstimateLowLkr = 60_000m } })["tripPlan"]);

    [Theory]
    [InlineData(0, 2, 640)]
    [InlineData(61, 2, 640)]
    [InlineData(4, 0, 640)]
    [InlineData(4, 41, 640)]
    [InlineData(4, 2, -1)]
    [InlineData(4, 2, 10_001)]
    public void Trip_plan_numbers_have_limits(int days, int travellers, double distance)
    {
        var plan = Plan() with { Days = days, Travelers = travellers, DistanceKm = (decimal)distance };

        Assert.True(Check(Valid() with { TripPlan = plan }).ContainsKey("tripPlan"));
    }

    [Fact]
    public void A_trip_plan_cannot_have_too_many_stops_or_bad_coordinates()
    {
        var tooMany = Plan() with { Stops = [.. Enumerable.Repeat(new TripStopDto("a", "A", 7m, 80m), 31)] };
        var badLatitude = Plan() with { Stops = [new TripStopDto("a", "A", 95m, 80m)] };
        var noName = Plan() with { Stops = [new TripStopDto("a", string.Empty, 7m, 80m)] };

        Assert.True(Check(Valid() with { TripPlan = tooMany }).ContainsKey("tripPlan"));
        Assert.True(Check(Valid() with { TripPlan = badLatitude }).ContainsKey("tripPlan"));
        Assert.True(Check(Valid() with { TripPlan = noName }).ContainsKey("tripPlan"));
    }

    // ---------------------------------------------------------------- default price

    [Fact]
    public void The_package_price_is_the_default_price()
        => Assert.Equal(99_000m, BookingMoney.DefaultPrice(99_000m, Plan()));

    [Theory]
    [InlineData(40_000, 50_000, 45_000)]
    [InlineData(40_000, 50_100, 45_000)]
    [InlineData(40_100, 50_000, 45_000)]
    [InlineData(41_000, 50_000, 45_500)]
    [InlineData(10_000, 10_000, 10_000)]
    [InlineData(100, 200, 0)]
    public void Without_a_package_the_middle_of_the_estimate_rounded_to_500_is_used(double low, double high, double expected)
    {
        var price = BookingMoney.DefaultPrice(null, Plan() with { EstimateLowLkr = (decimal)low, EstimateHighLkr = (decimal)high });

        Assert.Equal(expected == 0 ? null : (decimal?)expected, price);
    }

    [Fact]
    public void Without_a_package_or_a_plan_there_is_no_default_price()
        => Assert.Null(BookingMoney.DefaultPrice(null, null));

    [Fact]
    public void A_plan_with_a_zero_estimate_gives_no_price()
        => Assert.Null(BookingMoney.DefaultPrice(null, Plan() with { EstimateLowLkr = 0, EstimateHighLkr = 0 }));

    // ---------------------------------------------------------------- totals

    private static Payment Pay(PaymentStatus status, decimal lkr) => new() { Status = status, AmountLkr = lkr };

    [Fact]
    public void Paid_is_paid_payments_minus_refunds_and_ignores_pending_ones()
    {
        var paid = BookingMoney.Paid([Pay(PaymentStatus.Paid, 40_000m), Pay(PaymentStatus.Pending, 20_000m), Pay(PaymentStatus.Refunded, 10_000m)]);

        Assert.Equal(30_000m, paid);
    }

    [Fact]
    public void No_payments_means_nothing_paid()
        => Assert.Equal(0m, BookingMoney.Paid([]));

    [Fact]
    public void Paid_is_rounded_to_two_decimals()
        => Assert.Equal(0.67m, BookingMoney.Paid([Pay(PaymentStatus.Paid, 0.333m), Pay(PaymentStatus.Paid, 0.334m)]));

    [Theory]
    [InlineData(100_000, 40_000, 60_000)]
    [InlineData(100_000, 100_000, 0)]
    [InlineData(100_000, 130_000, 0)]
    [InlineData(100_000, 0, 100_000)]
    public void The_balance_is_the_price_minus_what_was_paid_and_never_below_zero(double price, double paid, double expected)
        => Assert.Equal((decimal)expected, BookingMoney.Balance((decimal)price, (decimal)paid));

    [Fact]
    public void Without_a_price_there_is_no_balance()
        => Assert.Null(BookingMoney.Balance(null, 50_000m));

    [Theory]
    [InlineData(99_000, "LKR 99,000")]
    [InlineData(99_000.5, "LKR 99,000.50")]
    [InlineData(0, "LKR 0")]
    [InlineData(1234567.89, "LKR 1,234,567.89")]
    public void Money_is_written_with_two_decimals_only_when_there_are_some(double amount, string expected)
        => Assert.Equal(expected, BookingMoney.Text((decimal)amount));
}
