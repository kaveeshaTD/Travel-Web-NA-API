using System.Globalization;
using System.Net;
using System.Text.Json;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>Recording, listing, editing and deleting payments through the real API and database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PaymentEndpointsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private const string Reference = "BK-ABC123";

    private async Task<Guid> PrepareBookingAsync(decimal? price = 100_000m, string reference = Reference)
    {
        if (await Scalar<long>("SELECT count(*) FROM services") == 0)
        {
            await CreateAsync("services", Service("Private tour"));
        }

        await AddBookingAsync("private-tour", reference: reference);
        var priceSql = price is null ? "NULL" : price.Value.ToString(CultureInfo.InvariantCulture);
        await Execute($"UPDATE bookings SET price_lkr = {priceSql} WHERE reference = '{reference}'");
        return await Scalar<Guid>($"SELECT id FROM bookings WHERE reference = '{reference}'");
    }

    private Task AddRatesAsync(decimal usd, decimal eur, decimal gbp, DateTimeOffset at) => Execute(
        string.Create(CultureInfo.InvariantCulture, $"""
        INSERT INTO exchange_rates (id, currency, rate_lkr, source, fetched_at) VALUES
          (gen_random_uuid(), 'USD', {usd}, 'test', '{at:u}'),
          (gen_random_uuid(), 'EUR', {eur}, 'test', '{at:u}'),
          (gen_random_uuid(), 'GBP', {gbp}, 'test', '{at:u}')
        """));

    private Task<HttpResponseMessage> PostPayment(string booking, object body) => AsAdmin(HttpMethod.Post, $"/api/bookings/{booking}/payments", body);

    private async Task<JsonElement> RecordAsync(object body, string booking = Reference)
    {
        var response = await PostPayment(booking, body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private async Task<JsonElement> AdminBookingAsync() => await Json(await AsAdmin(HttpMethod.Get, $"/api/bookings/{Reference}"));

    private async Task<(decimal Paid, decimal? Balance)> TotalsAsync()
    {
        var booking = await AdminBookingAsync();
        var balance = booking.GetProperty("balanceLkr");
        return (booking.GetProperty("paidLkr").GetDecimal(), balance.ValueKind == JsonValueKind.Null ? null : balance.GetDecimal());
    }

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task Every_payment_route_refuses_guests_and_customers()
    {
        var bookingId = await PrepareBookingAsync();
        var payment = await RecordAsync(new { amount = 10, currency = "LKR" });
        var id = payment.GetProperty("id").GetString();
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Post, $"/api/bookings/{bookingId}/payments", new { amount = 5, currency = "LKR" }),
            (HttpMethod.Get, "/api/payments", null),
            (HttpMethod.Put, $"/api/payments/{id}", new { amount = 7 }),
            (HttpMethod.Delete, $"/api/payments/{id}", null),
        };

        foreach (var (method, path, body) in attempts)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Api.SendAsync(method, path, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Api.SendAsync(method, path, body, bearer: CustomerToken)).StatusCode);
        }

        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM payments"));
        Assert.Equal(10m, await Scalar<decimal>("SELECT amount FROM payments"));
    }

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task A_payment_gets_the_defaults_a_location_and_todays_sri_lanka_date()
    {
        var bookingId = await PrepareBookingAsync();
        await AddRatesAsync(330m, 360m, 420m, Factory.Clock.UtcNow);

        var response = await PostPayment(Reference, new { amount = 100 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/bookings/{bookingId}", response.Headers.Location!.ToString());
        var payment = await Json(response);
        Assert.Equal(bookingId.ToString(), payment.GetProperty("bookingId").GetString());
        Assert.Equal("paid", payment.GetProperty("status").GetString());
        Assert.Equal("cash", payment.GetProperty("method").GetString());
        Assert.Equal("USD", payment.GetProperty("currency").GetString());
        Assert.Equal(330m, payment.GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(33_000m, payment.GetProperty("amountLkr").GetDecimal());
        Assert.Equal(Factory.Clock.TodayInSriLanka.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), payment.GetProperty("paidAt").GetString());
        Assert.Equal(string.Empty, payment.GetProperty("note").GetString());
        Assert.False(payment.TryGetProperty("gateway", out _));
        Assert.False(payment.TryGetProperty("gatewayReference", out _));
    }

    [Fact]
    public async Task An_lkr_payment_has_rate_1_and_gateway_columns_stay_empty()
    {
        await PrepareBookingAsync();

        var payment = await RecordAsync(new { amount = 50000.5, currency = "LKR", method = "bank_transfer", status = "pending", paidAt = "2026-10-01", note = "  deposit  " });

        Assert.Equal(1m, payment.GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(50000.5m, payment.GetProperty("amountLkr").GetDecimal());
        Assert.Equal("bank_transfer", payment.GetProperty("method").GetString());
        Assert.Equal("pending", payment.GetProperty("status").GetString());
        Assert.Equal("2026-10-01", payment.GetProperty("paidAt").GetString());
        Assert.Equal("deposit", payment.GetProperty("note").GetString());
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM payments WHERE gateway IS NULL AND gateway_reference IS NULL"));
    }

    [Fact]
    public async Task Without_stored_rates_the_fallback_rate_is_copied()
    {
        await PrepareBookingAsync();

        var payment = await RecordAsync(new { amount = 10, currency = "EUR" });

        Assert.Equal(360m, payment.GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(3600m, payment.GetProperty("amountLkr").GetDecimal());
    }

    [Fact]
    public async Task The_booking_can_be_given_by_id_or_reference_and_unknown_ones_are_404()
    {
        var bookingId = await PrepareBookingAsync();

        await RecordAsync(new { amount = 1, currency = "LKR" }, bookingId.ToString());
        await RecordAsync(new { amount = 2, currency = "LKR" }, Reference.ToLowerInvariant());
        var missing = await PostPayment("BK-ZZZZZZ", new { amount = 1 });
        var notAnId = await PostPayment("whatever", new { amount = 1 });

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Booking not found", (await Json(missing)).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, notAnId.StatusCode);
        Assert.Equal(2L, await Scalar<long>("SELECT count(*) FROM payments"));
    }

    [Fact]
    public async Task Every_wrong_field_gets_its_message_and_nothing_is_stored()
    {
        await PrepareBookingAsync();

        var response = await PostPayment(Reference, new
        {
            amount = 0,
            currency = "JPY",
            method = "cheque",
            status = "done",
            paidAt = "2026-02-30",
            note = new string('x', 501),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await Json(response)).GetProperty("error").GetProperty("fields");
        Assert.Equal("Enter an amount greater than 0", fields.GetProperty("amount").GetString());
        Assert.Equal("Currency must be one of: USD, EUR, GBP, LKR", fields.GetProperty("currency").GetString());
        Assert.Equal("Method must be one of: cash, bank_transfer, card, other", fields.GetProperty("method").GetString());
        Assert.Equal("Status must be one of: pending, paid, refunded", fields.GetProperty("status").GetString());
        Assert.Equal("Choose a valid date", fields.GetProperty("paidAt").GetString());
        Assert.Equal("Note must be 500 characters or fewer", fields.GetProperty("note").GetString());
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM payments"));
    }

    [Fact]
    public async Task A_missing_body_or_amount_is_a_400_with_the_amount_message()
    {
        await PrepareBookingAsync();

        var empty = await PostPayment(Reference, new { });
        var tooPrecise = await PostPayment(Reference, new { amount = 10.001 });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise.StatusCode);
        Assert.Equal("Enter an amount greater than 0", (await Json(empty)).GetProperty("error").GetProperty("fields").GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Rates_that_move_later_leave_the_payment_alone()
    {
        await PrepareBookingAsync();
        await AddRatesAsync(330m, 360m, 420m, Factory.Clock.UtcNow);
        await RecordAsync(new { amount = 100 });

        await AddRatesAsync(400m, 450m, 500m, Factory.Clock.UtcNow.AddDays(1));
        var list = (await Json(await AsAdmin(HttpMethod.Get, "/api/payments"))).GetProperty("items");

        Assert.Equal(330m, list[0].GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(33_000m, list[0].GetProperty("amountLkr").GetDecimal());
        Assert.Equal(33_000m, await Scalar<decimal>("SELECT amount_lkr FROM payments"));
    }

    // ---------------------------------------------------------------- update

    [Fact]
    public async Task Changing_only_the_amount_keeps_the_rate_but_a_new_currency_takes_the_current_one()
    {
        await PrepareBookingAsync();
        await AddRatesAsync(330m, 360m, 420m, Factory.Clock.UtcNow);
        var id = (await RecordAsync(new { amount = 100 })).GetProperty("id").GetString();
        await AddRatesAsync(340m, 370m, 430m, Factory.Clock.UtcNow.AddDays(1));

        var amount = await Json(await AsAdmin(HttpMethod.Put, $"/api/payments/{id}", new { amount = 120 }));
        var currency = await Json(await AsAdmin(HttpMethod.Put, $"/api/payments/{id}", new { currency = "EUR" }));

        Assert.Equal(330m, amount.GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(39_600m, amount.GetProperty("amountLkr").GetDecimal());
        Assert.Equal("EUR", currency.GetProperty("currency").GetString());
        Assert.Equal(370m, currency.GetProperty("exchangeRate").GetDecimal());
        Assert.Equal(44_400m, currency.GetProperty("amountLkr").GetDecimal());
        Assert.Equal(44_400m, await Scalar<decimal>("SELECT amount_lkr FROM payments"));
    }

    [Fact]
    public async Task Sending_only_the_status_changes_only_the_status_and_touches_updated_at()
    {
        await PrepareBookingAsync();
        var created = await RecordAsync(new { amount = 100, currency = "LKR", method = "card", note = "keep", paidAt = "2026-10-01" });
        var id = created.GetProperty("id").GetString();
        Factory.Clock.Advance(TimeSpan.FromHours(2));

        var updated = await Json(await AsAdmin(HttpMethod.Put, $"/api/payments/{id}", new { status = "refunded" }));

        Assert.Equal("refunded", updated.GetProperty("status").GetString());
        Assert.Equal("card", updated.GetProperty("method").GetString());
        Assert.Equal("keep", updated.GetProperty("note").GetString());
        Assert.Equal("2026-10-01", updated.GetProperty("paidAt").GetString());
        Assert.Equal(100m, updated.GetProperty("amountLkr").GetDecimal());
        Assert.True(await Scalar<bool>("SELECT updated_at > created_at FROM payments"));
    }

    [Fact]
    public async Task An_edit_can_clear_the_note_and_a_bad_edit_changes_nothing()
    {
        await PrepareBookingAsync();
        var id = (await RecordAsync(new { amount = 100, currency = "LKR", note = "keep" })).GetProperty("id").GetString();

        var cleared = await Json(await AsAdmin(HttpMethod.Put, $"/api/payments/{id}", new { note = "" }));
        var bad = await AsAdmin(HttpMethod.Put, $"/api/payments/{id}", new { amount = -5, status = "gone" });

        Assert.Equal(string.Empty, cleared.GetProperty("note").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var fields = (await Json(bad)).GetProperty("error").GetProperty("fields");
        Assert.Equal("Enter an amount greater than 0", fields.GetProperty("amount").GetString());
        Assert.Equal("Status must be one of: pending, paid, refunded", fields.GetProperty("status").GetString());
        Assert.Equal(100m, await Scalar<decimal>("SELECT amount FROM payments"));
        Assert.Equal("paid", await Scalar<string>("SELECT status FROM payments"));
    }

    [Fact]
    public async Task An_edit_never_touches_the_gateway_columns()
    {
        await PrepareBookingAsync();
        var id = (await RecordAsync(new { amount = 100, currency = "LKR" })).GetProperty("id").GetString();
        await Execute($"UPDATE payments SET gateway = 'payhere', gateway_reference = 'abc123' WHERE id = '{id}'");

        await AsAdmin(HttpMethod.Put, $"/api/payments/{id}", new { note = "changed" });

        Assert.Equal("payhere", await Scalar<string>("SELECT gateway FROM payments"));
        Assert.Equal("abc123", await Scalar<string>("SELECT gateway_reference FROM payments"));
    }

    [Fact]
    public async Task Editing_or_deleting_an_unknown_payment_is_404()
    {
        var unknown = Guid.NewGuid();

        var edit = await AsAdmin(HttpMethod.Put, $"/api/payments/{unknown}", new { amount = 1 });
        var delete = await AsAdmin(HttpMethod.Delete, "/api/payments/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal("Payment not found", (await Json(edit)).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    // ---------------------------------------------------------------- delete

    [Fact]
    public async Task Delete_is_204_then_404()
    {
        await PrepareBookingAsync();
        var id = (await RecordAsync(new { amount = 100, currency = "LKR" })).GetProperty("id").GetString();

        var first = await AsAdmin(HttpMethod.Delete, $"/api/payments/{id}");
        var second = await AsAdmin(HttpMethod.Delete, $"/api/payments/{id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM payments"));
    }

    // ---------------------------------------------------------------- list

    [Fact]
    public async Task The_list_shows_reference_and_customer_name_newest_paid_first()
    {
        await PrepareBookingAsync();
        await PrepareBookingAsync(price: null, reference: "BK-XYZ789");
        await RecordAsync(new { amount = 1, currency = "LKR", paidAt = "2026-10-01" });
        await RecordAsync(new { amount = 2, currency = "LKR", paidAt = "2026-10-20" }, "BK-XYZ789");
        await RecordAsync(new { amount = 3, currency = "LKR", paidAt = "2026-10-10" });

        var list = (await Json(await AsAdmin(HttpMethod.Get, "/api/payments"))).GetProperty("items");

        Assert.Equal([2m, 3m, 1m], list.EnumerateArray().Select(p => p.GetProperty("amount").GetDecimal()));
        Assert.Equal(["BK-XYZ789", Reference, Reference], list.EnumerateArray().Select(p => p.GetProperty("bookingReference").GetString()));
        Assert.All(list.EnumerateArray(), p => Assert.Equal("Test User", p.GetProperty("customerName").GetString()));
    }

    [Fact]
    public async Task With_no_payments_the_list_is_empty()
    {
        var list = (await Json(await AsAdmin(HttpMethod.Get, "/api/payments"))).GetProperty("items");

        Assert.Equal(0, list.GetArrayLength());
    }

    // ---------------------------------------------------------------- booking totals

    [Fact]
    public async Task The_booking_totals_follow_every_payment_change_in_every_view()
    {
        await PrepareBookingAsync(price: 100_000m);
        var bookerId = await Scalar<Guid>("SELECT id FROM users WHERE email = 'booker@example.com'");
        var bookerToken = TestTokens.For(bookerId, UserRole.Customer, "booker@example.com");

        Assert.Equal((0m, 100_000m), await TotalsAsync());

        var paid = await RecordAsync(new { amount = 40_000, currency = "LKR" });
        var pending = await RecordAsync(new { amount = 10_000, currency = "LKR", status = "pending" });
        Assert.Equal((40_000m, 60_000m), await TotalsAsync());

        var pendingId = pending.GetProperty("id").GetString();
        await AsAdmin(HttpMethod.Put, $"/api/payments/{pendingId}", new { status = "paid" });
        Assert.Equal((50_000m, 50_000m), await TotalsAsync());

        await RecordAsync(new { amount = 20_000, currency = "LKR", status = "refunded" });
        Assert.Equal((30_000m, 70_000m), await TotalsAsync());

        await AsAdmin(HttpMethod.Put, $"/api/payments/{paid.GetProperty("id").GetString()}", new { amount = 130_000 });
        Assert.Equal((120_000m, 0m), await TotalsAsync());

        var list = (await Json(await AsAdmin(HttpMethod.Get, "/api/bookings"))).GetProperty("items");
        var mine = (await Json(await Api.GetAsync("/api/bookings/mine", bearer: bookerToken))).GetProperty("items");
        Assert.Equal(120_000m, list[0].GetProperty("paidLkr").GetDecimal());
        Assert.Equal(120_000m, mine[0].GetProperty("paidLkr").GetDecimal());
        Assert.Equal(0m, mine[0].GetProperty("balanceLkr").GetDecimal());

        await AsAdmin(HttpMethod.Delete, $"/api/payments/{paid.GetProperty("id").GetString()}");
        Assert.Equal((-10_000m, 110_000m), await TotalsAsync());

        var detail = await AdminBookingAsync();
        Assert.Equal(2, detail.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task A_booking_without_a_price_has_no_balance_but_shows_what_was_paid()
    {
        await PrepareBookingAsync(price: null);

        await RecordAsync(new { amount = 5_000, currency = "LKR" });

        Assert.Equal((5_000m, null), await TotalsAsync());
    }

    [Fact]
    public async Task Deleting_a_booking_removes_its_payments_and_keeps_the_others()
    {
        await PrepareBookingAsync();
        await PrepareBookingAsync(reference: "BK-XYZ789");
        await RecordAsync(new { amount = 1, currency = "LKR" });
        await RecordAsync(new { amount = 2, currency = "LKR" }, "BK-XYZ789");

        var response = await AsAdmin(HttpMethod.Delete, "/api/bookings/BK-XYZ789");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM payments"));
        Assert.Equal(1m, await Scalar<decimal>("SELECT amount FROM payments"));
    }
}
