using System.Text.Json;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Tests.Unit;

public sealed class BookingServiceAdminTests
{
    private readonly BookingHarness _h = new();

    public BookingServiceAdminTests() => _h.CurrentUser.RoleValue = UserRole.Admin;

    private static ValidationException Invalid(Func<Task> action) => Assert.Throws<ValidationException>(() => action().GetAwaiter().GetResult());

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static PriceChange? PriceFrom(string json) => JsonSerializer.Deserialize<BookingUpdateRequest>(json, WebOptions)!.PriceLkr;

    // ---------------------------------------------------------------- the price in an edit

    [Fact]
    public void A_missing_price_is_not_a_change_and_null_and_numbers_are()
    {
        Assert.Null(PriceFrom("{}"));
        Assert.Null(PriceFrom("""{"priceLkr":null}""")!.Value);
        Assert.Equal(120_000m, PriceFrom("""{"priceLkr":120000}""")!.Value);
        Assert.Equal(99.5m, PriceFrom("""{"priceLkr":99.5}""")!.Value);
        Assert.Equal(-1m, PriceFrom("""{"priceLkr":"cheap"}""")!.Value);
        Assert.Equal(-1m, PriceFrom("""{"priceLkr":{"a":1}}""")!.Value);
    }

    // ---------------------------------------------------------------- editing

    [Fact]
    public async Task Sending_only_the_notes_changes_only_the_notes()
    {
        var booking = _h.AddBooking(price: 100_000m);
        booking.PickupLocation = "Colombo";

        var updated = await _h.Create().UpdateAsync(booking.Reference, new BookingUpdateRequest(AdminNotes: "  Call the customer  "));

        Assert.Equal("Call the customer", booking.AdminNotes);
        Assert.Equal("Call the customer", updated.AdminNotes);
        Assert.Equal(100_000m, booking.PriceLkr);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal("Colombo", booking.PickupLocation);
        Assert.Equal(new DateOnly(2026, 12, 1), booking.TravelDate);
        Assert.Empty(_h.Logs);
    }

