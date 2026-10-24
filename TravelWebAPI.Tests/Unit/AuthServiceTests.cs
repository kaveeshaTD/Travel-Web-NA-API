using TravelWebAPI.Application.Auth;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;
using ValidationException = TravelWebAPI.Domain.Exceptions.ValidationException;

namespace TravelWebAPI.Tests.Unit;

public class AuthServiceTests
{
    private readonly AuthHarness _h = new();

    private static RegisterRequest ValidRegistration(string email = "Emma.Wilson@Example.com") => new()
    {
        Name = "  Emma Wilson ",
        Email = $" {email} ",
        Password = "Customer123",
        Phone = "+44 7700 900123",
        Country = "United Kingdom",
    };

    // ---------------------------------------------------------------- register

    [Fact]
    public async Task Register_creates_a_customer_signs_in_and_queues_the_welcome_email()
    {
        var session = await _h.Service.RegisterAsync(ValidRegistration(), AuthHarness.Client);

        var user = Assert.Single(_h.Users);
        Assert.Equal("emma.wilson@example.com", user.Email);
        Assert.Equal("Emma Wilson", user.Name);
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal("hash:Customer123", user.PasswordHash);
        Assert.Equal(session.Response.User.Id, user.Id);
        Assert.Equal("access-token-for-emma.wilson@example.com", session.Response.Token);

        var email = Assert.Single(_h.QueuedEmails);
        Assert.Equal(EmailKind.Welcome, email.Kind);
        Assert.Equal("emma.wilson@example.com", email.To);
        Assert.Equal("Welcome to Ceylon Roads", email.Subject);
        Assert.Single(_h.DispatchedEmails);
    }

    [Fact]
    public async Task Register_stores_only_the_hash_of_a_30_day_refresh_token()
    {
        var session = await _h.Service.RegisterAsync(ValidRegistration(), AuthHarness.Client);

        var stored = Assert.Single(_h.RefreshTokens);
        Assert.NotEqual(session.RefreshToken, stored.TokenHash);
        Assert.Equal(_h.Tokens.Hash(session.RefreshToken), stored.TokenHash);
        Assert.True(stored.Remember);
        Assert.Equal(_h.Clock.Now.AddDays(30), stored.ExpiresAt);
        Assert.Equal(stored.ExpiresAt, session.RefreshExpiresAt);
        Assert.Equal("203.0.113.5", stored.CreatedByIp);
    }

    [Fact]
    public async Task Register_rejects_an_email_that_exists_ignoring_case_and_spaces()
    {
        _h.AddUser("emma.wilson@example.com");

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _h.Service.RegisterAsync(ValidRegistration("EMMA.WILSON@example.com"), AuthHarness.Client));

