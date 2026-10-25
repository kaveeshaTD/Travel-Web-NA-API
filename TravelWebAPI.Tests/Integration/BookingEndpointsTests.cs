using System.Net;
using System.Text.Json;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>Creating and reading bookings as a customer, through the real API and database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class BookingEndpointsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static string Today => DateText(0);

    /// <summary>A date well in the future (30 days ahead).</summary>
    private static string Tomorrow => DateText(30);

    /// <summary>A date clearly in the past (2 days ago).</summary>
    private static string Yesterday => DateText(-2);

    private static string DateText(int daysFromToday)
        => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysFromToday)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<(Guid Id, string Token)> AddCustomerAsync(string email = "emma@example.com")
    {
        await using var db = PostgresFixture.CreateContext(Connection);
        var user = TestData.User(email);
        user.Name = "Emma Wilson";
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, TestTokens.For(user.Id, UserRole.Customer, email));
    }

    private static Dictionary<string, object?> Booking(string serviceId = "private-tour", object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["serviceId"] = serviceId,
            ["date"] = Tomorrow,
            ["time"] = "09:30",
            ["pickupLocation"] = "Colombo",
            ["dropoffLocation"] = "Kandy",
            ["adults"] = 2,
            ["children"] = 1,
            ["bags"] = 3,
            ["notes"] = "Quiet driver please",
            ["name"] = "  Emma Wilson ",
            ["email"] = "  Emma@Example.COM ",
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

        return body;
    }

    private async Task SeedContentAsync()
    {
        await CreateAsync("services", Service("Private tour"));
        await CreateAsync("services", Service("Airport"));
        await CreateAsync("vehicles", Vehicle());
        await CreateAsync("packages", Package("Cultural Triangle", extra: new { priceLkr = 99000 }));
        await CreateAsync("packages", Package("Hidden", extra: new { active = false }));
    }

    private async Task<string> BookAsync(string token, object? extra = null, string serviceId = "private-tour")
    {
        var response = await Api.PostAsync("/api/bookings", Booking(serviceId, extra), bearer: token);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await Json(response)).GetProperty("reference").GetString()!;
    }

    // ---------------------------------------------------------------- creating

    [Fact]
    public async Task A_guest_cannot_book()
    {
        await SeedContentAsync();

        var response = await Api.PostAsync("/api/bookings", Booking());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM bookings"));
    }

    [Fact]
    public async Task A_customer_booking_is_201_with_a_reference_and_a_location_and_belongs_to_the_customer()
    {
        await SeedContentAsync();
        var (id, token) = await AddCustomerAsync();

        var response = await Api.PostAsync("/api/bookings", Booking(), bearer: token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reference = (await Json(response)).GetProperty("reference").GetString()!;
        Assert.Matches("^BK-[A-HJ-NP-Z2-9]{6}$", reference);
        Assert.Equal($"/api/bookings/{reference}", response.Headers.Location?.ToString());
        Assert.Equal(id.ToString(), await Scalar<string>("SELECT user_id::text FROM bookings"));
        Assert.Equal(reference, await Scalar<string>("SELECT reference FROM bookings"));
        Assert.Equal("pending", await Scalar<string>("SELECT status FROM bookings"));
        Assert.Equal("emma@example.com", await Scalar<string>("SELECT email FROM bookings"));
        Assert.Equal("Emma Wilson", await Scalar<string>("SELECT name FROM bookings"));
        Assert.Equal(new TimeOnly(9, 30), await Scalar<TimeOnly>("SELECT travel_time FROM bookings"));
        Assert.Equal((2, 1, 3), (await Scalar<int>("SELECT adults FROM bookings"), await Scalar<int>("SELECT children FROM bookings"), await Scalar<int>("SELECT bags FROM bookings")));
    }

    [Fact]
    public async Task An_admin_can_book_on_their_own_account()
    {
        await SeedContentAsync();
        Guid adminId;
        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            var admin = TestData.User("boss@example.com");
            admin.Role = UserRole.Admin;
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            adminId = admin.Id;
        }

        var response = await Api.PostAsync("/api/bookings", Booking(), bearer: TestTokens.For(adminId, UserRole.Admin, "boss@example.com"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(adminId.ToString(), await Scalar<string>("SELECT user_id::text FROM bookings"));
    }

    [Fact]
    public async Task Every_validation_message_is_the_one_in_the_contract_and_nothing_is_stored()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();

        var response = await Api.PostAsync("/api/bookings", new
        {
            serviceId = "nope",
            packageId = "hidden",
            vehicleId = "nope",
            date = Yesterday,
            time = "noon",
            adults = 0,
            children = 41,
            bags = 61,
            name = "E",
            email = "nope",
            whatsapp = "12",
            country = "U",
        }, bearer: token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Please choose a service", await Field(response, "serviceId"));
        Assert.Equal("Unknown package", await Field(response, "packageId"));
        Assert.Equal("Unknown vehicle", await Field(response, "vehicleId"));
        Assert.Equal("The date cannot be in the past", await Field(response, "date"));
        Assert.Equal("Choose a time", await Field(response, "time"));
        Assert.Equal("Adults must be 1 to 40", await Field(response, "adults"));
        Assert.Equal("Children must be 0 to 40", await Field(response, "children"));
        Assert.Equal("Bags must be 0 to 60", await Field(response, "bags"));
        Assert.Equal("Enter your full name", await Field(response, "name"));
        Assert.Equal("Enter a valid email address", await Field(response, "email"));
        Assert.Equal("Enter a phone number with country code", await Field(response, "whatsapp"));
        Assert.Equal("Enter your country", await Field(response, "country"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM bookings"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM email_logs"));
    }

    [Fact]
    public async Task Today_is_allowed_and_yesterday_is_not()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();

        var today = await Api.PostAsync("/api/bookings", Booking(extra: new { date = Today }), bearer: token);
        var past = await Api.PostAsync("/api/bookings", Booking(extra: new { date = Yesterday }), bearer: token);
        var invalid = await Api.PostAsync("/api/bookings", Booking(extra: new { date = "01/12/2030" }), bearer: token);

        Assert.Equal(HttpStatusCode.Created, today.StatusCode);
        Assert.Equal("The date cannot be in the past", await Field(past, "date"));
        Assert.Equal("Choose a valid date", await Field(invalid, "date"));
    }

    [Fact]
    public async Task A_fractional_head_count_gets_the_field_message()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();

        var response = await Api.PostAsync("/api/bookings", Booking(extra: new { adults = 1.5 }), bearer: token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Adults must be 1 to 40", await Field(response, "adults"));
    }

    [Fact]
    public async Task The_airport_service_needs_a_flight_number()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();

        var without = await Api.PostAsync("/api/bookings", Booking("airport"), bearer: token);
        var with = await Api.PostAsync("/api/bookings", Booking("airport", new { flightNumber = " UL504 " }), bearer: token);

        Assert.Equal("Enter your flight number so we can track it", await Field(without, "flightNumber"));
        Assert.Equal(HttpStatusCode.Created, with.StatusCode);
        Assert.Equal("UL504", await Scalar<string>("SELECT flight_number FROM bookings"));
    }

    [Fact]
    public async Task An_empty_object_and_an_empty_body_report_the_required_fields()
    {
        var (_, token) = await AddCustomerAsync();
        using var noBody = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = Raw(string.Empty) };
        noBody.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var empty = await Api.PostAsync("/api/bookings", new { }, bearer: token);
        var missing = await Api.SendAsync(noBody);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("Please choose a service", await Field(empty, "serviceId"));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    // ---------------------------------------------------------------- price and trip plan

    [Fact]
    public async Task A_published_package_gives_its_price()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();

        await BookAsync(token, new { packageId = "cultural-triangle", vehicleId = "toyota-prius" });

        Assert.Equal(99000m, await Scalar<decimal>("SELECT price_lkr FROM bookings"));
        Assert.Equal("cultural-triangle", await Scalar<string>("SELECT package_id FROM bookings"));
    }

    [Fact]
    public async Task A_booking_without_a_package_or_plan_has_no_price_and_no_balance()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token);

        var booking = await Json(await Api.GetAsync($"/api/bookings/{reference}", token));

        Assert.Equal(JsonValueKind.Null, booking.GetProperty("priceLkr").ValueKind);
        Assert.Equal(JsonValueKind.Null, booking.GetProperty("balanceLkr").ValueKind);
        Assert.Equal(0m, booking.GetProperty("paidLkr").GetDecimal());
    }

    [Fact]
    public async Task A_trip_plan_is_stored_and_returned_and_prices_the_booking()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();
        var plan = new
        {
            destinationIds = new[] { "sigiriya", "kandy" },
            days = 4,
            travelers = 2,
            vehicleId = "toyota-prius",
            stops = new[] { new { id = "sigiriya", name = "Sigiriya", lat = 7.95, lng = 80.76 } },
            distanceKm = 640,
            estimateLowLkr = 40000,
            estimateHighLkr = 50000,
        };

        var reference = await BookAsync(token, new { tripPlan = plan });
        var booking = await Json(await Api.GetAsync($"/api/bookings/{reference}", token));

        Assert.Equal(45000m, booking.GetProperty("priceLkr").GetDecimal());
        var stored = booking.GetProperty("tripPlan");
        Assert.Equal(4, stored.GetProperty("days").GetInt32());
        Assert.Equal(2, stored.GetProperty("travelers").GetInt32());
        Assert.Equal(640m, stored.GetProperty("distanceKm").GetDecimal());
        Assert.Equal("Sigiriya", stored.GetProperty("stops")[0].GetProperty("name").GetString());
        Assert.Equal(["sigiriya", "kandy"], Strings(stored.GetProperty("destinationIds")));
    }

    [Fact]
    public async Task An_invalid_trip_plan_is_rejected()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();
        var plan = new { destinationIds = new[] { "sigiriya" }, days = 4, travelers = 2, vehicleId = "x", stops = Array.Empty<object>(), distanceKm = 10, estimateLowLkr = 90000, estimateHighLkr = 50000 };

        var response = await Api.PostAsync("/api/bookings", Booking(extra: new { tripPlan = plan }), bearer: token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(await Field(response, "tripPlan"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM bookings"));
    }

    // ---------------------------------------------------------------- emails

    [Fact]
    public async Task Without_any_owner_address_only_the_customer_email_is_recorded_and_it_is_failed_with_the_reason()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();

        var reference = await BookAsync(token);

        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM email_logs"));
        Assert.Equal("booking-received", await Scalar<string>("SELECT kind FROM email_logs"));
        Assert.Equal("emma@example.com", await Scalar<string>("SELECT to_address FROM email_logs"));
        Assert.Equal(await Scalar<Guid>("SELECT id FROM bookings"), await Scalar<Guid>("SELECT booking_id FROM email_logs"));
        Assert.Equal("failed", await Scalar<string>("SELECT delivery_status FROM email_logs"));
        Assert.Equal("No email provider configured", await Scalar<string>("SELECT error FROM email_logs"));
        Assert.Contains(reference, await Scalar<string>("SELECT body FROM email_logs"), StringComparison.Ordinal);
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM bookings"));
    }

    [Fact]
    public async Task The_owner_notice_goes_to_the_business_email_of_the_settings()
    {
        await SeedContentAsync();
        await AsAdmin(HttpMethod.Put, "/api/settings", new { name = "Ceylon Roads", driverName = "Nuwan Dissanayake", phone = "+94 77 123 4567", whatsappNumber = "94771234567", email = "hello@ceylonroads.example" });
        var (_, token) = await AddCustomerAsync();

        await BookAsync(token);

        Assert.Equal(2L, await Scalar<long>("SELECT count(*) FROM email_logs"));
        Assert.Equal("hello@ceylonroads.example", await Scalar<string>("SELECT to_address FROM email_logs WHERE kind = 'booking-notice'"));
        Assert.Contains("Emma Wilson", await Scalar<string>("SELECT body FROM email_logs WHERE kind = 'booking-notice'"), StringComparison.Ordinal);
        Assert.Contains("Nuwan Dissanayake", await Scalar<string>("SELECT body FROM email_logs WHERE kind = 'booking-received'"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_configured_owner_address_wins_over_the_business_email()
    {
        await SeedContentAsync();
        await AsAdmin(HttpMethod.Put, "/api/settings", new { name = "Ceylon Roads", driverName = "Nuwan Dissanayake", phone = "+94 77 123 4567", whatsappNumber = "94771234567", email = "hello@ceylonroads.example" });
        await using var factory = ApiFactory.With(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = Connection, ["Email:OwnerEmail"] = "owner@example.com" });
        var api = new AuthApi(AuthApi.CreateClient(factory));
        var (_, token) = await AddCustomerAsync();

        var response = await api.PostAsync("/api/bookings", Booking(), bearer: token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("owner@example.com", await Scalar<string>("SELECT to_address FROM email_logs WHERE kind = 'booking-notice'"));
        Assert.Equal(2L, await Scalar<long>("SELECT count(*) FROM email_logs"));
    }

    // ---------------------------------------------------------------- the per-customer limit

    [Fact]
    public async Task The_fourth_booking_in_an_hour_is_429_and_another_customer_can_still_book()
    {
        await SeedContentAsync();
        await using var factory = ApiFactory.With(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = Connection, ["Bookings:MaxPerUserPerHour"] = "3" });
        var api = new AuthApi(AuthApi.CreateClient(factory));
        var (_, first) = await AddCustomerAsync("one@example.com");
        var (_, second) = await AddCustomerAsync("two@example.com");

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.Created, (await api.PostAsync("/api/bookings", Booking(), bearer: first)).StatusCode);
        }

        var blocked = await api.PostAsync("/api/bookings", Booking(), bearer: first);
        var other = await api.PostAsync("/api/bookings", Booking(), bearer: second);

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));
        AuthApi.AssertError(await Json(blocked), HttpStatusCode.TooManyRequests);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.Equal(4L, await Scalar<long>("SELECT count(*) FROM bookings"));

        factory.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(HttpStatusCode.Created, (await api.PostAsync("/api/bookings", Booking(), bearer: first)).StatusCode);
    }

    // ---------------------------------------------------------------- my bookings

    [Fact]
    public async Task My_bookings_are_only_mine_newest_first_without_admin_notes()
    {
        await SeedContentAsync();
        var (_, mine) = await AddCustomerAsync("emma@example.com");
        var (_, theirs) = await AddCustomerAsync("other@example.com");
        var first = await BookAsync(mine);
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var second = await BookAsync(mine);
        await BookAsync(theirs);
        await Execute("UPDATE bookings SET admin_notes = 'internal only'");

        var response = await Api.GetAsync("/api/bookings/mine", mine);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await Json(response)).GetProperty("items");
        Assert.Equal([second, first], list.EnumerateArray().Select(b => b.GetProperty("reference").GetString()));
        Assert.All(list.EnumerateArray(), booking =>
        {
            Assert.False(booking.TryGetProperty("adminNotes", out _));
            Assert.False(booking.TryGetProperty("payments", out _));
            Assert.False(booking.TryGetProperty("customer", out _));
        });
        var shape = list[0];
        Assert.Equal("pending", shape.GetProperty("status").GetString());
        Assert.Equal(Tomorrow, shape.GetProperty("date").GetString());
        Assert.Equal("09:30", shape.GetProperty("time").GetString());
        Assert.Equal("Colombo", shape.GetProperty("pickupLocation").GetString());
        Assert.Equal(string.Empty, shape.GetProperty("flightNumber").GetString());
        Assert.Equal("emma@example.com", shape.GetProperty("email").GetString());
    }

    [Fact]
    public async Task My_bookings_need_a_token_and_are_empty_for_a_new_customer()
    {
        var (_, token) = await AddCustomerAsync();

        var anonymous = await Api.GetAsync("/api/bookings/mine");
        var empty = await Api.GetAsync("/api/bookings/mine", token);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var emptyPage = await Json(empty);
        Assert.Empty(emptyPage.GetProperty("items").EnumerateArray());
        Assert.Equal(0, emptyPage.GetProperty("totalCount").GetInt32());
    }

    // ---------------------------------------------------------------- one booking

    [Fact]
    public async Task A_customer_reads_their_booking_by_id_and_by_reference_in_any_letter_case()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token);
        var id = (await Scalar<Guid>("SELECT id FROM bookings")).ToString();

        var byReference = await Json(await Api.GetAsync($"/api/bookings/{reference}", token));
        var lower = await Json(await Api.GetAsync($"/api/bookings/{reference.ToLowerInvariant()}", token));
        var byId = await Json(await Api.GetAsync($"/api/bookings/{id}", token));

        Assert.Equal(reference, byReference.GetProperty("reference").GetString());
        Assert.Equal(id, byReference.GetProperty("id").GetString());
        Assert.True(SameJson(byReference, lower));
        Assert.True(SameJson(byReference, byId));
        Assert.False(byReference.TryGetProperty("adminNotes", out _));
    }

    [Fact]
    public async Task Somebody_elses_booking_and_an_unknown_one_both_look_like_not_found()
    {
        await SeedContentAsync();
        var (_, mine) = await AddCustomerAsync("emma@example.com");
        var (_, theirs) = await AddCustomerAsync("other@example.com");
        var reference = await BookAsync(theirs);
        var id = (await Scalar<Guid>("SELECT id FROM bookings")).ToString();

        var byReference = await Api.GetAsync($"/api/bookings/{reference}", mine);
        var byId = await Api.GetAsync($"/api/bookings/{id}", mine);
        var unknown = await Api.GetAsync("/api/bookings/BK-ZZZZZZ", mine);
        var junk = await Api.GetAsync("/api/bookings/not-a-booking", mine);
        var anonymous = await Api.GetAsync($"/api/bookings/{reference}");

        foreach (var response in new[] { byReference, byId, unknown, junk })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Booking not found", (await Json(response)).GetProperty("error").GetProperty("message").GetString());
        }

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // ---------------------------------------------------------------- money totals

    [Fact]
    public async Task Paid_and_balance_follow_the_payments()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token, new { packageId = "cultural-triangle" });
        var bookingId = await Scalar<Guid>("SELECT id FROM bookings");

        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            foreach (var (status, amount) in new[] { (PaymentStatus.Paid, 40000m), (PaymentStatus.Pending, 20000m), (PaymentStatus.Paid, 10000m), (PaymentStatus.Refunded, 5000m) })
            {
                var payment = TestData.Payment(bookingId);
                payment.Status = status;
                payment.Amount = amount;
                payment.AmountLkr = amount;
                db.Payments.Add(payment);
            }

            await db.SaveChangesAsync();
        }

        var booking = await Json(await Api.GetAsync($"/api/bookings/{reference}", token));
        var list = (await Json(await Api.GetAsync("/api/bookings/mine", token))).GetProperty("items");

        Assert.Equal(99000m, booking.GetProperty("priceLkr").GetDecimal());
        Assert.Equal(45000m, booking.GetProperty("paidLkr").GetDecimal());
        Assert.Equal(54000m, booking.GetProperty("balanceLkr").GetDecimal());
        Assert.True(SameJson(booking, list[0]));
    }

    [Fact]
    public async Task An_overpaid_booking_has_a_zero_balance()
    {
        await SeedContentAsync();
        var (_, token) = await AddCustomerAsync();
        var reference = await BookAsync(token, new { packageId = "cultural-triangle" });
        var bookingId = await Scalar<Guid>("SELECT id FROM bookings");
        await using (var db = PostgresFixture.CreateContext(Connection))
        {
            var payment = TestData.Payment(bookingId);
            payment.Amount = 120000m;
            payment.AmountLkr = 120000m;
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
        }

        var booking = await Json(await Api.GetAsync($"/api/bookings/{reference}", token));

        Assert.Equal(120000m, booking.GetProperty("paidLkr").GetDecimal());
        Assert.Equal(0m, booking.GetProperty("balanceLkr").GetDecimal());
    }
}