    [Fact]
    public async Task The_price_can_be_set_kept_and_cleared()
    {
        var booking = _h.AddBooking(price: null);
        var service = _h.Create();

        var set = await service.UpdateAsync(booking.Id.ToString(), new BookingUpdateRequest(PriceLkr: new PriceChange(120_000m)));
        var kept = await service.UpdateAsync(booking.Id.ToString(), new BookingUpdateRequest(AdminNotes: "x"));
        var cleared = await service.UpdateAsync(booking.Id.ToString(), new BookingUpdateRequest(PriceLkr: new PriceChange(null)));

        Assert.Equal(120_000m, set.PriceLkr);
        Assert.Equal(120_000m, set.BalanceLkr);
        Assert.Equal(120_000m, kept.PriceLkr);
        Assert.Null(cleared.PriceLkr);
        Assert.Null(cleared.BalanceLkr);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.234)]
    [InlineData(100_000_000)]
    public async Task An_unusable_price_gets_the_price_message(double price)
    {
        var booking = _h.AddBooking();

        var error = Invalid(() => _h.Create().UpdateAsync(booking.Reference, new BookingUpdateRequest(PriceLkr: new PriceChange((decimal)price))));

        Assert.Equal("Enter a valid price", error.Fields["priceLkr"]);
        Assert.Equal(100_000m, booking.PriceLkr);
    }

    [Fact]
    public async Task A_price_of_zero_is_allowed()
    {
        var booking = _h.AddBooking();

        var updated = await _h.Create().UpdateAsync(booking.Reference, new BookingUpdateRequest(PriceLkr: new PriceChange(0m)));

        Assert.Equal(0m, updated.PriceLkr);
    }

    [Fact]
    public async Task The_admin_can_move_a_booking_to_a_past_date_and_change_time_places_flight_and_vehicle()
    {
        var booking = _h.AddBooking();

        var updated = await _h.Create().UpdateAsync(booking.Reference, new BookingUpdateRequest(
            Date: "2026-01-02", Time: "18:45", VehicleId: "toyota-prius", PickupLocation: " Airport ", DropoffLocation: "Galle", FlightNumber: " UL225 "));

        Assert.Equal("2026-01-02", updated.Date);
        Assert.Equal("18:45", updated.Time);
        Assert.Equal("toyota-prius", updated.VehicleId);
        Assert.Equal("Airport", updated.PickupLocation);
        Assert.Equal("Galle", updated.DropoffLocation);
        Assert.Equal("UL225", updated.FlightNumber);
    }

    [Fact]
    public async Task An_empty_text_clears_the_vehicle_and_the_places()
    {
        var booking = _h.AddBooking();
        booking.VehicleId = "toyota-prius";
        booking.PickupLocation = "Colombo";

        var updated = await _h.Create().UpdateAsync(booking.Reference, new BookingUpdateRequest(VehicleId: "", PickupLocation: "  "));

        Assert.Null(booking.VehicleId);
        Assert.Null(booking.PickupLocation);
        Assert.Equal(string.Empty, updated.PickupLocation);
        Assert.Null(updated.VehicleId);
    }

    [Fact]
    public async Task Invalid_values_get_the_contract_messages_and_nothing_changes()
    {
        var booking = _h.AddBooking();

        var error = Invalid(() => _h.Create().UpdateAsync(booking.Reference, new BookingUpdateRequest(
            Status: "done", Date: "tomorrow", Time: "noon", VehicleId: "nope")));

        Assert.Equal("Status must be one of: pending, confirmed, completed, cancelled", error.Fields["status"]);
        Assert.Equal("Choose a valid date", error.Fields["date"]);
        Assert.Equal("Choose a valid time", error.Fields["time"]);
        Assert.Equal("Unknown vehicle", error.Fields["vehicleId"]);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(0, _h.Saves);
        Assert.Empty(_h.Logs);
    }

    [Fact]
    public async Task An_unknown_booking_is_not_found_for_every_admin_operation()
    {
        var service = _h.Create();

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync("BK-ZZZZZZ", new BookingUpdateRequest()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Guid.NewGuid().ToString()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.SendEmailAsync("nope", new BookingEmailRequest("Hello", "Hello there")));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync("BK-ZZZZZZ"));
    }

    // ---------------------------------------------------------------- status emails

    [Fact]
    public async Task Confirming_records_one_email_with_the_date_time_and_agreed_price()
    {
        var booking = _h.AddBooking(price: 120_000m);

        await _h.Create().UpdateAsync(booking.Reference, new BookingUpdateRequest(Status: "confirmed"));

        var email = Assert.Single(_h.Logs);
        Assert.Equal(EmailKind.BookingConfirmed, email.Kind);
        Assert.Equal("emma@example.com", email.ToAddress);
        Assert.Equal(booking.Id, email.BookingId);
        Assert.Equal($"Your booking {booking.Reference} is confirmed", email.Subject);
        Assert.Contains("Private tour", email.Body, StringComparison.Ordinal);
        Assert.Contains("2026-12-01 at 09:30", email.Body, StringComparison.Ordinal);
        Assert.Contains("Agreed price: LKR 120,000.", email.Body, StringComparison.Ordinal);
        Assert.Contains("Nuwan", email.Body, StringComparison.Ordinal);
        Assert.Equal(["saved", "dispatched"], _h.Events);
    }

    [Fact]
    public async Task A_confirmation_without_a_price_does_not_mention_one()
    {
        var booking = _h.AddBooking(price: null);

        await _h.Create().UpdateAsync(booking.Reference, new BookingUpdateRequest(Status: "confirmed"));

        Assert.DoesNotContain("Agreed price", _h.Logs.Single().Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cancelled", EmailKind.BookingCancelled)]
    [InlineData("completed", EmailKind.BookingCompleted)]
    [InlineData("confirmed", EmailKind.BookingConfirmed)]
    public async Task Each_of_the_three_statuses_sends_its_own_kind(string status, EmailKind kind)
    {
        var booking = _h.AddBooking();

        await _h.Create().UpdateAsync(booking.Reference, new BookingUpdateRequest(Status: status));

        Assert.Equal(kind, Assert.Single(_h.Logs).Kind);
    }

    [Fact]
    public async Task Saving_the_same_status_again_or_going_back_to_pending_sends_nothing()
    {
        var booking = _h.AddBooking(status: BookingStatus.Confirmed);
        var service = _h.Create();

        await service.UpdateAsync(booking.Reference, new BookingUpdateRequest(Status: "confirmed", AdminNotes: "again"));
        await service.UpdateAsync(booking.Reference, new BookingUpdateRequest(Status: "pending"));

        Assert.Empty(_h.Logs);
        Assert.Equal(BookingStatus.Pending, booking.Status);
    }

    [Fact]
    public async Task Confirming_again_after_going_back_to_pending_sends_a_new_email()
    {
        var booking = _h.AddBooking();
        var service = _h.Create();

        await service.UpdateAsync(booking.Reference, new BookingUpdateRequest(Status: "confirmed"));
        await service.UpdateAsync(booking.Reference, new BookingUpdateRequest(Status: "pending"));
        await service.UpdateAsync(booking.Reference, new BookingUpdateRequest(Status: "confirmed"));

        Assert.Equal(2, _h.Logs.Count);
    }

    // ---------------------------------------------------------------- detail and visibility

    [Fact]
    public async Task The_admin_detail_has_payments_emails_customer_notes_and_totals()
    {
        var booking = _h.AddBooking(price: 100_000m);
        _h.Payments.Add(new Payment { Id = Guid.NewGuid(), BookingId = booking.Id, Amount = 40_000m, AmountLkr = 40_000m, Currency = CurrencyCode.Lkr, ExchangeRate = 1m, Method = PaymentMethod.Cash, Status = PaymentStatus.Paid, PaidAt = new DateOnly(2026, 10, 1), Notes = "deposit" });
        _h.Payments.Add(new Payment { Id = Guid.NewGuid(), BookingId = booking.Id, Amount = 20_000m, AmountLkr = 20_000m, Currency = CurrencyCode.Lkr, ExchangeRate = 1m, Method = PaymentMethod.Card, Status = PaymentStatus.Pending, PaidAt = new DateOnly(2026, 10, 5) });
        await _h.QueueAsync(new TravelWebAPI.Application.Abstractions.EmailMessage(EmailKind.BookingReceived, "emma@example.com", "Hi", "Body text", booking.Id));

        var detail = await _h.Create().GetAsync(booking.Reference);

        Assert.Equal("internal only", detail.AdminNotes);
        Assert.Equal(40_000m, detail.PaidLkr);
        Assert.Equal(60_000m, detail.BalanceLkr);
        Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 1)], detail.Payments!.Select(p => DateOnly.ParseExact(p.PaidAt, "yyyy-MM-dd")));
        Assert.Equal("deposit", detail.Payments![^1].Note);
        Assert.Equal("LKR", detail.Payments![0].Currency);
        Assert.Single(detail.Emails!);
        Assert.Equal("Emma Wilson", detail.Customer!.Name);
        Assert.Equal("+44 7700 900123", detail.Customer.Phone);
    }

    [Fact]
    public async Task A_customer_never_gets_admin_fields_and_cannot_read_somebody_elses_booking()
    {
        var mine = _h.AddBooking(reference: "BK-MINE22");
        var theirs = _h.AddBooking(userId: BookingHarness.OtherCustomerId, reference: "BK-THEIRS");
        _h.CurrentUser.RoleValue = UserRole.Customer;
        var service = _h.Create();

        var own = await service.GetAsync(mine.Reference);

        Assert.Null(own.AdminNotes);
        Assert.Null(own.Payments);
        Assert.Null(own.Emails);
        Assert.Null(own.Customer);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(theirs.Reference));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(theirs.Id.ToString()));
    }

    [Fact]
    public async Task The_admin_list_has_every_customers_bookings_newest_first_and_mine_only_the_callers()
    {
        _h.AddBooking(reference: "BK-OLDER2", created: _h.Clock.UtcNow.AddHours(-3));
        _h.AddBooking(userId: BookingHarness.OtherCustomerId, reference: "BK-NEWEST", created: _h.Clock.UtcNow.AddHours(-1));
        var service = _h.Create();

        var all = await service.ListAllAsync();
        var mine = await service.ListMineAsync();

        Assert.Equal(["BK-NEWEST", "BK-OLDER2"], all.Select(b => b.Reference));
        Assert.All(all, b => Assert.NotNull(b.AdminNotes));
        Assert.Equal(["BK-OLDER2"], mine.Select(b => b.Reference));
        Assert.All(mine, b => Assert.Null(b.AdminNotes));
    }

    // ---------------------------------------------------------------- delete and email

    [Fact]
    public async Task Delete_removes_the_booking()
    {
        var booking = _h.AddBooking();

        await _h.Create().DeleteAsync(booking.Reference);

        Assert.Empty(_h.Bookings);
        Assert.Equal(1, _h.Saves);
    }

    [Fact]
    public async Task A_custom_email_is_recorded_linked_to_the_booking_and_shows_its_delivery_status()
    {
        var booking = _h.AddBooking();

        var entry = await _h.Create().SendEmailAsync(booking.Reference, new BookingEmailRequest("  About your pickup ", "  We will be at the arrivals hall.  "));

        Assert.Equal("custom", entry.Kind);
        Assert.Equal("emma@example.com", entry.To);
        Assert.Equal("About your pickup", entry.Subject);
        Assert.Equal("We will be at the arrivals hall.", entry.Body);
        Assert.Equal(booking.Id, entry.BookingId);
        Assert.Equal("failed", entry.Status);
        Assert.Equal("No email provider configured", entry.Error);
    }

    [Theory]
    [InlineData("", "Hello there")]
    [InlineData("Hi", "Hello there")]
    [InlineData("About your pickup", "")]
    [InlineData("About your pickup", "ok")]
    public async Task A_custom_email_needs_a_subject_and_a_message_of_three_characters(string subject, string body)
    {
        var booking = _h.AddBooking();

        var error = Invalid(() => _h.Create().SendEmailAsync(booking.Reference, new BookingEmailRequest(subject, body)));

        Assert.True(error.Fields.ContainsKey("subject") || error.Fields.ContainsKey("body"));
        Assert.Empty(_h.Logs);
    }

    [Fact]
    public async Task The_custom_email_messages_are_the_ones_in_the_contract()
    {
        var booking = _h.AddBooking();

        var error = Invalid(() => _h.Create().SendEmailAsync(booking.Reference, new BookingEmailRequest("x", "y")));

        Assert.Equal("Enter a subject", error.Fields["subject"]);
        Assert.Equal("Write a message", error.Fields["body"]);
    }
}