        Assert.Equal("That email is already registered", ex.Fields["email"]);
        Assert.Single(_h.Users);
    }

    [Fact]
    public async Task Register_reports_every_invalid_field_with_the_contract_messages()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _h.Service.RegisterAsync(
            new RegisterRequest { Name = "", Email = "nope", Password = "short" },
            AuthHarness.Client));

        Assert.Equal("Enter your full name", ex.Fields["name"]);
        Assert.Equal("Enter a valid email address", ex.Fields["email"]);
        Assert.Equal("Use at least 8 characters with letters and numbers", ex.Fields["password"]);
        Assert.Empty(_h.Users);
    }

    [Theory]
    [InlineData("onlyletters")]
    [InlineData("12345678")]
    [InlineData("short1")]
    public async Task Register_rejects_weak_passwords(string password)
    {
        var request = ValidRegistration() with { Password = password };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _h.Service.RegisterAsync(request, AuthHarness.Client));

        Assert.True(ex.Fields.ContainsKey("password"));
    }

    // ---------------------------------------------------------------- login

    [Fact]
    public async Task Login_succeeds_with_any_case_and_surrounding_spaces_and_records_the_time()
    {
        var user = _h.AddUser("admin@ceylonroads.example", "Admin@12345", role: UserRole.Admin);

        var session = await _h.Service.LoginAsync(new LoginRequest { Email = " ADMIN@ceylonroads.example ", Password = "Admin@12345" }, AuthHarness.Client);

        Assert.Equal(UserRole.Admin, session.Response.User.Role);
        Assert.Equal(_h.Clock.Now, user.LastLoginAt);
    }

    [Fact]
    public async Task Login_gives_the_same_401_for_a_wrong_password_and_an_unknown_email()
    {
        _h.AddUser("emma@example.com");

        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() => _h.Service.LoginAsync(new LoginRequest { Email = "emma@example.com", Password = "Wrong123" }, AuthHarness.Client));
        var unknown = await Assert.ThrowsAsync<UnauthorizedException>(() => _h.Service.LoginAsync(new LoginRequest { Email = "ghost@example.com", Password = "Wrong123" }, AuthHarness.Client));

        Assert.Equal("Email or password is incorrect", wrong.Message);
        Assert.Equal(wrong.Message, unknown.Message);
    }

    [Fact]
    public async Task Login_without_a_password_is_a_bad_credentials_error()
    {
        _h.AddUser("emma@example.com");

        await Assert.ThrowsAsync<UnauthorizedException>(() => _h.Service.LoginAsync(new LoginRequest { Email = "emma@example.com" }, AuthHarness.Client));
    }

    [Fact]
    public async Task Login_refresh_token_lasts_1_day_by_default_and_30_days_with_remember()
    {
        _h.AddUser("emma@example.com");

        var normal = await _h.Service.LoginAsync(new LoginRequest { Email = "emma@example.com", Password = "Customer123" }, AuthHarness.Client);
        var remembered = await _h.Service.LoginAsync(new LoginRequest { Email = "emma@example.com", Password = "Customer123", Remember = true }, AuthHarness.Client);

        Assert.Equal(_h.Clock.Now.AddDays(1), normal.RefreshExpiresAt);
        Assert.Equal(_h.Clock.Now.AddDays(30), remembered.RefreshExpiresAt);
        Assert.Contains(_h.RefreshTokens, t => t is { Remember: true });
    }

    [Fact]
    public async Task Inactive_user_with_the_right_password_gets_403_and_no_session()
    {
        _h.AddUser("jack@example.com", status: UserStatus.Inactive);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _h.Service.LoginAsync(new LoginRequest { Email = "jack@example.com", Password = "Customer123" }, AuthHarness.Client));

        Assert.Equal("This account has been deactivated", ex.Message);
        Assert.Empty(_h.RefreshTokens);
    }

    [Fact]
    public async Task Inactive_user_with_a_wrong_password_only_gets_the_generic_401()
    {
        _h.AddUser("jack@example.com", status: UserStatus.Inactive);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _h.Service.LoginAsync(new LoginRequest { Email = "jack@example.com", Password = "Wrong123" }, AuthHarness.Client));
    }

    [Fact]
    public async Task Old_password_hash_is_upgraded_after_a_successful_login()
    {
        var user = _h.AddUser();
        user.PasswordHash = "old:Customer123";

        await _h.Service.LoginAsync(new LoginRequest { Email = user.Email, Password = "Customer123" }, AuthHarness.Client);

        Assert.Equal("hash:Customer123", user.PasswordHash);
    }

    [Fact]
    public async Task Login_deletes_refresh_tokens_that_expired_long_ago()
    {
        var user = _h.AddUser();
        var stale = _h.ActiveTokenFor(user, out _);
        stale.ExpiresAt = _h.Clock.Now.AddDays(-31);
        var recent = _h.ActiveTokenFor(user, out _);
        recent.ExpiresAt = _h.Clock.Now.AddDays(-2);

        await _h.Service.LoginAsync(new LoginRequest { Email = user.Email, Password = "Customer123" }, AuthHarness.Client);

        Assert.DoesNotContain(stale, _h.RefreshTokens);
        Assert.Contains(recent, _h.RefreshTokens);
    }

    // ---------------------------------------------------------------- lockout

    private async Task FailLoginAsync(string email, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await Assert.ThrowsAsync<UnauthorizedException>(() => _h.Service.LoginAsync(new LoginRequest { Email = email, Password = "Wrong123" }, AuthHarness.Client));
        }
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_password()
    {
        var user = _h.AddUser();
        await FailLoginAsync(user.Email, 5);

        var ex = await Assert.ThrowsAsync<TooManyRequestsException>(() => _h.Service.LoginAsync(new LoginRequest { Email = user.Email, Password = "Customer123" }, AuthHarness.Client));

        Assert.Equal("Too many sign-in attempts. Try again in 15 minutes.", ex.Message);
        Assert.Equal(TimeSpan.FromMinutes(15), ex.RetryAfter);
    }

    [Fact]
    public async Task The_lock_ends_after_15_minutes()
    {
        var user = _h.AddUser();
        await FailLoginAsync(user.Email, 5);
        _h.Clock.Advance(TimeSpan.FromMinutes(15));

        var session = await _h.Service.LoginAsync(new LoginRequest { Email = user.Email, Password = "Customer123" }, AuthHarness.Client);

        Assert.NotNull(session);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task The_retry_time_counts_down()
    {
        var user = _h.AddUser();
        await FailLoginAsync(user.Email, 5);
        _h.Clock.Advance(TimeSpan.FromMinutes(5));

        var ex = await Assert.ThrowsAsync<TooManyRequestsException>(() => _h.Service.LoginAsync(new LoginRequest { Email = user.Email, Password = "Customer123" }, AuthHarness.Client));

        Assert.Equal(TimeSpan.FromMinutes(10), ex.RetryAfter);
    }

    [Fact]
    public async Task A_good_login_resets_the_wrong_password_counter()
    {
        var user = _h.AddUser();
        await FailLoginAsync(user.Email, 3);
        Assert.Equal(3, user.FailedLoginCount);

        await _h.Service.LoginAsync(new LoginRequest { Email = user.Email, Password = "Customer123" }, AuthHarness.Client);

        Assert.Equal(0, user.FailedLoginCount);
        await FailLoginAsync(user.Email, 4);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task An_unknown_email_is_locked_exactly_like_a_real_account()
    {
        await FailLoginAsync("ghost@example.com", 5);

        var ex = await Assert.ThrowsAsync<TooManyRequestsException>(() => _h.Service.LoginAsync(new LoginRequest { Email = "ghost@example.com", Password = "Whatever123" }, AuthHarness.Client));

        Assert.Equal("Too many sign-in attempts. Try again in 15 minutes.", ex.Message);
        Assert.Equal(TimeSpan.FromMinutes(15), ex.RetryAfter);
    }

    // ---------------------------------------------------------------- refresh and logout

    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_stops_working()
    {
        var user = _h.AddUser();
        var old = _h.ActiveTokenFor(user, out var raw);

        var result = await _h.Service.RefreshAsync(raw, AuthHarness.Client);

        Assert.NotNull(result.Session);
        Assert.False(result.ClearCookie);
        Assert.NotEqual(raw, result.Session.RefreshToken);
        Assert.NotNull(old.RevokedAt);
        Assert.NotNull(old.ReplacedById);
        Assert.Equal(2, _h.RefreshTokens.Count);

        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        var again = await _h.Service.RefreshAsync(raw, AuthHarness.Client);
        Assert.Null(again.Session);
    }

    [Fact]
    public async Task Refresh_keeps_the_session_kind_and_gives_a_fresh_lifetime()
    {
        var user = _h.AddUser();
        _h.ActiveTokenFor(user, out var raw, remember: true);
        _h.Clock.Advance(TimeSpan.FromDays(10));

        var result = await _h.Service.RefreshAsync(raw, AuthHarness.Client);

        Assert.Equal(_h.Clock.Now.AddDays(30), result.Session!.RefreshExpiresAt);
        Assert.Contains(_h.RefreshTokens, t => t.Remember && t.RevokedAt is null);
    }

    [Fact]
    public async Task Refresh_without_a_token_is_rejected_and_nothing_is_cleared()
    {
        var result = await _h.Service.RefreshAsync(null, AuthHarness.Client);

        Assert.Null(result.Session);
        Assert.False(result.ClearCookie);
    }

    [Fact]
    public async Task Refresh_with_an_unknown_expired_or_deactivated_token_clears_the_cookie()
    {
        var unknown = await _h.Service.RefreshAsync("not-a-real-token", AuthHarness.Client);

        var user = _h.AddUser();
        var token = _h.ActiveTokenFor(user, out var expiredRaw);
        token.ExpiresAt = _h.Clock.Now.AddMinutes(-1);
        var expired = await _h.Service.RefreshAsync(expiredRaw, AuthHarness.Client);

        var inactive = _h.AddUser("jack@example.com", status: UserStatus.Inactive);
        _h.ActiveTokenFor(inactive, out var inactiveRaw);
        var deactivated = await _h.Service.RefreshAsync(inactiveRaw, AuthHarness.Client);

        Assert.All([unknown, expired, deactivated], r =>
        {
            Assert.Null(r.Session);
            Assert.True(r.ClearCookie);
        });
        Assert.All(_h.RefreshTokens.Where(t => t.UserId == inactive.Id), t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task Reusing_a_rotated_token_after_the_grace_period_revokes_every_session()
    {
        var user = _h.AddUser();
        _h.ActiveTokenFor(user, out var raw);
        _h.ActiveTokenFor(user, out _);
        await _h.Service.RefreshAsync(raw, AuthHarness.Client);

        _h.Clock.Advance(TimeSpan.FromSeconds(11));
        var replay = await _h.Service.RefreshAsync(raw, AuthHarness.Client);

        Assert.Null(replay.Session);
        Assert.True(replay.ClearCookie);
        Assert.All(_h.RefreshTokens, t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task Reusing_a_rotated_token_within_10_seconds_is_a_harmless_double_request()
    {
        var user = _h.AddUser();
        _h.ActiveTokenFor(user, out var raw);
        var first = await _h.Service.RefreshAsync(raw, AuthHarness.Client);

        _h.Clock.Advance(TimeSpan.FromSeconds(3));
        var second = await _h.Service.RefreshAsync(raw, AuthHarness.Client);

        Assert.Null(second.Session);
        Assert.False(second.ClearCookie);
        Assert.Contains(_h.RefreshTokens, t => t.RevokedAt is null);

        var newest = await _h.Service.RefreshAsync(first.Session!.RefreshToken, AuthHarness.Client);
        Assert.NotNull(newest.Session);
    }

    [Fact]
    public async Task A_token_ended_by_logout_does_not_revoke_other_sessions_when_replayed()
    {
        var user = _h.AddUser();
        _h.ActiveTokenFor(user, out var loggedOut);
        _h.ActiveTokenFor(user, out _);
        await _h.Service.LogoutAsync(loggedOut);

        var replay = await _h.Service.RefreshAsync(loggedOut, AuthHarness.Client);

        Assert.Null(replay.Session);
        Assert.True(replay.ClearCookie);
        Assert.Single(_h.RefreshTokens, t => t.RevokedAt is null);
    }

    [Fact]
    public async Task Logout_revokes_the_token_and_is_harmless_without_one()
    {
        var user = _h.AddUser();
        var token = _h.ActiveTokenFor(user, out var raw);

        await _h.Service.LogoutAsync(raw);
        await _h.Service.LogoutAsync(null);
        await _h.Service.LogoutAsync("unknown");

        Assert.NotNull(token.RevokedAt);
        Assert.Null(token.ReplacedById);
    }

    // ---------------------------------------------------------------- profile

    [Fact]
    public async Task Profile_requires_a_signed_in_user()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _h.Service.GetProfileAsync());
    }

    [Fact]
    public async Task A_deactivated_user_cannot_read_the_profile()
    {
        var user = _h.AddUser(status: UserStatus.Inactive);
        _h.CurrentUser.UserId = user.Id;

        await Assert.ThrowsAsync<UnauthorizedException>(() => _h.Service.GetProfileAsync());
    }

    [Fact]
    public async Task Profile_update_changes_only_the_fields_that_were_sent()
    {
        var user = _h.AddUser();
        user.Phone = "111";
        user.Country = "UK";
        _h.CurrentUser.UserId = user.Id;

        var dto = await _h.Service.UpdateProfileAsync(new UpdateProfileRequest { Name = " Emma W ", Phone = "" }, null);

        Assert.Equal("Emma W", dto.Name);
        Assert.Equal(string.Empty, dto.Phone);
        Assert.Equal("UK", dto.Country);
        Assert.Null(user.Phone);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one()
    {
        var user = _h.AddUser();
        _h.CurrentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _h.Service.UpdateProfileAsync(
            new UpdateProfileRequest { CurrentPassword = "Wrong123", NewPassword = "Brand1New" }, null));

        Assert.Equal("Current password is incorrect", ex.Fields["currentPassword"]);
        Assert.Equal("hash:Customer123", user.PasswordHash);
    }

    [Fact]
    public async Task A_weak_new_password_is_rejected()
    {
        var user = _h.AddUser();
        _h.CurrentUser.UserId = user.Id;

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _h.Service.UpdateProfileAsync(
            new UpdateProfileRequest { CurrentPassword = "Customer123", NewPassword = "weak" }, null));

        Assert.Equal("Use at least 8 characters with letters and numbers", ex.Fields["newPassword"]);
    }

    [Fact]
    public async Task After_a_password_change_other_sessions_end_and_the_current_one_stays()
    {
        var user = _h.AddUser();
        var current = _h.ActiveTokenFor(user, out var currentRaw);
        var other = _h.ActiveTokenFor(user, out _);
        _h.CurrentUser.UserId = user.Id;

        await _h.Service.UpdateProfileAsync(new UpdateProfileRequest { CurrentPassword = "Customer123", NewPassword = "Brand1New" }, currentRaw);

        Assert.Equal("hash:Brand1New", user.PasswordHash);
        Assert.Null(current.RevokedAt);
        Assert.NotNull(other.RevokedAt);
    }

    // ---------------------------------------------------------------- forgot and reset

    [Fact]
    public async Task Forgot_for_a_known_email_stores_a_hashed_token_and_queues_the_reset_email()
    {
        var user = _h.AddUser();

        await _h.Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = " EMMA@example.com " });

        var token = Assert.Single(_h.ResetTokens);
        Assert.Equal(user.Id, token.UserId);
        Assert.Equal(_h.Clock.Now.AddHours(1), token.ExpiresAt);
        var email = Assert.Single(_h.QueuedEmails);
        Assert.Equal(EmailKind.PasswordReset, email.Kind);
        Assert.Contains("https://ceylonroads.example/reset-password?token=", email.Body, StringComparison.Ordinal);

        var raw = email.Body.Split("token=")[1].Split('\n')[0].Trim();
        Assert.Equal(_h.Tokens.Hash(raw), token.TokenHash);
        Assert.NotEqual(raw, token.TokenHash);
    }

    [Fact]
    public async Task Forgot_for_an_unknown_email_creates_nothing_and_does_not_fail()
    {
        await _h.Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "ghost@example.com" });

        Assert.Empty(_h.ResetTokens);
        Assert.Empty(_h.QueuedEmails);
    }

    [Fact]
    public async Task Forgot_with_an_invalid_email_is_a_validation_error()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _h.Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "nope" }));

        Assert.Equal("Enter a valid email address", ex.Fields["email"]);
    }

    [Fact]
    public async Task A_second_forgot_request_invalidates_the_first_link()
    {
        _h.AddUser();
        await _h.Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "emma@example.com" });
        var firstRaw = _h.QueuedEmails[0].Body.Split("token=")[1].Split('\n')[0].Trim();
        await _h.Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "emma@example.com" });
        var secondRaw = _h.QueuedEmails[1].Body.Split("token=")[1].Split('\n')[0].Trim();

        await Assert.ThrowsAsync<ValidationException>(() => _h.Service.ResetPasswordAsync(new ResetPasswordRequest { Token = firstRaw, NewPassword = "Brand1New" }));
        await _h.Service.ResetPasswordAsync(new ResetPasswordRequest { Token = secondRaw, NewPassword = "Brand1New" });
    }

    private async Task<string> RequestResetAsync(string email = "emma@example.com")
    {
        await _h.Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = email });
        return _h.QueuedEmails[^1].Body.Split("token=")[1].Split('\n')[0].Trim();
    }

    [Fact]
    public async Task Reset_sets_the_password_once_clears_the_lock_and_ends_every_session()
    {
        var user = _h.AddUser();
        await FailLoginAsync(user.Email, 5);
        _h.ActiveTokenFor(user, out _);
        var raw = await RequestResetAsync();

        await _h.Service.ResetPasswordAsync(new ResetPasswordRequest { Token = raw, NewPassword = "Brand1New" });

        Assert.Equal("hash:Brand1New", user.PasswordHash);
        Assert.Null(user.LockedUntil);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.All(_h.RefreshTokens, t => Assert.NotNull(t.RevokedAt));
        Assert.NotNull(Assert.Single(_h.ResetTokens).UsedAt);

        var session = await _h.Service.LoginAsync(new LoginRequest { Email = user.Email, Password = "Brand1New" }, AuthHarness.Client);
        Assert.NotNull(session);

        var again = await Assert.ThrowsAsync<ValidationException>(() => _h.Service.ResetPasswordAsync(new ResetPasswordRequest { Token = raw, NewPassword = "Another1Pass" }));
        Assert.Equal("This reset link is invalid or has expired", again.Fields["token"]);
    }

    [Fact]
    public async Task Reset_rejects_unknown_and_expired_tokens_with_the_same_message()
    {
        _h.AddUser();
        var raw = await RequestResetAsync();
        _h.Clock.Advance(TimeSpan.FromMinutes(61));

        var expired = await Assert.ThrowsAsync<ValidationException>(() => _h.Service.ResetPasswordAsync(new ResetPasswordRequest { Token = raw, NewPassword = "Brand1New" }));
        var unknown = await Assert.ThrowsAsync<ValidationException>(() => _h.Service.ResetPasswordAsync(new ResetPasswordRequest { Token = "nope", NewPassword = "Brand1New" }));

        Assert.Equal("This reset link is invalid or has expired", expired.Fields["token"]);
        Assert.Equal(expired.Fields["token"], unknown.Fields["token"]);
    }

    [Fact]
    public async Task A_weak_new_password_keeps_the_reset_token_usable()
    {
        var user = _h.AddUser();
        var raw = await RequestResetAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _h.Service.ResetPasswordAsync(new ResetPasswordRequest { Token = raw, NewPassword = "weak" }));

        Assert.True(ex.Fields.ContainsKey("newPassword"));
        Assert.Null(Assert.Single(_h.ResetTokens).UsedAt);
        Assert.Equal("hash:Customer123", user.PasswordHash);
    }
}
