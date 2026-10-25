using System.Text.Json;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

public sealed class BookingServiceCreateTests
{
    private readonly BookingHarness _h = new();

    private static ValidationException Invalid(Func<Task> action) => Assert.Throws<ValidationException>(() => action().GetAwaiter().GetResult());

    private static TripPlanRequest Plan() => new(
        ["sigiriya", "kandy"], 4, 2, "toyota-prius", [new TripStopRequest("sigiriya", "Sigiriya", 7.95m, 80.76m)], 640m, 40_000m, 50_000m);

    // ---------------------------------------------------------------- success

    [Fact]
    public async Task A_booking_is_saved_for_the_signed_in_customer_with_a_reference_and_pending_status()
    {
        var receipt = await _h.Create().CreateAsync(BookingHarness.ValidRequest());

        var booking = Assert.Single(_h.Bookings);
        Assert.Equal(receipt.Reference, booking.Reference);
        Assert.Matches("^BK-[A-HJ-NP-Z2-9]{6}$", booking.Reference);
        Assert.Equal(BookingHarness.CustomerId, booking.UserId);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), booking.TravelDate);
        Assert.Equal(new TimeOnly(9, 30), booking.TravelTime);
        Assert.Equal((2, 1, 3), (booking.Adults, booking.Children, booking.Bags));
        Assert.Equal("Colombo", booking.PickupLocation);
        Assert.Null(booking.AdminNotes);
        Assert.Null(booking.PackageId);
        Assert.Null(booking.VehicleId);
        Assert.Null(booking.FlightNumber);
        Assert.Null(booking.PriceLkr);
    }

    [Fact]
    public async Task Text_is_trimmed_the_email_is_lower_cased_and_children_and_bags_default_to_zero()
    {
        var request = BookingHarness.ValidRequest() with
        {
            Name = "  Emma Wilson  ",
            Email = "  Emma@Example.COM ",
            Children = null,
            Bags = null,
            Notes = "   ",
            PickupLocation = " Colombo ",
        };

        await _h.Create().CreateAsync(request);

        var booking = _h.Bookings.Single();
        Assert.Equal("Emma Wilson", booking.Name);
        Assert.Equal("emma@example.com", booking.Email);
        Assert.Equal((0, 0), (booking.Children, booking.Bags));
        Assert.Null(booking.Notes);
        Assert.Equal("Colombo", booking.PickupLocation);
    }

    [Fact]
    public async Task An_admin_can_book_on_their_own_account()
    {
        _h.CurrentUser.RoleValue = UserRole.Admin;

        await _h.Create().CreateAsync(BookingHarness.ValidRequest());

        Assert.Single(_h.Bookings);
    }

    [Fact]
    public async Task Without_a_signed_in_user_nothing_is_saved()
    {
        _h.CurrentUser.UserId = null;

        await Assert.ThrowsAsync<UnauthorizedException>(() => _h.Create().CreateAsync(BookingHarness.ValidRequest()));

        Assert.Empty(_h.Bookings);
        Assert.Empty(_h.Logs);
        Assert.Equal(0, _h.Saves);
    }

    // ---------------------------------------------------------------- emails

    [Fact]
    public async Task Two_emails_are_queued_in_the_same_save_and_sent_after_it()
    {
        await _h.Create().CreateAsync(BookingHarness.ValidRequest());

        var booking = _h.Bookings.Single();
        Assert.Equal([EmailKind.BookingReceived, EmailKind.BookingNotice], _h.Logs.Select(l => l.Kind));
        Assert.All(_h.Logs, log => Assert.Equal(booking.Id, log.BookingId));

        var received = _h.Logs[0];
        Assert.Equal("emma@example.com", received.ToAddress);
        Assert.Equal($"We received your booking request {booking.Reference}", received.Subject);
        Assert.Contains("Private tour", received.Body, StringComparison.Ordinal);
        Assert.Contains("2026-12-01 at 09:30", received.Body, StringComparison.Ordinal);
        Assert.Contains("No payment has been taken", received.Body, StringComparison.Ordinal);
        Assert.Contains(booking.Reference, received.Body, StringComparison.Ordinal);

        var notice = _h.Logs[1];
        Assert.Equal("hello@ceylonroads.example", notice.ToAddress);
        Assert.Contains("emma@example.com", notice.Body, StringComparison.Ordinal);
        Assert.Contains("2 adults, 1 children", notice.Body, StringComparison.Ordinal);

        Assert.Equal(1, _h.Saves);
        Assert.Equal(["saved", "dispatched"], _h.Events);
        Assert.Equal(_h.Logs.Select(l => l.Id), _h.Dispatched);
    }

    [Fact]
    public async Task The_notice_goes_to_the_configured_owner_address_first_then_the_business_email_and_is_skipped_without_either()
    {
        _h.Owner.OwnerEmail = "  owner@example.com ";
        await _h.Create().CreateAsync(BookingHarness.ValidRequest());
        Assert.Equal("owner@example.com", _h.Logs.Single(l => l.Kind == EmailKind.BookingNotice).ToAddress);

        _h.Logs.Clear();
        _h.Owner.OwnerEmail = string.Empty;
        _h.Settings.Items[0].Email = string.Empty;
        await _h.Create().CreateAsync(BookingHarness.ValidRequest());

        Assert.Equal([EmailKind.BookingReceived], _h.Logs.Select(l => l.Kind));
    }

    [Fact]
    public async Task A_failed_delivery_does_not_undo_the_booking()
    {
        await _h.Create().CreateAsync(BookingHarness.ValidRequest());

        Assert.Single(_h.Bookings);
        Assert.All(_h.Logs, log => Assert.Equal(EmailDeliveryStatus.Failed, log.DeliveryStatus));
    }

    // ---------------------------------------------------------------- validation

    [Fact]
    public async Task Field_errors_from_the_rules_and_from_the_lookups_are_reported_together()
    {
        var request = BookingHarness.ValidRequest() with
        {
            ServiceId = "nope",
            PackageId = "hidden",
            VehicleId = "nope",
            Date = "2026-10-01",
            Adults = 0,
        };

        var error = Invalid(() => _h.Create().CreateAsync(request));

        Assert.Equal("Please choose a service", error.Fields["serviceId"]);
        Assert.Equal("Unknown package", error.Fields["packageId"]);
        Assert.Equal("Unknown vehicle", error.Fields["vehicleId"]);
        Assert.Equal("The date cannot be in the past", error.Fields["date"]);
        Assert.Equal("Adults must be 1 to 40", error.Fields["adults"]);
        Assert.Empty(_h.Bookings);
        Assert.Empty(_h.Logs);
        Assert.Equal(0, _h.Saves);
    }

    [Fact]
    public async Task An_unknown_package_is_rejected_too()
    {
        var error = Invalid(() => _h.Create().CreateAsync(BookingHarness.ValidRequest() with { PackageId = "missing" }));

        Assert.Equal("Unknown package", error.Fields["packageId"]);
    }

    [Fact]
    public async Task An_empty_request_reports_the_required_fields()
    {
        var error = Invalid(() => _h.Create().CreateAsync(new BookingRequest()));

        foreach (var field in new[] { "serviceId", "date", "time", "adults", "name", "email", "whatsapp", "country" })
        {
            Assert.True(error.Fields.ContainsKey(field), field);
        }
    }

    [Fact]
    public async Task The_airport_service_needs_a_flight_number()
    {
        var withoutFlight = Invalid(() => _h.Create().CreateAsync(BookingHarness.ValidRequest() with { ServiceId = "airport" }));
        await _h.Create().CreateAsync(BookingHarness.ValidRequest() with { ServiceId = "airport", FlightNumber = "  UL504 " });

        Assert.Equal("Enter your flight number so we can track it", withoutFlight.Fields["flightNumber"]);
        Assert.Equal("UL504", _h.Bookings.Single().FlightNumber);
    }

    // ---------------------------------------------------------------- price and trip plan

    [Fact]
    public async Task A_package_gives_its_price_and_a_published_package_is_accepted()
    {
        await _h.Create().CreateAsync(BookingHarness.ValidRequest() with { PackageId = "cultural-triangle", VehicleId = "toyota-prius" });

        var booking = _h.Bookings.Single();
        Assert.Equal(99_000m, booking.PriceLkr);
        Assert.Equal("cultural-triangle", booking.PackageId);
        Assert.Equal("toyota-prius", booking.VehicleId);
    }

    [Fact]
    public async Task A_trip_plan_without_a_package_gives_the_middle_of_the_estimate()
    {
        await _h.Create().CreateAsync(BookingHarness.ValidRequest() with { TripPlan = Plan() });

        Assert.Equal(45_000m, _h.Bookings.Single().PriceLkr);
    }

    [Fact]
    public async Task The_trip_plan_is_stored_in_its_own_shape()
    {
        await _h.Create().CreateAsync(BookingHarness.ValidRequest() with { TripPlan = Plan() });

        using var json = JsonDocument.Parse(_h.Bookings.Single().TripPlanJson!);
        var root = json.RootElement;
        Assert.Equal(4, root.GetProperty("days").GetInt32());
        Assert.Equal(2, root.GetProperty("travelers").GetInt32());
        Assert.Equal(640m, root.GetProperty("distanceKm").GetDecimal());
        Assert.Equal(40_000m, root.GetProperty("estimateLowLkr").GetDecimal());
        Assert.Equal("Sigiriya", root.GetProperty("stops")[0].GetProperty("name").GetString());
        Assert.Equal(["sigiriya", "kandy"], root.GetProperty("destinationIds").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task An_invalid_trip_plan_is_a_trip_plan_field_error()
    {
        var reversed = Invalid(() => _h.Create().CreateAsync(BookingHarness.ValidRequest() with { TripPlan = Plan() with { EstimateLowLkr = 90_000m } }));
        var fractionalDays = Invalid(() => _h.Create().CreateAsync(BookingHarness.ValidRequest() with { TripPlan = Plan() with { Days = 2.5m } }));

        Assert.True(reversed.Fields.ContainsKey("tripPlan"));
        Assert.True(fractionalDays.Fields.ContainsKey("tripPlan"));
        Assert.Empty(_h.Bookings);
    }

    // ---------------------------------------------------------------- limit and references

    [Fact]
    public async Task The_customer_limit_stops_the_next_booking_and_names_when_to_try_again()
    {
        _h.Options.MaxPerUserPerHour = 3;
        _h.AddBooking(reference: "BK-AAAAA2", created: _h.Clock.UtcNow.AddMinutes(-50));
        _h.AddBooking(reference: "BK-AAAAA3", created: _h.Clock.UtcNow.AddMinutes(-30));
        _h.AddBooking(reference: "BK-AAAAA4", created: _h.Clock.UtcNow.AddMinutes(-10));

        var error = await Assert.ThrowsAsync<TooManyRequestsException>(() => _h.Create().CreateAsync(BookingHarness.ValidRequest()));

        Assert.Equal(TimeSpan.FromMinutes(10), error.RetryAfter);
        Assert.Equal(3, _h.Bookings.Count);
        Assert.Empty(_h.Logs);
    }

    [Fact]
    public async Task Bookings_older_than_an_hour_and_other_customers_do_not_count()
    {
        _h.Options.MaxPerUserPerHour = 2;
        _h.AddBooking(reference: "BK-AAAAA2", created: _h.Clock.UtcNow.AddMinutes(-61));
        _h.AddBooking(reference: "BK-AAAAA3", created: _h.Clock.UtcNow.AddMinutes(-5));
        _h.AddBooking(userId: BookingHarness.OtherCustomerId, reference: "BK-AAAAA4", created: _h.Clock.UtcNow.AddMinutes(-5));
        _h.AddBooking(userId: BookingHarness.OtherCustomerId, reference: "BK-AAAAA5", created: _h.Clock.UtcNow.AddMinutes(-4));

        await _h.Create().CreateAsync(BookingHarness.ValidRequest());

        Assert.Equal(5, _h.Bookings.Count);
    }

    [Fact]
    public async Task A_reference_that_is_already_used_is_replaced_by_a_new_one()
    {
        _h.TakenReferenceAnswers = 2;

        await _h.Create().CreateAsync(BookingHarness.ValidRequest());

        Assert.Equal(3, _h.ReferenceChecks);
        Assert.Single(_h.Bookings);
    }

    [Fact]
    public async Task When_no_free_reference_is_found_the_booking_is_refused_with_a_conflict()
    {
        _h.TakenReferenceAnswers = 100;

        await Assert.ThrowsAsync<ConflictException>(() => _h.Create().CreateAsync(BookingHarness.ValidRequest()));

        Assert.Equal(5, _h.ReferenceChecks);
        Assert.Empty(_h.Bookings);
    }

    [Fact]
    public async Task An_invalid_booking_does_not_count_toward_the_limit_check_or_use_references()
    {
        Invalid(() => _h.Create().CreateAsync(new BookingRequest()));

        Assert.Equal(0, _h.ReferenceChecks);
    }
}
