using FluentValidation;
using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Application.Email;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;
using ValidationException = TravelWebAPI.Domain.Exceptions.ValidationException;

namespace TravelWebAPI.Application.Auth;

/// <summary>Register, sign in, sessions, profile and password recovery.</summary>
public sealed class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordResetTokenRepository resetTokens,
    IRepository<SiteSettings> siteSettings,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwt,
    ITokenGenerator tokens,
    ILoginAttemptTracker attempts,
    IEmailOutbox emails,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<AuthOptions> options,
    IValidator<RegisterRequest> registerValidator,
    IValidator<UpdateProfileRequest> profileValidator,
    IValidator<ForgotPasswordRequest> forgotValidator,
    IValidator<ResetPasswordRequest> resetValidator)
{
    public const string BadCredentialsMessage = "Email or password is incorrect";
    public const string DeactivatedMessage = "This account has been deactivated";
    public const string DuplicateEmailMessage = "That email is already registered";

    private static string? dummyHash;

    private AuthOptions Settings => options.Value;

    // ---------------------------------------------------------------- register and sign in

    public async Task<AuthSession> RegisterAsync(RegisterRequest request, ClientInfo client, CancellationToken ct = default)
    {
        registerValidator.ThrowIfInvalid(request);

        var email = Rules.NormalizeEmail(request.Email);
        if (await users.GetByEmailAsync(email, ct) is not null)
        {
            throw new ConflictException(DuplicateEmailMessage, new Dictionary<string, string> { ["email"] = DuplicateEmailMessage });
        }

        var now = clock.UtcNow;
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name!.Trim(),
            Email = email,
            PasswordHash = passwordHasher.Hash(request.Password!),
            Role = UserRole.Customer,
            Status = UserStatus.Active,
            Phone = Blank(request.Phone),
            Country = Blank(request.Country),
            LastLoginAt = now,
        };
        await users.AddAsync(user, ct);

        var (session, _) = await StartSessionAsync(user, remember: true, client, ct);

        var settings = await siteSettings.GetByIdAsync(SiteSettings.SingletonId, ct);
        var emailId = await emails.QueueAsync(
            AccountEmails.Welcome(email, user.Name, settings?.Name ?? "Ceylon Roads", settings?.DriverName ?? string.Empty),
            ct);

        await unitOfWork.SaveChangesAsync(ct);
        await emails.DispatchAsync([emailId], ct);

        return session;
    }

    public async Task<AuthSession> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct = default)
    {
        var email = Rules.NormalizeEmail(request.Email);
        var now = clock.UtcNow;
        var user = await users.GetByEmailAsync(email, ct);

        if (user is null)
        {
            ThrowIfLocked(attempts.GetRetryAfter(email));

            // Same work as a real check, so the response time does not reveal whether the account exists.
            dummyHash ??= passwordHasher.Hash("not-a-real-password-1");
            passwordHasher.Verify(request.Password ?? string.Empty, dummyHash);

            attempts.RecordFailure(email);
            throw new UnauthorizedException(BadCredentialsMessage);
        }

        if (user.LockedUntil is { } lockedUntil)
        {
            if (lockedUntil > now)
            {
                throw LockedOut(lockedUntil - now);
            }

            user.LockedUntil = null;
        }

        if (string.IsNullOrEmpty(request.Password) || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            RegisterFailedAttempt(user, now);
            await unitOfWork.SaveChangesAsync(ct);
            throw new UnauthorizedException(BadCredentialsMessage);
        }

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenException(DeactivatedMessage);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastLoginAt = now;
        if (passwordHasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        attempts.Reset(email);
        await refreshTokens.DeleteExpiredBeforeAsync(now.AddDays(-Settings.CleanupAfterExpiryDays), ct);

        var (session, _) = await StartSessionAsync(user, request.Remember == true, client, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return session;
    }

    // ---------------------------------------------------------------- sessions

    public async Task<RefreshResult> RefreshAsync(string? refreshToken, ClientInfo client, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return new RefreshResult(null, ClearCookie: false);
        }

        var now = clock.UtcNow;
        var token = await refreshTokens.GetByHashAsync(tokens.Hash(refreshToken), ct);
        if (token is null)
        {
            return new RefreshResult(null, ClearCookie: true);
        }

        if (token.RevokedAt is { } revokedAt)
        {
            // Replaced by a rotation: using it again is either two tabs at once or a stolen copy.
            if (token.ReplacedById is not null)
            {
                if (now - revokedAt < TimeSpan.FromSeconds(Settings.RefreshGraceSeconds))
                {
                    return new RefreshResult(null, ClearCookie: false);
                }

                await RevokeAllAsync(token.UserId, now, except: null, ct);
                await unitOfWork.SaveChangesAsync(ct);
            }

            return new RefreshResult(null, ClearCookie: true);
        }

        if (token.ExpiresAt <= now)
        {
            return new RefreshResult(null, ClearCookie: true);
        }

        var user = await users.GetByIdAsync(token.UserId, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            await RevokeAllAsync(token.UserId, now, except: null, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return new RefreshResult(null, ClearCookie: true);
        }

        var (session, newTokenId) = await StartSessionAsync(user, token.Remember, client, ct);
        token.RevokedAt = now;
        token.ReplacedById = newTokenId;
        await unitOfWork.SaveChangesAsync(ct);

        return new RefreshResult(session, ClearCookie: false);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var token = await refreshTokens.GetByHashAsync(tokens.Hash(refreshToken), ct);
        if (token is { RevokedAt: null })
        {
            token.RevokedAt = clock.UtcNow;
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    // ---------------------------------------------------------------- profile

    public async Task<UserDto> GetProfileAsync(CancellationToken ct = default)
        => UserDto.From(await LoadCurrentUserAsync(ct));

    public async Task<UserDto> UpdateProfileAsync(UpdateProfileRequest request, string? currentRefreshToken, CancellationToken ct = default)
    {
        profileValidator.ThrowIfInvalid(request);
        var user = await LoadCurrentUserAsync(ct);
        var now = clock.UtcNow;

        if (request.NewPassword is not null
            && !(request.CurrentPassword is not null && passwordHasher.Verify(request.CurrentPassword, user.PasswordHash)))
        {
            throw new ValidationException("currentPassword", Rules.CurrentPasswordMessage);
        }

        if (request.Name is not null)
        {
            user.Name = request.Name.Trim();
        }

        if (request.Phone is not null)
        {
            user.Phone = Blank(request.Phone);
        }

        if (request.Country is not null)
        {
            user.Country = Blank(request.Country);
        }

        if (request.NewPassword is not null)
        {
            user.PasswordHash = passwordHasher.Hash(request.NewPassword);

            // Other devices must sign in again; this session carries on.
            var keep = string.IsNullOrWhiteSpace(currentRefreshToken) ? null : tokens.Hash(currentRefreshToken);
            await RevokeAllAsync(user.Id, now, except: keep, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    // ---------------------------------------------------------------- password recovery

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        forgotValidator.ThrowIfInvalid(request);

        // The caller always gets the same answer; nothing below is visible to them.
        var user = await users.GetByEmailAsync(Rules.NormalizeEmail(request.Email), ct);
        if (user is null)
        {
            return;
        }

        var now = clock.UtcNow;
        foreach (var older in await resetTokens.GetUnusedForUserAsync(user.Id, ct))
        {
            older.UsedAt = now;
        }

        var generated = tokens.Generate();
        await resetTokens.AddAsync(
            new PasswordResetToken
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                TokenHash = generated.Hash,
                ExpiresAt = now.AddMinutes(Settings.ResetTokenMinutes),
            },
            ct);

        var emailId = await emails.QueueAsync(
            AccountEmails.PasswordReset(user.Email, user.Name, Settings.WebBaseUrl, generated.Token, Settings.ResetTokenMinutes),
            ct);

        await unitOfWork.SaveChangesAsync(ct);
        await emails.DispatchAsync([emailId], ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        resetValidator.ThrowIfInvalid(request);

        var now = clock.UtcNow;
        var token = await resetTokens.GetByHashAsync(tokens.Hash(request.Token!), ct);
        if (token is null || token.UsedAt is not null || token.ExpiresAt <= now)
        {
            throw new ValidationException("token", Rules.ResetTokenMessage);
        }

        var user = await users.GetByIdAsync(token.UserId, ct)
            ?? throw new ValidationException("token", Rules.ResetTokenMessage);

        user.PasswordHash = passwordHasher.Hash(request.NewPassword!);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        token.UsedAt = now;
        await RevokeAllAsync(user.Id, now, except: null, ct);

        await unitOfWork.SaveChangesAsync(ct);
        attempts.Reset(user.Email);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Adds a new refresh token and creates the access token. Nothing is saved here.</summary>
    private async Task<(AuthSession Session, Guid TokenId)> StartSessionAsync(User user, bool remember, ClientInfo client, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var generated = tokens.Generate();
        var expires = now.AddDays(remember ? Settings.RefreshDaysRemember : Settings.RefreshDays);

        var entity = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = generated.Hash,
            ExpiresAt = expires,
            Remember = remember,
            CreatedByIp = Truncate(client.IpAddress, 45),
            UserAgent = Truncate(client.UserAgent, 255),
        };
        await refreshTokens.AddAsync(entity, ct);

        var response = new AuthResponse(jwt.CreateAccessToken(user), UserDto.From(user));
        return (new AuthSession(response, generated.Token, expires), entity.Id);
    }

    private void RegisterFailedAttempt(User user, DateTimeOffset now)
    {
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= Settings.MaxFailedAttempts)
        {
            user.LockedUntil = now.AddMinutes(Settings.LockoutMinutes);
            user.FailedLoginCount = 0;
        }
    }

    private void ThrowIfLocked(TimeSpan? retryAfter)
    {
        if (retryAfter is { } wait)
        {
            throw LockedOut(wait);
        }
    }

    private TooManyRequestsException LockedOut(TimeSpan retryAfter)
        => new($"Too many sign-in attempts. Try again in {Settings.LockoutMinutes} minutes.", retryAfter);

    private async Task RevokeAllAsync(Guid userId, DateTimeOffset now, string? except, CancellationToken ct)
    {
        foreach (var token in await refreshTokens.GetActiveForUserAsync(userId, now, ct))
        {
            if (except is null || token.TokenHash != except)
            {
                token.RevokedAt = now;
            }
        }
    }

    private async Task<User> LoadCurrentUserAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } id)
        {
            throw new UnauthorizedException();
        }

        var user = await users.GetByIdAsync(id, ct);
        if (user is null || user.Status != UserStatus.Active)
        {
            throw new UnauthorizedException();
        }

        return user;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
