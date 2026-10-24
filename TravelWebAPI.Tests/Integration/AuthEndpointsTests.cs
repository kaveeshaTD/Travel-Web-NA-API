using System.Net;
using System.Text.Json;
using TravelWebAPI.Infrastructure.Services;
using TravelWebAPI.Tests.Integration.Support;

namespace TravelWebAPI.Tests.Integration;

/// <summary>Every auth endpoint through the real API and a real PostgreSQL database. Each test gets its own empty database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class AuthEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private string _connection = string.Empty;
    private ApiFactory _factory = null!;
    private AuthApi _api = null!;

    public async Task InitializeAsync()
    {
        _connection = await postgres.CreateDatabaseAsync();
        _factory = ApiFactory.With(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = _connection });
        _api = new AuthApi(AuthApi.CreateClient(_factory));
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private Task<T> Scalar<T>(string sql) => PostgresFixture.ScalarAsync<T>(_connection, sql);

    private Task<int> Execute(string sql) => PostgresFixture.ExecuteAsync(_connection, sql);

    /// <summary>Registers (or logs in) and returns the access token and refresh cookie value.</summary>
    private async Task<(string Token, string Cookie)> SignInAsync(string email = "emma@example.com", string password = "Customer123", bool register = false)
    {
        var response = register ? await _api.RegisterAsync(email, password) : await _api.LoginAsync(email, password);
        Assert.True(response.IsSuccessStatusCode, $"sign in failed: {response.StatusCode}");
        return (await AuthApi.TokenAsync(response), SetCookie.Find(response)!.Value);
    }

    // ---------------------------------------------------------------- register

    [Fact]
    public async Task Register_returns_201_with_token_and_user_and_sets_the_cookie()
    {
        var response = await _api.RegisterAsync("  Emma.Wilson@Example.com ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await AuthApi.JsonAsync(response);
        var user = body.GetProperty("user");
        Assert.Equal("emma.wilson@example.com", user.GetProperty("email").GetString());
        Assert.Equal("Emma Wilson", user.GetProperty("name").GetString());
        Assert.Equal("customer", user.GetProperty("role").GetString());
        Assert.Equal("active", user.GetProperty("status").GetString());
        Assert.Equal("+44 7700 900123", user.GetProperty("phone").GetString());
        Assert.Equal("United Kingdom", user.GetProperty("country").GetString());
        Assert.Equal(JsonValueKind.String, user.GetProperty("createdAt").ValueKind);
        Assert.False(user.TryGetProperty("passwordHash", out _));
        Assert.False(user.TryGetProperty("password", out _));
        Assert.NotNull(SetCookie.Find(response));
        Assert.Equal("emma.wilson@example.com", await Scalar<string>("SELECT email FROM users"));
    }

    [Fact]
    public async Task The_access_token_from_registration_works_straight_away()
    {
        var (token, _) = await SignInAsync(register: true);

        var me = await _api.GetAsync("/api/auth/me", token);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("emma@example.com", (await AuthApi.JsonAsync(me)).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Registering_the_same_email_in_another_case_is_409()
    {
        await _api.RegisterAsync("emma@example.com");

        var response = await _api.RegisterAsync("EMMA@Example.com");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await AuthApi.JsonAsync(response);
        Assert.Equal("That email is already registered", body.GetProperty("error").GetProperty("fields").GetProperty("email").GetString());
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM users"));
    }

    [Fact]
    public async Task Register_with_invalid_fields_is_400_naming_each_one()
    {
        var response = await _api.PostAsync("/api/auth/register", new { name = "", email = "nope", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await AuthApi.JsonAsync(response)).GetProperty("error").GetProperty("fields");
        Assert.Equal("Enter your full name", fields.GetProperty("name").GetString());
        Assert.Equal("Enter a valid email address", fields.GetProperty("email").GetString());
        Assert.Equal("Use at least 8 characters with letters and numbers", fields.GetProperty("password").GetString());
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM users"));
    }

    [Fact]
    public async Task Register_with_an_empty_body_is_400_not_500()
    {
        var response = await _api.PostAsync("/api/auth/register");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registration_records_the_welcome_email_as_failed_when_no_provider_exists_and_still_succeeds()
    {
        await _api.RegisterAsync();

        Assert.Equal("welcome", await Scalar<string>("SELECT kind FROM email_logs"));
        Assert.Equal("failed", await Scalar<string>("SELECT delivery_status FROM email_logs"));
        Assert.Equal("No email provider configured", await Scalar<string>("SELECT error FROM email_logs"));
        Assert.Equal("emma@example.com", await Scalar<string>("SELECT to_address FROM email_logs"));
    }

    // ---------------------------------------------------------------- login

    [Fact]
    public async Task Login_returns_200_with_token_user_and_cookie_and_records_the_time()
    {
        await _api.RegisterAsync();

        var response = await _api.LoginAsync("  EMMA@example.com ", "Customer123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await AuthApi.JsonAsync(response);
        Assert.Equal("emma@example.com", body.GetProperty("user").GetProperty("email").GetString());
        Assert.NotNull(SetCookie.Find(response));
        Assert.True(await Scalar<bool>("SELECT last_login_at IS NOT NULL FROM users"));
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_the_same_401()
    {
        await _api.RegisterAsync();

        var wrong = await _api.LoginAsync("emma@example.com", "Wrong12345");
        var unknown = await _api.LoginAsync("ghost@example.com", "Wrong12345");

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        AuthApi.AssertError(await AuthApi.JsonAsync(wrong), HttpStatusCode.Unauthorized, "Email or password is incorrect");
        AuthApi.AssertError(await AuthApi.JsonAsync(unknown), HttpStatusCode.Unauthorized, "Email or password is incorrect");
        Assert.Null(SetCookie.Find(wrong));
        Assert.Null(SetCookie.Find(unknown));
    }

    [Fact]
    public async Task A_deactivated_user_with_the_right_password_gets_403_and_no_cookie()
    {
        await _api.RegisterAsync();
        await Execute("UPDATE users SET status = 'inactive'");

        var response = await _api.LoginAsync("emma@example.com", "Customer123");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AuthApi.AssertError(await AuthApi.JsonAsync(response), HttpStatusCode.Forbidden, "This account has been deactivated");
        Assert.Null(SetCookie.Find(response));
    }

    [Fact]
    public async Task A_deactivated_user_with_a_wrong_password_only_gets_401()
    {
        await _api.RegisterAsync();
        await Execute("UPDATE users SET status = 'inactive'");

        var response = await _api.LoginAsync("emma@example.com", "Wrong12345");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------------------------------------------------------- lockout

    private async Task FailLoginAsync(string email, int times)
    {
        for (var i = 0; i < times; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _api.LoginAsync(email, "Wrong12345")).StatusCode);
        }
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_even_the_right_password_gets_429_with_retry_after()
    {
        await _api.RegisterAsync();
        await FailLoginAsync("emma@example.com", 5);

        var response = await _api.LoginAsync("emma@example.com", "Customer123");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("900", Assert.Single(response.Headers.GetValues("Retry-After")));
        AuthApi.AssertError(await AuthApi.JsonAsync(response), HttpStatusCode.TooManyRequests, "Too many sign-in attempts. Try again in 15 minutes.");
        Assert.Null(SetCookie.Find(response));
    }

    [Fact]
    public async Task The_lock_lifts_after_15_minutes()
    {
        await _api.RegisterAsync();
        await FailLoginAsync("emma@example.com", 5);

        _factory.Clock.Advance(TimeSpan.FromMinutes(15));
        var response = await _api.LoginAsync("emma@example.com", "Customer123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await Scalar<bool>("SELECT locked_until IS NOT NULL FROM users"));
    }

    [Fact]
    public async Task A_good_login_resets_the_wrong_password_counter()
    {
        await _api.RegisterAsync();
        await FailLoginAsync("emma@example.com", 3);
        Assert.Equal(3, await Scalar<int>("SELECT failed_login_count FROM users"));

        await _api.LoginAsync("emma@example.com", "Customer123");

        Assert.Equal(0, await Scalar<int>("SELECT failed_login_count FROM users"));
    }

    [Fact]
    public async Task An_unknown_email_is_locked_exactly_like_a_real_one()
    {
        await FailLoginAsync("ghost@example.com", 5);

        var response = await _api.LoginAsync("ghost@example.com", "Customer123");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("900", Assert.Single(response.Headers.GetValues("Retry-After")));
        AuthApi.AssertError(await AuthApi.JsonAsync(response), HttpStatusCode.TooManyRequests, "Too many sign-in attempts. Try again in 15 minutes.");
    }

    [Fact]
    public async Task An_old_password_hash_is_upgraded_on_sign_in()
    {
        var oldHash = new PasswordHasher(iterations: 1_000).Hash("Customer123");
        await Execute(
            $"INSERT INTO users (id, name, email, password_hash, role, status, created_at, updated_at) VALUES (gen_random_uuid(), 'Emma', 'emma@example.com', '{oldHash}', 'customer', 'active', now(), now())");
        await using var realFactory = ApiFactory.With(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = _connection },
            realHasher: true);
        var api = new AuthApi(AuthApi.CreateClient(realFactory));

        var response = await api.LoginAsync("emma@example.com", "Customer123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await Scalar<string>("SELECT password_hash FROM users");
        Assert.StartsWith("v1.600000.", stored, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await api.LoginAsync("emma@example.com", "Customer123")).StatusCode);
    }

    // ---------------------------------------------------------------- refresh and logout

    [Fact]
    public async Task Refresh_returns_a_new_token_and_cookie_and_the_old_cookie_stops_working()
    {
        var (_, oldCookie) = await SignInAsync(register: true);

        var response = await _api.PostAsync("/api/auth/refresh", refreshCookie: oldCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await AuthApi.JsonAsync(response);
        Assert.Equal("emma@example.com", body.GetProperty("user").GetProperty("email").GetString());
        var newCookie = SetCookie.Find(response)!;
        Assert.NotEqual(oldCookie, newCookie.Value);
        Assert.True(newCookie.HttpOnly);

        var me = await _api.GetAsync("/api/auth/me", body.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        _factory.Clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostAsync("/api/auth/refresh", refreshCookie: oldCookie)).StatusCode);
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_401_and_clears_nothing()
    {
        var response = await _api.PostAsync("/api/auth/refresh");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AuthApi.AssertError(await AuthApi.JsonAsync(response), HttpStatusCode.Unauthorized);
        Assert.Null(SetCookie.Find(response));
    }

    [Fact]
    public async Task Refresh_with_an_unknown_cookie_is_401_and_clears_it()
    {
        var response = await _api.PostAsync("/api/auth/refresh", refreshCookie: "not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(SetCookie.Find(response)!.IsCleared);
    }

    [Fact]
    public async Task Refresh_with_an_expired_cookie_is_401_and_clears_it()
    {
        var (_, cookie) = await SignInAsync(register: true);
        await _api.LoginAsync("emma@example.com", "Customer123");
        var normalLogin = await _api.LoginAsync("emma@example.com", "Customer123");
        var oneDayCookie = SetCookie.Find(normalLogin)!.Value;

        _factory.Clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
        var response = await _api.PostAsync("/api/auth/refresh", refreshCookie: oneDayCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(SetCookie.Find(response)!.IsCleared);

        // The 30 day session from registration is still good.
        Assert.Equal(HttpStatusCode.OK, (await _api.PostAsync("/api/auth/refresh", refreshCookie: cookie)).StatusCode);
    }

    [Fact]
    public async Task Refresh_for_a_deactivated_user_is_401_and_ends_their_sessions()
    {
        var (_, cookie) = await SignInAsync(register: true);
        await Execute("UPDATE users SET status = 'inactive'");

        var response = await _api.PostAsync("/api/auth/refresh", refreshCookie: cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(SetCookie.Find(response)!.IsCleared);
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM refresh_tokens WHERE revoked_at IS NULL"));
    }

    [Fact]
    public async Task A_30_day_session_stays_30_days_after_refreshing()
    {
        var (_, cookie) = await SignInAsync(register: true);
        _factory.Clock.Advance(TimeSpan.FromDays(10));

        var response = await _api.PostAsync("/api/auth/refresh", refreshCookie: cookie);

        var renewed = SetCookie.Find(response)!;
        Assert.InRange((renewed.Expires!.Value - _factory.Clock.Now).TotalDays, 29.99, 30.01);
    }

    [Fact]
    public async Task Reusing_a_rotated_cookie_after_10_seconds_revokes_every_session_of_the_user()
    {
        var (_, first) = await SignInAsync(register: true);
        var (_, other) = await SignInAsync(register: false);
        var rotated = SetCookie.Find(await _api.PostAsync("/api/auth/refresh", refreshCookie: first))!.Value;

        _factory.Clock.Advance(TimeSpan.FromSeconds(11));
        var replay = await _api.PostAsync("/api/auth/refresh", refreshCookie: first);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.True(SetCookie.Find(replay)!.IsCleared);
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM refresh_tokens WHERE revoked_at IS NULL"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostAsync("/api/auth/refresh", refreshCookie: rotated)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostAsync("/api/auth/refresh", refreshCookie: other)).StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_cookie_within_10_seconds_does_not_sign_the_user_out()
    {
        var (_, first) = await SignInAsync(register: true);
        var rotated = SetCookie.Find(await _api.PostAsync("/api/auth/refresh", refreshCookie: first))!.Value;

        _factory.Clock.Advance(TimeSpan.FromSeconds(3));
        var second = await _api.PostAsync("/api/auth/refresh", refreshCookie: first);

        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Null(SetCookie.Find(second));
        Assert.Equal(HttpStatusCode.OK, (await _api.PostAsync("/api/auth/refresh", refreshCookie: rotated)).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_cookie_clears_it_and_works_without_an_access_token()
    {
        var (_, cookie) = await SignInAsync(register: true);

        var response = await _api.PostAsync("/api/auth/logout", refreshCookie: cookie);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.True(SetCookie.Find(response)!.IsCleared);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostAsync("/api/auth/refresh", refreshCookie: cookie)).StatusCode);
    }

    [Fact]
    public async Task Logout_without_a_cookie_is_204()
    {
        var response = await _api.PostAsync("/api/auth/logout");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Logging_out_one_device_leaves_the_others_signed_in()
    {
        var (_, first) = await SignInAsync(register: true);
        var (_, second) = await SignInAsync();

        await _api.PostAsync("/api/auth/logout", refreshCookie: first);

        Assert.Equal(HttpStatusCode.OK, (await _api.PostAsync("/api/auth/refresh", refreshCookie: second)).StatusCode);
    }

    // ---------------------------------------------------------------- profile

    [Fact]
    public async Task Me_returns_the_signed_in_user_and_needs_a_token()
    {
        var (token, _) = await SignInAsync(register: true);

        var ok = await _api.GetAsync("/api/auth/me", token);
        var anonymous = await _api.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Emma Wilson", (await AuthApi.JsonAsync(ok)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task A_deactivated_user_with_a_still_valid_token_is_refused_by_me()
    {
        var (token, _) = await SignInAsync(register: true);
        await Execute("UPDATE users SET status = 'inactive'");

        var response = await _api.GetAsync("/api/auth/me", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Updating_name_phone_and_country_returns_the_updated_user()
    {
        var (token, _) = await SignInAsync(register: true);

        var response = await _api.SendAsync(HttpMethod.Put, "/api/auth/me", new { name = "Emma W", phone = "0771234567", country = "Sri Lanka" }, bearer: token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await AuthApi.JsonAsync(response);
        Assert.Equal("Emma W", user.GetProperty("name").GetString());
        Assert.Equal("0771234567", user.GetProperty("phone").GetString());
        Assert.Equal("Sri Lanka", user.GetProperty("country").GetString());
        Assert.Equal("Emma W", await Scalar<string>("SELECT name FROM users"));
    }

    [Fact]
    public async Task Updating_with_an_invalid_name_is_400()
    {
        var (token, _) = await SignInAsync(register: true);

        var response = await _api.SendAsync(HttpMethod.Put, "/api/auth/me", new { name = "E" }, bearer: token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await AuthApi.JsonAsync(response)).GetProperty("error").GetProperty("fields");
        Assert.Equal("Enter your full name", fields.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Changing_the_password_with_a_wrong_current_password_is_400()
    {
        var (token, _) = await SignInAsync(register: true);

        var response = await _api.SendAsync(HttpMethod.Put, "/api/auth/me", new { currentPassword = "Wrong12345", newPassword = "Brand1New" }, bearer: token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await AuthApi.JsonAsync(response)).GetProperty("error").GetProperty("fields");
        Assert.Equal("Current password is incorrect", fields.GetProperty("currentPassword").GetString());
    }

    [Fact]
    public async Task Changing_to_a_weak_password_is_400()
    {
        var (token, _) = await SignInAsync(register: true);

        var response = await _api.SendAsync(HttpMethod.Put, "/api/auth/me", new { currentPassword = "Customer123", newPassword = "weak" }, bearer: token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await AuthApi.JsonAsync(response)).GetProperty("error").GetProperty("fields");
        Assert.Equal("Use at least 8 characters with letters and numbers", fields.GetProperty("newPassword").GetString());
    }

    [Fact]
    public async Task After_a_password_change_other_devices_are_signed_out_and_this_one_carries_on()
    {
        var (token, thisDevice) = await SignInAsync(register: true);
        var (_, otherDevice) = await SignInAsync();

        var response = await _api.SendAsync(
            HttpMethod.Put,
            "/api/auth/me",
            new { currentPassword = "Customer123", newPassword = "Brand1New" },
            refreshCookie: thisDevice,
            bearer: token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostAsync("/api/auth/refresh", refreshCookie: otherDevice)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.PostAsync("/api/auth/refresh", refreshCookie: thisDevice)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.LoginAsync("emma@example.com", "Brand1New")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.LoginAsync("emma@example.com", "Customer123")).StatusCode);
    }

    // ---------------------------------------------------------------- forgot and reset

    /// <summary>The raw token only exists in the email handed to the provider, never in the database.</summary>
    private Task<string> LatestResetTokenAsync() => Task.FromResult(_factory.Emails.LatestResetToken());

    [Fact]
    public async Task Forgot_gives_the_same_answer_for_known_and_unknown_emails()
    {
        await _api.RegisterAsync();

        var known = await _api.PostAsync("/api/auth/forgot", new { email = "emma@example.com" });
        var unknown = await _api.PostAsync("/api/auth/forgot", new { email = "ghost@example.com" });

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.True((await AuthApi.JsonAsync(known)).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Forgot_only_creates_a_token_and_an_email_when_the_account_exists()
    {
        await _api.RegisterAsync();
        await _api.PostAsync("/api/auth/forgot", new { email = "ghost@example.com" });
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM password_reset_tokens"));
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM email_logs WHERE kind = 'password-reset'"));

        await _api.PostAsync("/api/auth/forgot", new { email = "  EMMA@example.com " });

        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM password_reset_tokens"));
        var token = await LatestResetTokenAsync();
        var sent = _factory.Emails.Messages.Last(m => m.Kind == TravelWebAPI.Domain.Enums.EmailKind.PasswordReset);
        Assert.Contains($"http://localhost:5173/reset-password?token={token}", sent.Body, StringComparison.Ordinal);
        Assert.Equal("failed", await Scalar<string>("SELECT delivery_status FROM email_logs WHERE kind = 'password-reset'"));

        // The database keeps only the hash of the token, and the saved email text has the token removed.
        var stored = await Scalar<string>("SELECT token_hash FROM password_reset_tokens");
        Assert.NotEqual(token, stored);
        Assert.Matches("^[0-9a-f]{64}$", stored);
        var savedBody = await Scalar<string>("SELECT body FROM email_logs WHERE kind = 'password-reset'");
        Assert.DoesNotContain(token, savedBody, StringComparison.Ordinal);
        Assert.Contains("http://localhost:5173/reset-password?token=[REDACTED]", savedBody, StringComparison.Ordinal);
        Assert.DoesNotContain(token, await Scalar<string>("SELECT string_agg(body || subject, ' ') FROM email_logs"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forgot_with_an_invalid_email_is_400()
    {
        var response = await _api.PostAsync("/api/auth/forgot", new { email = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await AuthApi.JsonAsync(response)).GetProperty("error").GetProperty("fields");
        Assert.Equal("Enter a valid email address", fields.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Reset_sets_the_new_password_once()
    {
        await _api.RegisterAsync();
        await _api.PostAsync("/api/auth/forgot", new { email = "emma@example.com" });
        var token = await LatestResetTokenAsync();

        var response = await _api.PostAsync("/api/auth/reset", new { token, newPassword = "Brand1New" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.LoginAsync("emma@example.com", "Brand1New")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.LoginAsync("emma@example.com", "Customer123")).StatusCode);

        var again = await _api.PostAsync("/api/auth/reset", new { token, newPassword = "Another1Pass" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var fields = (await AuthApi.JsonAsync(again)).GetProperty("error").GetProperty("fields");
        Assert.Equal("This reset link is invalid or has expired", fields.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Reset_with_an_unknown_or_expired_token_is_400()
    {
        await _api.RegisterAsync();
        await _api.PostAsync("/api/auth/forgot", new { email = "emma@example.com" });
        var token = await LatestResetTokenAsync();

        var unknown = await _api.PostAsync("/api/auth/reset", new { token = "nope", newPassword = "Brand1New" });
        _factory.Clock.Advance(TimeSpan.FromMinutes(61));
        var expired = await _api.PostAsync("/api/auth/reset", new { token, newPassword = "Brand1New" });

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.LoginAsync("emma@example.com", "Brand1New")).StatusCode);
    }

    [Fact]
    public async Task Reset_with_a_weak_password_is_400_and_the_link_still_works()
    {
        await _api.RegisterAsync();
        await _api.PostAsync("/api/auth/forgot", new { email = "emma@example.com" });
        var token = await LatestResetTokenAsync();

        var weak = await _api.PostAsync("/api/auth/reset", new { token, newPassword = "weak" });

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var fields = (await AuthApi.JsonAsync(weak)).GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("newPassword", out _));
        Assert.Equal(HttpStatusCode.NoContent, (await _api.PostAsync("/api/auth/reset", new { token, newPassword = "Brand1New" })).StatusCode);
    }

    [Fact]
    public async Task A_second_reset_request_makes_the_first_link_useless()
    {
        await _api.RegisterAsync();
        await _api.PostAsync("/api/auth/forgot", new { email = "emma@example.com" });
        var first = await LatestResetTokenAsync();
        await _api.PostAsync("/api/auth/forgot", new { email = "emma@example.com" });
        var second = await LatestResetTokenAsync();

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PostAsync("/api/auth/reset", new { token = first, newPassword = "Brand1New" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.PostAsync("/api/auth/reset", new { token = second, newPassword = "Brand1New" })).StatusCode);
    }

    [Fact]
    public async Task A_locked_account_can_sign_in_straight_after_a_reset()
    {
        await _api.RegisterAsync();
        await FailLoginAsync("emma@example.com", 5);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _api.LoginAsync("emma@example.com", "Customer123")).StatusCode);
        await _api.PostAsync("/api/auth/forgot", new { email = "emma@example.com" });
        var token = await LatestResetTokenAsync();

        await _api.PostAsync("/api/auth/reset", new { token, newPassword = "Brand1New" });

        Assert.Equal(HttpStatusCode.OK, (await _api.LoginAsync("emma@example.com", "Brand1New")).StatusCode);
    }

    [Fact]
    public async Task A_reset_ends_every_session()
    {
        var (_, cookie) = await SignInAsync(register: true);
        await _api.PostAsync("/api/auth/forgot", new { email = "emma@example.com" });
        var token = await LatestResetTokenAsync();

        await _api.PostAsync("/api/auth/reset", new { token, newPassword = "Brand1New" });

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.PostAsync("/api/auth/refresh", refreshCookie: cookie)).StatusCode);
    }
}

/// <summary>The seeded demo logins must keep working through the real API.</summary>
[Collection(PostgresCollection.Name)]
public sealed class SeededLoginTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_seeded_demo_accounts_can_sign_in()
    {
        var connection = await postgres.CreateDatabaseAsync(migrate: false);
        await using var factory = ApiFactory.With(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connection,
                ["Seed:Enabled"] = "true",
                ["Seed:IncludeSampleActivity"] = "false",
            },
            "Development");
        var api = new AuthApi(AuthApi.CreateClient(factory));

        var admin = await api.LoginAsync("admin@ceylonroads.example", "Admin@12345");
        var customer = await api.LoginAsync("emma.wilson@example.com", "Customer123");
        var inactive = await api.LoginAsync("jack.miller@example.com", "Customer123");

        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        Assert.Equal("admin", (await AuthApi.JsonAsync(admin)).GetProperty("user").GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, customer.StatusCode);
        Assert.Equal("customer", (await AuthApi.JsonAsync(customer)).GetProperty("user").GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, inactive.StatusCode);
    }
}
