using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;
using TravelWebAPI.Infrastructure.Services;
using TravelWebAPI.Tests.Unit;

namespace TravelWebAPI.Tests.Integration;

[Collection(PostgresCollection.Name)]
public class AuthInfrastructureTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeSender(Func<EmailMessage, EmailSendResult> respond) : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(respond(message));
        }
    }

    private sealed class ThrowingSender : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("provider exploded");
    }

    private static EmailOutbox Outbox(AppDbContext db, IEmailSender sender, string fromEmail = "")
        => new(db, sender, new FakeClock(Now), Options.Create(new EmailOutboxOptions { FromEmail = fromEmail }), NullLogger<EmailOutbox>.Instance);

    private static EmailMessage Welcome() => new(EmailKind.Welcome, "emma@example.com", "Welcome", "Hello");

    // ---------------------------------------------------------------- repositories

    [Fact]
    public async Task User_repository_finds_by_normalised_email_and_by_id()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var repository = new UserRepository(db);
        var user = TestData.User("emma@example.com");
        await repository.AddAsync(user);
        await db.SaveChangesAsync();

        Assert.Equal(user.Id, (await repository.GetByEmailAsync("emma@example.com"))!.Id);
        Assert.Null(await repository.GetByEmailAsync("ghost@example.com"));
        Assert.Equal("emma@example.com", (await repository.GetByIdAsync(user.Id))!.Email);
    }

    [Fact]
    public async Task Refresh_token_repository_finds_by_hash_and_lists_only_active_tokens()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var user = TestData.User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.RefreshTokens.AddRange(
            new RefreshToken { UserId = user.Id, TokenHash = "active", ExpiresAt = Now.AddDays(1) },
            new RefreshToken { UserId = user.Id, TokenHash = "revoked", ExpiresAt = Now.AddDays(1), RevokedAt = Now },
            new RefreshToken { UserId = user.Id, TokenHash = "expired", ExpiresAt = Now.AddDays(-1) });
        await db.SaveChangesAsync();
        var repository = new RefreshTokenRepository(db);

        Assert.NotNull(await repository.GetByHashAsync("revoked"));
        Assert.Null(await repository.GetByHashAsync("missing"));
        var active = await repository.GetActiveForUserAsync(user.Id, Now);
        Assert.Equal("active", Assert.Single(active).TokenHash);
    }

    [Fact]
    public async Task Cleanup_deletes_only_tokens_that_expired_before_the_cutoff()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var user = TestData.User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.RefreshTokens.AddRange(
            new RefreshToken { UserId = user.Id, TokenHash = "old", ExpiresAt = Now.AddDays(-31) },
            new RefreshToken { UserId = user.Id, TokenHash = "recent", ExpiresAt = Now.AddDays(-5) },
            new RefreshToken { UserId = user.Id, TokenHash = "valid", ExpiresAt = Now.AddDays(1) });
        await db.SaveChangesAsync();

        var deleted = await new RefreshTokenRepository(db).DeleteExpiredBeforeAsync(Now.AddDays(-30));

        Assert.Equal(1, deleted);
        var left = await db.RefreshTokens.Select(t => t.TokenHash).OrderBy(h => h).ToListAsync();
        Assert.Equal(["recent", "valid"], left);
    }

    [Fact]
    public async Task Reset_token_repository_finds_by_hash_and_lists_unused_tokens()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var user = TestData.User();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.PasswordResetTokens.AddRange(
            new PasswordResetToken { UserId = user.Id, TokenHash = "fresh", ExpiresAt = Now.AddHours(1) },
            new PasswordResetToken { UserId = user.Id, TokenHash = "used", ExpiresAt = Now.AddHours(1), UsedAt = Now });
        await db.SaveChangesAsync();
        var repository = new PasswordResetTokenRepository(db);

        Assert.NotNull(await repository.GetByHashAsync("used"));
        Assert.Equal("fresh", Assert.Single(await repository.GetUnusedForUserAsync(user.Id)).TokenHash);
    }

    // ---------------------------------------------------------------- email outbox

    [Fact]
    public async Task Queued_email_is_saved_with_the_business_change_and_starts_queued()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var outbox = Outbox(db, new FakeSender(_ => new EmailSendResult(EmailSendStatus.Sent)), fromEmail: "owner@ceylonroads.example");

        var id = await outbox.QueueAsync(Welcome());
        Assert.Equal(0, await PostgresFixture.ScalarAsync<long>(connection, "SELECT count(*) FROM email_logs"));
        await db.SaveChangesAsync();

        var log = await db.EmailLogs.SingleAsync(e => e.Id == id);
        Assert.Equal(EmailDeliveryStatus.Queued, log.DeliveryStatus);
        Assert.Equal("owner@ceylonroads.example", log.FromAddress);
        Assert.Equal("emma@example.com", log.ToAddress);
        Assert.Equal(EmailKind.Welcome, log.Kind);
    }

    [Fact]
    public async Task Sender_address_falls_back_to_the_website_contact_email()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        db.SiteSettings.Add(new SiteSettings { Name = "Ceylon Roads", Email = "hello@ceylonroads.example" });
        await db.SaveChangesAsync();

        var id = await Outbox(db, new FakeSender(_ => new EmailSendResult(EmailSendStatus.Sent))).QueueAsync(Welcome());
        await db.SaveChangesAsync();

        Assert.Equal("hello@ceylonroads.example", (await db.EmailLogs.SingleAsync(e => e.Id == id)).FromAddress);
    }

    [Fact]
    public async Task A_sent_email_records_the_time_and_the_provider_id()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var sender = new FakeSender(_ => new EmailSendResult(EmailSendStatus.Sent, ProviderMessageId: "sg-123"));
        var outbox = Outbox(db, sender);
        var id = await outbox.QueueAsync(Welcome());
        await db.SaveChangesAsync();

        await outbox.DispatchAsync([id]);

        var log = await db.EmailLogs.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(EmailDeliveryStatus.Sent, log.DeliveryStatus);
        Assert.Equal(Now, log.SentAt);
        Assert.Equal("sg-123", log.ProviderMessageId);
        Assert.Null(log.Error);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task With_no_provider_the_email_is_marked_failed_with_the_reason_and_never_retried()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var outbox = Outbox(db, new LogOnlyEmailSender(NullLogger<LogOnlyEmailSender>.Instance, Options.Create(new EmailOutboxOptions())));
        var id = await outbox.QueueAsync(Welcome());
        await db.SaveChangesAsync();

        await outbox.DispatchAsync([id]);
        await outbox.DispatchAsync([id]);

        var log = await db.EmailLogs.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(EmailDeliveryStatus.Failed, log.DeliveryStatus);
        Assert.Equal("No email provider configured", log.Error);
        Assert.Null(log.SentAt);
        Assert.Equal("failed", await PostgresFixture.ScalarAsync<string>(connection, "SELECT delivery_status FROM email_logs"));
    }

    [Fact]
    public async Task The_saved_copy_of_a_secret_email_has_no_secret_but_the_provider_gets_the_real_text()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var sender = new FakeSender(_ => new EmailSendResult(EmailSendStatus.Sent));
        var outbox = Outbox(db, sender);
        var message = TravelWebAPI.Application.Email.AccountEmails.PasswordReset("emma@example.com", "Emma", "https://x.example", "super-secret-token", 60);
        var id = await outbox.QueueAsync(message);
        await db.SaveChangesAsync();

        await outbox.DispatchAsync([id]);

        var stored = await PostgresFixture.ScalarAsync<string>(connection, "SELECT body FROM email_logs");
        Assert.DoesNotContain("super-secret-token", stored, StringComparison.Ordinal);
        Assert.Contains("token=[REDACTED]", stored, StringComparison.Ordinal);
        Assert.Contains("super-secret-token", Assert.Single(sender.Sent).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_provider_the_secret_is_not_in_the_database_and_not_in_the_log_unless_body_logging_is_on()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var logs = new Support.CapturingLogSink();
        var quiet = new LogOnlyEmailSender(LoggerFor(logs), Options.Create(new EmailOutboxOptions { LogBodies = false }));
        var verbose = new LogOnlyEmailSender(LoggerFor(logs), Options.Create(new EmailOutboxOptions { LogBodies = true }));
        var message = TravelWebAPI.Application.Email.AccountEmails.PasswordReset("emma@example.com", "Emma", "https://x.example", "super-secret-token", 60);

        var outbox = Outbox(db, quiet);
        var id = await outbox.QueueAsync(message);
        await db.SaveChangesAsync();
        await outbox.DispatchAsync([id]);

        Assert.DoesNotContain("super-secret-token", await PostgresFixture.ScalarAsync<string>(connection, "SELECT body FROM email_logs"), StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("super-secret-token", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Message.Contains("emma@example.com", StringComparison.Ordinal));

        await verbose.SendAsync(message);
        Assert.Contains(logs.Entries, e => e.Message.Contains("super-secret-token", StringComparison.Ordinal));
    }

    private static Microsoft.Extensions.Logging.ILogger<LogOnlyEmailSender> LoggerFor(Support.CapturingLogSink sink)
    {
        var serilog = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        return Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<LogOnlyEmailSender>(new Serilog.Extensions.Logging.SerilogLoggerFactory(serilog));
    }

    [Fact]
    public async Task A_provider_error_marks_the_email_failed_with_the_error_text()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var outbox = Outbox(db, new FakeSender(_ => new EmailSendResult(EmailSendStatus.Failed, Error: "Mailbox full")));
        var id = await outbox.QueueAsync(Welcome());
        await db.SaveChangesAsync();

        await outbox.DispatchAsync([id]);

        var log = await db.EmailLogs.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(EmailDeliveryStatus.Failed, log.DeliveryStatus);
        Assert.Equal("Mailbox full", log.Error);
    }

    [Fact]
    public async Task A_provider_that_throws_never_breaks_the_caller_and_the_email_is_failed()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var outbox = Outbox(db, new ThrowingSender());
        var id = await outbox.QueueAsync(Welcome());
        await db.SaveChangesAsync();

        await outbox.DispatchAsync([id]);

        var log = await db.EmailLogs.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(EmailDeliveryStatus.Failed, log.DeliveryStatus);
        Assert.DoesNotContain("exploded", log.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_only_touches_emails_that_are_still_queued()
    {
        var connection = await postgres.CreateDatabaseAsync();
        await using var db = PostgresFixture.CreateContext(connection);
        var sender = new FakeSender(_ => new EmailSendResult(EmailSendStatus.Sent));
        var outbox = Outbox(db, sender);
        var id = await outbox.QueueAsync(Welcome());
        await db.SaveChangesAsync();

        await outbox.DispatchAsync([id]);
        await outbox.DispatchAsync([id]);

        Assert.Single(sender.Sent);
    }
}
