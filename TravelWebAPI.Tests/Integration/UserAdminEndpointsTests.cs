using System.Net;
using System.Text.Json;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>The admin's user routes through the real API and database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class UserAdminEndpointsTests(PostgresFixture postgres) : ContentTestBase(postgres)
{
    private static object NewUser(string email = "new.person@example.com", string role = "customer", string name = "New Person") => new
    {
        name,
        email,
        password = "Secret123",
        role,
        phone = "+94 77 123 4567",
        country = "Sri Lanka",
    };

    private async Task<JsonElement> CreateUserAsync(object body)
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/users", body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    /// <summary>Creates an admin account through the API and returns its id and an access token from a real sign-in.</summary>
    private async Task<(string Id, string Token)> SignedInAdminAsync(string email = "second.admin@example.com")
    {
        var created = await CreateUserAsync(NewUser(email, "admin", "Second Admin"));
        var login = await Api.LoginAsync(email, "Secret123");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (created.GetProperty("id").GetString()!, await AuthApi.TokenAsync(login));
    }

    // ---------------------------------------------------------------- access

    [Fact]
    public async Task Every_user_route_refuses_guests_and_customers()
    {
        var created = await CreateUserAsync(NewUser());
        var id = created.GetProperty("id").GetString();
        var attempts = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, "/api/users", null),
            (HttpMethod.Post, "/api/users", NewUser("other@example.com")),
            (HttpMethod.Put, $"/api/users/{id}", new { name = "Changed Name" }),
        };

        foreach (var (method, path, body) in attempts)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Api.SendAsync(method, path, body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Api.SendAsync(method, path, body, bearer: CustomerToken)).StatusCode);
        }

        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM users"));
        Assert.Equal("New Person", await Scalar<string>("SELECT name FROM users"));
    }

    // ---------------------------------------------------------------- list

    [Fact]
    public async Task The_list_is_newest_first_with_booking_counts_and_no_password_data()
    {
        await CreateAsync("services", Service("Private tour"));
        await AddBookingAsync("private-tour", reference: "BK-AAA111");
        await AddBookingAsync("private-tour", reference: "BK-BBB222");
        Factory.Clock.Advance(TimeSpan.FromHours(1));
        await CreateUserAsync(NewUser());

        var list = (await Json(await AsAdmin(HttpMethod.Get, "/api/users"))).GetProperty("items");

        Assert.Equal(["new.person@example.com", "booker@example.com"], list.EnumerateArray().Select(u => u.GetProperty("email").GetString()));
        Assert.Equal([0, 2], list.EnumerateArray().Select(u => u.GetProperty("bookingCount").GetInt32()));
        Assert.All(list.EnumerateArray(), user =>
        {
            Assert.False(user.TryGetProperty("passwordHash", out _));
            Assert.False(user.TryGetProperty("password", out _));
            Assert.Equal("customer", user.GetProperty("role").GetString());
            Assert.Equal("active", user.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.String, user.GetProperty("createdAt").ValueKind);
        });
    }

    // ---------------------------------------------------------------- create

    [Fact]
    public async Task Create_is_201_with_a_location_and_the_new_person_can_sign_in()
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/users", NewUser("  New.Person@Example.com "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/users", response.Headers.Location!.ToString());
        var user = await Json(response);
        Assert.Equal("new.person@example.com", user.GetProperty("email").GetString());
        Assert.Equal("New Person", user.GetProperty("name").GetString());
        Assert.Equal("+94 77 123 4567", user.GetProperty("phone").GetString());
        Assert.Equal("Sri Lanka", user.GetProperty("country").GetString());
        Assert.Equal(0, user.GetProperty("bookingCount").GetInt32());
        Assert.False(user.TryGetProperty("passwordHash", out _));
        Assert.DoesNotContain("Secret123", await Scalar<string>("SELECT password_hash FROM users"));
        Assert.Equal(HttpStatusCode.OK, (await Api.LoginAsync("new.person@example.com", "Secret123")).StatusCode);
    }

    [Fact]
    public async Task A_new_admin_account_gets_the_admin_role()
    {
        await CreateUserAsync(NewUser("boss@example.com", "admin", "Boss Person"));

        Assert.Equal("admin", await Scalar<string>("SELECT role FROM users"));
    }

    [Fact]
    public async Task A_duplicate_email_is_409_in_any_letter_case_and_stores_nothing()
    {
        await CreateUserAsync(NewUser());

        var response = await AsAdmin(HttpMethod.Post, "/api/users", NewUser("NEW.PERSON@example.com"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("That email is already registered", await Field(response, "email"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM users"));
    }

    [Fact]
    public async Task Wrong_values_get_all_their_messages()
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/users", new { name = "A", email = "bad", password = "short", role = "owner" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter a name", await Field(response, "name"));
        Assert.Equal("Enter a valid email address", await Field(response, "email"));
        Assert.Equal("Use at least 8 characters with letters and numbers", await Field(response, "password"));
        Assert.Equal("Role must be admin or customer", await Field(response, "role"));
        Assert.Equal(0L, await Scalar<long>("SELECT count(*) FROM users"));
    }

    [Fact]
    public async Task An_empty_body_is_a_400()
    {
        var response = await AsAdmin(HttpMethod.Post, "/api/users", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------- edit

    [Fact]
    public async Task An_edit_changes_only_what_is_sent_and_never_the_email_or_password()
    {
        var created = await CreateUserAsync(NewUser());
        var id = created.GetProperty("id").GetString();
        var hash = await Scalar<string>("SELECT password_hash FROM users");

        var response = await AsAdmin(HttpMethod.Put, $"/api/users/{id}", new { role = "admin", phone = "", email = "other@example.com", password = "Another123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await Json(response);
        Assert.Equal("admin", user.GetProperty("role").GetString());
        Assert.Equal(string.Empty, user.GetProperty("phone").GetString());
        Assert.Equal("New Person", user.GetProperty("name").GetString());
        Assert.Equal("Sri Lanka", user.GetProperty("country").GetString());
        Assert.Equal("new.person@example.com", await Scalar<string>("SELECT email FROM users"));
        Assert.Equal(hash, await Scalar<string>("SELECT password_hash FROM users"));
    }

    [Fact]
    public async Task A_wrong_edit_changes_nothing()
    {
        var created = await CreateUserAsync(NewUser());
        var id = created.GetProperty("id").GetString();

        var response = await AsAdmin(HttpMethod.Put, $"/api/users/{id}", new { name = "E", role = "owner", status = "banned" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Enter a name", await Field(response, "name"));
        Assert.Equal("Role must be admin or customer", await Field(response, "role"));
        Assert.Equal("Status must be active or inactive", await Field(response, "status"));
        Assert.Equal("New Person", await Scalar<string>("SELECT name FROM users"));
    }

    [Fact]
    public async Task An_unknown_user_is_404()
    {
        var missing = await AsAdmin(HttpMethod.Put, $"/api/users/{Guid.NewGuid()}", new { name = "Someone" });
        var notAnId = await AsAdmin(HttpMethod.Put, "/api/users/nope", new { name = "Someone" });

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notAnId.StatusCode);
    }

    // ---------------------------------------------------------------- own account

    [Fact]
    public async Task An_admin_cannot_demote_or_deactivate_themselves_but_can_edit_their_name()
    {
        var (id, token) = await SignedInAdminAsync();

        var demote = await Api.SendAsync(HttpMethod.Put, $"/api/users/{id}", new { role = "customer" }, bearer: token);
        var deactivate = await Api.SendAsync(HttpMethod.Put, $"/api/users/{id}", new { status = "inactive" }, bearer: token);
        var rename = await Api.SendAsync(HttpMethod.Put, $"/api/users/{id}", new { name = "Nuwan Admin" }, bearer: token);

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal("You cannot remove your own admin access or deactivate yourself", (await Json(demote)).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal("admin", await Scalar<string>("SELECT role FROM users WHERE name = 'Nuwan Admin'"));
        Assert.Equal("active", await Scalar<string>("SELECT status FROM users WHERE name = 'Nuwan Admin'"));
    }

    [Fact]
    public async Task One_admin_can_deactivate_another()
    {
        var (id, _) = await SignedInAdminAsync();

        var response = await AsAdmin(HttpMethod.Put, $"/api/users/{id}", new { status = "inactive" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("inactive", (await Json(response)).GetProperty("status").GetString());
    }

    // ---------------------------------------------------------------- sessions

    [Fact]
    public async Task A_deactivated_user_cannot_sign_in_or_refresh_and_can_again_after_reactivation()
    {
        await CreateAsync("services", Service("Private tour"));
        var created = await CreateUserAsync(NewUser());
        var id = created.GetProperty("id").GetString();
        var login = await Api.LoginAsync("new.person@example.com", "Secret123");
        var cookie = SetCookie.Find(login)!.Value;
        await Execute($"INSERT INTO bookings (id, reference, user_id, service_id, travel_date, travel_time, adults, name, email, whatsapp, country) " +
                      $"VALUES (gen_random_uuid(), 'BK-KEEP11', '{id}', 'private-tour', '2030-01-15', '09:30', 2, 'N', 'n@example.com', '+94', 'LK')");

        var off = await AsAdmin(HttpMethod.Put, $"/api/users/{id}", new { status = "inactive" });
        var refresh = await Api.PostAsync("/api/auth/refresh", refreshCookie: cookie);
        var again = await Api.LoginAsync("new.person@example.com", "Secret123");

        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Equal(1, (await Json(off)).GetProperty("bookingCount").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
        Assert.Equal(0L, await Scalar<long>($"SELECT count(*) FROM refresh_tokens WHERE user_id = '{id}' AND revoked_at IS NULL"));
        Assert.Equal(1L, await Scalar<long>("SELECT count(*) FROM bookings WHERE reference = 'BK-KEEP11'"));

        var on = await AsAdmin(HttpMethod.Put, $"/api/users/{id}", new { status = "active" });
        var back = await Api.LoginAsync("new.person@example.com", "Secret123");

        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
    }
}
