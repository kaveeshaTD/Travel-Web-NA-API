using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TravelWebAPI.Application.Auth;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Tests.Unit;

public class TokenTests
{
    private const string Key = "0123456789-0123456789-0123456789-0123";

    private static readonly User Admin = new()
    {
        Id = Guid.Parse("0197a1b2-1111-7222-8333-444455556666"),
        Name = "Nuwan Dissanayake",
        Email = "admin@ceylonroads.example",
        Role = UserRole.Admin,
    };

    private static JwtTokenService Service(FakeClock clock, string key = Key)
        => new(Options.Create(new JwtSettings { Key = key }), clock);

    private static TokenValidationParameters Parameters(string key = Key) => new()
    {
        ValidIssuer = "TravelWebAPI",
        ValidAudience = "TravelWebAPI",
        IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(key)),
        ClockSkew = TimeSpan.Zero,
    };

    [Fact]
    public async Task Access_token_carries_sub_email_name_and_role_and_expires_in_15_minutes()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var token = Service(clock).CreateAccessToken(Admin);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters());

        Assert.True(result.IsValid);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal(Admin.Id.ToString(), jwt.GetClaim("sub").Value);
        Assert.Equal("admin@ceylonroads.example", jwt.GetClaim("email").Value);
        Assert.Equal("Nuwan Dissanayake", jwt.GetClaim("name").Value);
        Assert.Equal("admin", jwt.GetClaim("role").Value);
        Assert.False(string.IsNullOrEmpty(jwt.Id));
        Assert.Equal(TimeSpan.FromMinutes(15), jwt.ValidTo - jwt.ValidFrom);
        Assert.Equal("HS256", jwt.Alg);
    }

    [Fact]
    public async Task A_token_is_rejected_after_it_expires()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow.AddHours(-1));
        var token = Service(clock).CreateAccessToken(Admin);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters());

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_rejected()
    {
        var token = Service(new FakeClock(DateTimeOffset.UtcNow), "another-key-another-key-another-key-0000").CreateAccessToken(Admin);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, Parameters());

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task A_token_with_an_edited_role_is_rejected()
    {
        var token = Service(new FakeClock(DateTimeOffset.UtcNow)).CreateAccessToken(new User { Id = Admin.Id, Name = "Emma", Email = "e@e.co", Role = UserRole.Customer });
        var parts = token.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1])).Replace("\"customer\"", "\"admin\"", StringComparison.Ordinal);
        var forged = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(forged, Parameters());

        Assert.False(result.IsValid);
    }

    [Fact]
    public void A_short_signing_key_is_refused()
        => Assert.Throws<InvalidOperationException>(() => Service(new FakeClock(DateTimeOffset.UtcNow), "too-short").CreateAccessToken(Admin));

    [Fact]
    public void Random_tokens_are_unique_url_safe_and_hash_to_a_stable_sha256_hex()
    {
        var generator = new TokenGenerator();

        var a = generator.Generate();
        var b = generator.Generate();

        Assert.NotEqual(a.Token, b.Token);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a.Token);
        Assert.Matches("^[0-9a-f]{64}$", a.Hash);
        Assert.Equal(a.Hash, generator.Hash(a.Token));
        Assert.NotEqual(a.Token, a.Hash);
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", generator.Hash("hello"));
    }
}

public class LoginAttemptTrackerTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));

    private MemoryLoginAttemptTracker Tracker() => new(_clock, Options.Create(new AuthOptions()));

    [Fact]
    public void Five_failures_lock_the_email_for_15_minutes()
    {
        var tracker = Tracker();

        for (var i = 0; i < 4; i++)
        {
            tracker.RecordFailure("ghost@example.com");
            Assert.Null(tracker.GetRetryAfter("ghost@example.com"));
        }

        tracker.RecordFailure("ghost@example.com");

        Assert.Equal(TimeSpan.FromMinutes(15), tracker.GetRetryAfter("ghost@example.com"));
        Assert.Null(tracker.GetRetryAfter("other@example.com"));
    }

    [Fact]
    public void The_lock_expires_and_counting_starts_again()
    {
        var tracker = Tracker();
        for (var i = 0; i < 5; i++)
        {
            tracker.RecordFailure("ghost@example.com");
        }

        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Null(tracker.GetRetryAfter("ghost@example.com"));

        tracker.RecordFailure("ghost@example.com");
        Assert.Null(tracker.GetRetryAfter("ghost@example.com"));
    }

    [Fact]
    public void Failures_spread_over_more_than_15_minutes_do_not_add_up()
    {
        var tracker = Tracker();
        for (var i = 0; i < 4; i++)
        {
            tracker.RecordFailure("ghost@example.com");
        }

        _clock.Advance(TimeSpan.FromMinutes(16));
        tracker.RecordFailure("ghost@example.com");

        Assert.Null(tracker.GetRetryAfter("ghost@example.com"));
    }

    [Fact]
    public void Reset_clears_the_count()
    {
        var tracker = Tracker();
        for (var i = 0; i < 5; i++)
        {
            tracker.RecordFailure("ghost@example.com");
        }

        tracker.Reset("ghost@example.com");

        Assert.Null(tracker.GetRetryAfter("ghost@example.com"));
    }
}
