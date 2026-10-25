using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Seed;
using TravelWebAPI.Infrastructure.Services;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The admin's side of bookings through the real API and database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BookingAdminEndpointsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static string Later => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<(Guid Id, string Token)> AddCustomerAsync(string email = "emma@example.com")
    {
        await using var db = PostgresFixture.CreateContext(Connection);
        var user = TestData.User(email);
        user.Name = "Emma Wilson";
        user.Phone = "+44 7700 900123";
        user.Country = "United Kingdom";
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, TestTokens.For(user.Id, UserRole.Customer, email));
    }

    private async Task<string> BookAsync(string token, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["serviceId"] = "private-tour",
            ["date"] = Later,
            ["time"] = "09:30",
            ["adults"] = 2,
            ["name"] = "Emma Wilson",
            ["email"] = "emma@example.com",
            ["whatsapp"] = "+44 7700 900123",
            ["country"] = "United Kingdom",
        };
        if (extra is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject())
            {
                body[property.Name] = property.Value.Clone();
            }
        }

        var response = await Api.PostAsync("/api/bookings", body, bearer: token);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("reference").GetString()!;
    }

    private async Task PrepareAsync()
    {
        await CreateAsync("services", Service("Private tour"));
        await CreateAsync("vehicles", Vehicle());
        await CreateAsync("packages", Package("Cultural Triangle", extra: new { priceLkr = 99000 }));
    }

    private async Task<HttpResponseMessage> PutRawAsync(string reference, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/bookings/{reference}") { Content = Raw(json) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AdminToken);
        return await Api.SendAsync(request);
    }

    private Task<long> CountEmails(string kind) => Scalar<long>($"SELECT count(*) FROM email_logs WHERE kind = '{kind}'");

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task Every_admin_route_refuses_guests_and_customers()
    {
        await PrepareAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token);
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/api/bookings", null),
            (HttpMethod.Put, $"/api/bookings/{reference}", new { status = "confirmed" }),
            (HttpMethod.Delete, $"/api/bookings/{reference}", null),
            (HttpMethod.Post, $"/api/bookings/{reference}/email", new { subject = "Hello", body = "Hello there" }),
        };

        foreach (var (method, path, body) in attempts)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Api.SendAsync(method, path, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Api.SendAsync(method, path, body, bearer: token)).StatusCode);
        }

        Assert.Equal("pending", await Scalar<string>("SELECT status FROM bookings"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM bookings"));
    }

    // ---------------------------------------------------------------- list and detail

    [Fact]
    public async Task The_admin_list_has_every_customers_bookings_newest_first_with_notes_and_totals()
    {
        await PrepareAsync();
        var (_, one) = await AddCustomerAsync("one@example.com");
        var (_, two) = await AddCustomerAsync("two@example.com");
        var first = await BookAsync(one);
        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        var second = await BookAsync(two, new { packageId = "cultural-triangle" });

        var list = (await Json(await AsAdmin(HttpMethod.Get, "/api/bookings"))).GetProperty("items");

        Assert.Equal([second, first], list.EnumerateArray().Select(b => b.GetProperty("reference").GetString()));
        Assert.All(list.EnumerateArray(), booking =>
        {
            Assert.True(booking.TryGetProperty("adminNotes", out _));
            Assert.True(booking.TryGetProperty("paidLkr", out _));
            Assert.True(booking.TryGetProperty("balanceLkr", out _));
        });
        Assert.Equal(99000m, list[0].GetProperty("balanceLkr").GetDecimal());
    }

    [Fact]
    public async Task The_admin_detail_has_payments_emails_and_the_customer_and_works_by_reference()
    {
        await PrepareAsync();
        var (customerId, token) = await AddCustomerAsync();
        var reference = await BookAsync(token, new { packageId = "cultural-triangle" });
        var bookingId = await Scalar<Guid>("SELECT id FROM bookings");
        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            var payment = TestData.Payment(bookingId);
            payment.Amount = 30000m;
            payment.AmountLkr = 30000m;
            payment.Notes = "deposit";
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
        }

        var byReference = await Json(await AsAdmin(HttpMethod.Get, $"/api/bookings/{reference}"));
        var byId = await Json(await AsAdmin(HttpMethod.Get, $"/api/bookings/{bookingId}"));

        Assert.True(SameJson(byReference, byId));
        Assert.Equal(string.Empty, byReference.GetProperty("adminNotes").GetString());
        Assert.Equal(30000m, byReference.GetProperty("paidLkr").GetDecimal());
        Assert.Equal(69000m, byReference.GetProperty("balanceLkr").GetDecimal());
        var payment2 = byReference.GetProperty("payments")[0];
        Assert.Equal(30000m, payment2.GetProperty("amountLkr").GetDecimal());
        Assert.Equal("LKR", payment2.GetProperty("currency").GetString());
        Assert.Equal("cash", payment2.GetProperty("method").GetString());
        Assert.Equal("paid", payment2.GetProperty("status").GetString());
        Assert.Equal("deposit", payment2.GetProperty("note").GetString());
        Assert.Equal("booking-received", byReference.GetProperty("emails")[0].GetProperty("kind").GetString());
        var customer = byReference.GetProperty("customer");
        Assert.Equal(customerId.ToString(), customer.GetProperty("id").GetString());
        Assert.Equal("Emma Wilson", customer.GetProperty("name").GetString());
        Assert.Equal("+44 7700 900123", customer.GetProperty("phone").GetString());
        Assert.Equal("United Kingdom", customer.GetProperty("country").GetString());
    }

    [Fact]
    public async Task An_unknown_booking_is_404_for_the_admin()
    {
        var response = await AsAdmin(HttpMethod.Get, "/api/bookings/BK-ZZZZZZ");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------------------------------------------------------- editing

    [Fact]
    public async Task The_admin_can_agree_a_price_clear_it_and_keep_it_by_not_sending_it()
    {
        await PrepareAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token);

        var set = await Json(await PutRawAsync(reference, """{"priceLkr":120000}"""));
        var kept = await Json(await PutRawAsync(reference, """{"adminNotes":"call first"}"""));
        var cleared = await Json(await PutRawAsync(reference, """{"priceLkr":null}"""));

        Assert.Equal(120000m, set.GetProperty("priceLkr").GetDecimal());
        Assert.Equal(120000m, set.GetProperty("balanceLkr").GetDecimal());
        Assert.Equal(120000m, kept.GetProperty("priceLkr").GetDecimal());
        Assert.Equal("call first", kept.GetProperty("adminNotes").GetString());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("priceLkr").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("balanceLkr").ValueKind);
        Assert.Equal(DBNull.Value, await Scalar<object>("SELECT price_lkr FROM bookings"));
    }

    [Fact]
    public async Task The_admin_can_change_date_time_places_flight_and_vehicle_and_set_a_past_date()
    {
        await PrepareAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token);

        var response = await AsAdmin(HttpMethod.Put, $"/api/bookings/{reference}", new
        {
            date = "2026-01-02",
            time = "18:45",
            vehicleId = "toyota-prius",
            pickupLocation = "Airport",
            dropoffLocation = "Galle",
            flightNumber = "UL225",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var booking = await Json(response);
        Assert.Equal("2026-01-02", booking.GetProperty("date").GetString());
        Assert.Equal("18:45", booking.GetProperty("time").GetString());
        Assert.Equal("toyota-prius", booking.GetProperty("vehicleId").GetString());
        Assert.Equal("Airport", booking.GetProperty("pickupLocation").GetString());
        Assert.Equal("Galle", booking.GetProperty("dropoffLocation").GetString());
        Assert.Equal("UL225", booking.GetProperty("flightNumber").GetString());
        Assert.Equal("toyota-prius", await Scalar<string>("SELECT vehicle_id FROM bookings"));

        var cleared = await AsAdmin(HttpMethod.Put, $"/api/bookings/{reference}", new { vehicleId = "" });
        Assert.Equal(JsonValueKind.Null, (await Json(cleared)).GetProperty("vehicleId").ValueKind);
        Assert.Equal(DBNull.Value, await Scalar<object>("SELECT vehicle_id FROM bookings"));
    }

    [Fact]
    public async Task Invalid_edits_get_the_contract_messages_and_change_nothing()
    {
        await PrepareAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token);

        var response = await AsAdmin(HttpMethod.Put, $"/api/bookings/{reference}", new { status = "done", priceLkr = -5, date = "tomorrow", time = "noon", vehicleId = "nope" });
        var textPrice = await PutRawAsync(reference, """{"priceLkr":"cheap"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Status must be one of: pending, confirmed, completed, cancelled", await Field(response, "status"));
        Assert.Equal("Enter a valid price", await Field(response, "priceLkr"));
        Assert.Equal("Choose a valid date", await Field(response, "date"));
        Assert.Equal("Choose a valid time", await Field(response, "time"));
        Assert.Equal("Unknown vehicle", await Field(response, "vehicleId"));
        Assert.Equal(HttpStatusCode.BadRequest, textPrice.StatusCode);
        Assert.Equal("Enter a valid price", await Field(textPrice, "priceLkr"));
        Assert.Equal("pending", await Scalar<string>("SELECT status FROM bookings"));
    }

    // ---------------------------------------------------------------- status emails

    [Fact]
    public async Task Confirming_cancelling_and_completing_each_email_the_customer_once_per_change()
    {
        await PrepareAsync();
        var (_, token) = await AddCustomerAsync();
        var confirm = await BookAsync(token, new { packageId = "cultural-triangle" });
        var cancel = await BookAsync(token);
        var complete = await BookAsync(token);

        await AsAdmin(HttpMethod.Put, $"/api/bookings/{confirm}", new { status = "confirmed" });
        await AsAdmin(HttpMethod.Put, $"/api/bookings/{confirm}", new { status = "confirmed", adminNotes = "again" });
        await AsAdmin(HttpMethod.Put, $"/api/bookings/{cancel}", new { status = "cancelled" });
        await AsAdmin(HttpMethod.Put, $"/api/bookings/{complete}", new { status = "completed" });
        await AsAdmin(HttpMethod.Put, $"/api/bookings/{complete}", new { status = "pending" });

        Assert.Equal(1L, await CountEmails("booking-confirmed"));
        Assert.Equal(1L, await CountEmails("booking-cancelled"));
        Assert.Equal(1L, await CountEmails("booking-completed"));
        var body = await Scalar<string>("SELECT body FROM email_logs WHERE kind = 'booking-confirmed'");
        Assert.Contains("Private tour", body, StringComparison.Ordinal);
        Assert.Contains(Later, body, StringComparison.Ordinal);
        Assert.Contains("Agreed price: LKR 99,000.", body, StringComparison.Ordinal);
        Assert.Equal("emma@example.com", await Scalar<string>("SELECT to_address FROM email_logs WHERE kind = 'booking-confirmed'"));
        Assert.Equal(await Scalar<Guid>($"SELECT id FROM bookings WHERE reference = '{confirm}'"), await Scalar<Guid>("SELECT booking_id FROM email_logs WHERE kind = 'booking-confirmed'"));
        Assert.Equal("failed", await Scalar<string>("SELECT delivery_status FROM email_logs WHERE kind = 'booking-confirmed'"));
        Assert.Equal(3, Factory.Emails.Messages.Count(m => m.Kind is EmailKind.BookingConfirmed or EmailKind.BookingCancelled or EmailKind.BookingCompleted));
    }

    // ---------------------------------------------------------------- delete

    [Fact]
    public async Task Deleting_a_booking_removes_its_payments_and_keeps_its_emails_with_the_link_cleared()
    {
        await PrepareAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token);
        var bookingId = await Scalar<Guid>("SELECT id FROM bookings");
        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            db.Payments.Add(TestData.Payment(bookingId));
            await db.SaveChangesAsync();
        }

        var first = await AsAdmin(HttpMethod.Delete, $"/api/bookings/{reference}");
        var second = await AsAdmin(HttpMethod.Delete, $"/api/bookings/{reference}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM bookings"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM payments"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM email_logs"));
        Assert.Equal(DBNull.Value, await Scalar<object>("SELECT booking_id FROM email_logs"));
    }

    [Fact]
    public async Task A_service_package_or_vehicle_with_bookings_still_cannot_be_deleted()
    {
        await PrepareAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token, new { packageId = "cultural-triangle", vehicleId = "toyota-prius" });

        var service = await AsAdmin(HttpMethod.Delete, "/api/services/private-tour");
        var package = await AsAdmin(HttpMethod.Delete, "/api/packages/cultural-triangle");
        var vehicle = await AsAdmin(HttpMethod.Delete, "/api/vehicles/toyota-prius");

        foreach (var response in new[] { service, package, vehicle })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal([$"booking {reference}"], Strings((await Json(response)).GetProperty("error").GetProperty("fields").GetProperty("dependents")));
        }
    }

    // ---------------------------------------------------------------- custom email

    [Fact]
    public async Task A_custom_email_is_201_with_the_log_entry_and_is_linked_to_the_booking()
    {
        await PrepareAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token);

        var response = await AsAdmin(HttpMethod.Post, $"/api/bookings/{reference}/email", new { subject = "About your pickup", body = "We will be at the arrivals hall." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await Json(response);
        Assert.Equal("custom", entry.GetProperty("kind").GetString());
        Assert.Equal("emma@example.com", entry.GetProperty("to").GetString());
        Assert.Equal("About your pickup", entry.GetProperty("subject").GetString());
        Assert.Equal("failed", entry.GetProperty("status").GetString());
        Assert.Equal("No email provider configured", entry.GetProperty("error").GetString());
        Assert.Equal(await Scalar<Guid>("SELECT id FROM bookings"), entry.GetProperty("bookingId").GetGuid());
        Assert.Equal(EmailKind.Custom, Factory.Emails.Messages[^1].Kind);
        Assert.Equal("We will be at the arrivals hall.", Factory.Emails.Messages[^1].Body);
    }

    [Fact]
    public async Task A_custom_email_needs_a_subject_and_a_message_and_a_real_booking()
    {
        await PrepareAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token);
        var emailsBefore = await Scalar<long>("SELECT count(*) FROM email_logs");

        var invalid = await AsAdmin(HttpMethod.Post, $"/api/bookings/{reference}/email", new { subject = "x", body = "" });
        var unknown = await AsAdmin(HttpMethod.Post, "/api/bookings/BK-ZZZZZZ/email", new { subject = "Hello", body = "Hello there" });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Enter a subject", await Field(invalid, "subject"));
        Assert.Equal("Write a message", await Field(invalid, "body"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(emailsBefore, await Scalar<long>("SELECT count(*) FROM email_logs"));
    }

    // ---------------------------------------------------------------- seed

    [Fact]
    public async Task Seeded_bookings_show_totals_that_match_their_payments()
    {
        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            await new DatabaseSeeder(db, new PasswordHasher(iterations: 1_000), NullLogger<DatabaseSeeder>.Instance).SeedAsync(new SeedOptions());
        }

        var page = await Json(await AsAdmin(HttpMethod.Get, "/api/bookings?pageSize=100"));
        var list = page.GetProperty("items");
        var seeded = await Scalar<long>("SELECT count(*) FROM bookings");

        Assert.Equal(seeded, page.GetProperty("totalCount").GetInt64());
        Assert.Equal(seeded, list.GetArrayLength());
        foreach (var booking in list.EnumerateArray())
        {
            var id = booking.GetProperty("id").GetString();
            var paid = await Scalar<decimal>($"SELECT coalesce(sum(CASE status WHEN 'paid' THEN amount_lkr WHEN 'refunded' THEN -amount_lkr ELSE 0 END), 0) FROM payments WHERE booking_id = '{id}'");
            Assert.Equal(paid, booking.GetProperty("paidLkr").GetDecimal());

            if (booking.GetProperty("priceLkr").ValueKind == JsonValueKind.Number)
            {
                Assert.Equal(Math.Max(booking.GetProperty("priceLkr").GetDecimal() - paid, 0), booking.GetProperty("balanceLkr").GetDecimal());
            }
        }

        Assert.Contains(list.EnumerateArray(), b => b.GetProperty("paidLkr").GetDecimal() > 0);
    }
}
