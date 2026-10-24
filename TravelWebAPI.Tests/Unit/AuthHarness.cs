using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Tests.Unit;

public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset Now { get; set; } = start;

    public DateTimeOffset UtcNow => Now;

    public DateOnly TodayInSriLanka => DateOnly.FromDateTime(Now.UtcDateTime);

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>In-memory stand-ins for the repositories, so the auth rules can be tested without a database.</summary>
public sealed class AuthHarness
{
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));
    public List<User> Users { get; } = [];
    public List<RefreshToken> RefreshTokens { get; } = [];
    public List<PasswordResetToken> ResetTokens { get; } = [];
    public List<EmailMessage> QueuedEmails { get; } = [];
    public List<Guid> DispatchedEmails { get; } = [];
    public FakeCurrentUser CurrentUser { get; } = new();
    public FakeHasher Hasher { get; } = new();
    public TokenGenerator Tokens { get; } = new();
    public int Saves { get; private set; }
    public AuthOptions Options { get; } = new() { WebBaseUrl = "https://ceylonroads.example/" };
    public MemoryLoginAttemptTracker Tracker { get; }
    public AuthService Service { get; }

    public static readonly ClientInfo Client = new("203.0.113.5", "unit-test");

    public AuthHarness()
    {
        var options = Microsoft.Extensions.Options.Options.Create(Options);
        Tracker = new MemoryLoginAttemptTracker(Clock, options);
        Service = new AuthService(
            new FakeUserRepository(this),
            new FakeRefreshTokenRepository(this),
            new FakeResetTokenRepository(this),
            new FakeSettingsRepository(),
            new FakeUnitOfWork(this),
            Hasher,
            new FakeJwt(),
            Tokens,
            Tracker,
            new FakeOutbox(this),
            CurrentUser,
            Clock,
            options,
            new RegisterRequestValidator(),
            new UpdateProfileRequestValidator(),
            new ForgotPasswordRequestValidator(),
            new ResetPasswordRequestValidator());
    }

    public User AddUser(string email = "emma@example.com", string password = "Customer123", UserStatus status = UserStatus.Active, UserRole role = UserRole.Customer)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = "Emma Wilson",
            Email = email,
            PasswordHash = Hasher.Hash(password),
            Status = status,
            Role = role,
            CreatedAt = Clock.Now,
        };
        Users.Add(user);
        return user;
    }

    public RefreshToken ActiveTokenFor(User user, out string rawToken, bool remember = false)
    {
        var generated = Tokens.Generate();
        rawToken = generated.Token;
        var token = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = generated.Hash,
            ExpiresAt = Clock.Now.AddDays(remember ? 30 : 1),
            Remember = remember,
        };
        RefreshTokens.Add(token);
        return token;
    }

    public void Save() => Saves++;

    private sealed class FakeUserRepository(AuthHarness h) : IUserRepository
    {
        public Task<User?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            => Task.FromResult(h.Users.FirstOrDefault(u => u.Id.Equals(id)));

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
            => Task.FromResult(h.Users.FirstOrDefault(u => u.Email == normalizedEmail));

        public Task AddAsync(User entity, CancellationToken cancellationToken = default)
        {
            h.Users.Add(entity);
            return Task.CompletedTask;
        }

        public void Remove(User entity) => h.Users.Remove(entity);
    }

    private sealed class FakeRefreshTokenRepository(AuthHarness h) : IRefreshTokenRepository
    {
        public Task<RefreshToken?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            => Task.FromResult(h.RefreshTokens.FirstOrDefault(t => t.Id.Equals(id)));

        public Task AddAsync(RefreshToken entity, CancellationToken cancellationToken = default)
        {
            h.RefreshTokens.Add(entity);
            return Task.CompletedTask;
        }

        public void Remove(RefreshToken entity) => h.RefreshTokens.Remove(entity);

        public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
            => Task.FromResult(h.RefreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash));

        public Task<IReadOnlyList<RefreshToken>> GetActiveForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RefreshToken>>(
                h.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt is null && t.ExpiresAt > now).ToList());

        public Task<int> DeleteExpiredBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
            => Task.FromResult(h.RefreshTokens.RemoveAll(t => t.ExpiresAt < cutoff));
    }

    private sealed class FakeResetTokenRepository(AuthHarness h) : IPasswordResetTokenRepository
    {
        public Task<PasswordResetToken?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            => Task.FromResult(h.ResetTokens.FirstOrDefault(t => t.Id.Equals(id)));

        public Task AddAsync(PasswordResetToken entity, CancellationToken cancellationToken = default)
        {
            h.ResetTokens.Add(entity);
            return Task.CompletedTask;
        }

        public void Remove(PasswordResetToken entity) => h.ResetTokens.Remove(entity);

        public Task<PasswordResetToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
            => Task.FromResult(h.ResetTokens.FirstOrDefault(t => t.TokenHash == tokenHash));

        public Task<IReadOnlyList<PasswordResetToken>> GetUnusedForUserAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PasswordResetToken>>(h.ResetTokens.Where(t => t.UserId == userId && t.UsedAt is null).ToList());
    }

    private sealed class FakeSettingsRepository : IRepository<SiteSettings>
    {
        private readonly SiteSettings _settings = new() { Name = "Ceylon Roads", DriverName = "Nuwan Dissanayake", Email = "hello@ceylonroads.example" };

        public Task<SiteSettings?> GetByIdAsync(object id, CancellationToken cancellationToken = default)
            => Task.FromResult<SiteSettings?>(_settings);

        public Task AddAsync(SiteSettings entity, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(SiteSettings entity)
        {
        }
    }

    private sealed class FakeUnitOfWork(AuthHarness h) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            h.Save();
            return Task.FromResult(0);
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => operation(cancellationToken);
    }

    private sealed class FakeJwt : IJwtTokenService
    {
        public string CreateAccessToken(User user) => $"access-token-for-{user.Email}";
    }

    private sealed class FakeOutbox(AuthHarness h) : IEmailOutbox
    {
        public Task<Guid> QueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            h.QueuedEmails.Add(message);
            return Task.FromResult(Guid.CreateVersion7());
        }

        public Task DispatchAsync(IEnumerable<Guid> emailLogIds, CancellationToken cancellationToken = default)
        {
            h.DispatchedEmails.AddRange(emailLogIds);
            return Task.CompletedTask;
        }
    }
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public bool IsAuthenticated => UserId is not null;

    public string? Email => null;

    public UserRole? Role => null;

    public bool IsAdmin => false;
}

/// <summary>Readable fake hashes: <c>hash:password</c> is current, <c>old:password</c> needs a rehash.</summary>
public sealed class FakeHasher : IPasswordHasher
{
    public string Hash(string password) => $"hash:{password}";

    public bool Verify(string password, string hash) => hash == $"hash:{password}" || hash == $"old:{password}";

    public bool NeedsRehash(string hash) => hash.StartsWith("old:", StringComparison.Ordinal);
}
