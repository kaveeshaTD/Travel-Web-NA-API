using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Infrastructure.Persistence;

namespace TravelWebAPI.Infrastructure.Services;

/// <summary>Email settings the outbox needs (<c>Email</c> section).</summary>
public sealed class EmailOutboxOptions
{
    public const string Section = "Email";

    /// <summary>Sender address. When empty the website's contact email from the settings is used.</summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>
    /// Lets the log-only sender write email bodies to the application log (so a reset link can be copied while
    /// testing without SendGrid). Keep off outside Development: bodies can contain secrets.
    /// </summary>
    public bool LogBodies { get; set; }
}

public sealed class EmailOutbox(
    AppDbContext db,
    IEmailSender sender,
    IClock clock,
    IOptions<EmailOutboxOptions> options,
    ILogger<EmailOutbox> logger) : IEmailOutbox
{
    private const string FallbackFrom = "no-reply@localhost";

    // The real messages, kept only in memory until they are sent, because the saved copy may have secrets removed.
    private readonly Dictionary<Guid, EmailMessage> _pending = [];

    public async Task<Guid> QueueAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var log = new EmailLog
        {
            Id = Guid.CreateVersion7(),
            BookingId = message.BookingId,
            ContactMessageId = message.ContactMessageId,
            Kind = message.Kind,
            FromAddress = await ResolveFromAsync(cancellationToken),
            ToAddress = message.To,
            Subject = message.Subject,
            Body = message.LogBody ?? message.Body,
            DeliveryStatus = EmailDeliveryStatus.Queued,
        };

        await db.EmailLogs.AddAsync(log, cancellationToken);
        _pending[log.Id] = message;
        return log.Id;
    }

    public async Task DispatchAsync(IEnumerable<Guid> emailLogIds, CancellationToken cancellationToken = default)
    {
        var ids = emailLogIds.ToList();
        var logs = await db.EmailLogs
            .Where(e => ids.Contains(e.Id) && e.DeliveryStatus == EmailDeliveryStatus.Queued)
            .ToListAsync(cancellationToken);

        foreach (var log in logs)
        {
            var message = _pending.Remove(log.Id, out var original)
                ? original
                : new EmailMessage(log.Kind, log.ToAddress, log.Subject, log.Body, log.BookingId, log.ContactMessageId);

            EmailSendResult result;
            try
            {
                result = await sender.SendAsync(message, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A broken provider must never break the request that already succeeded.
                logger.LogError(ex, "Sending email {EmailLogId} failed", log.Id);
                result = new EmailSendResult(EmailSendStatus.Failed, Error: "The email provider could not be reached");
            }

            switch (result.Status)
            {
                case EmailSendStatus.Sent:
                    log.DeliveryStatus = EmailDeliveryStatus.Sent;
                    log.SentAt = clock.UtcNow;
                    log.ProviderMessageId = result.ProviderMessageId;
                    break;

                default:
                    // Not configured counts as failed on purpose: emails of the testing phase are never sent later.
                    log.DeliveryStatus = EmailDeliveryStatus.Failed;
                    log.Error = result.Error ?? LogOnlyEmailSender.NotSentReason;
                    break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> ResolveFromAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value.FromEmail;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var settings = await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(settings?.Email) ? FallbackFrom : settings.Email;
    }
}
