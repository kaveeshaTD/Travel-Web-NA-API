using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Common;

namespace TravelWebAPI.Infrastructure.Services;

/// <summary>
/// Stand-in used until an email provider (SendGrid) is configured. It writes the email to the log and
/// reports <see cref="EmailSendStatus.NotConfigured"/>, so the calling operation still succeeds.
/// The body is logged only when <c>Email:LogBodies</c> is on (Development), because bodies can hold secrets.
/// </summary>
public sealed class LogOnlyEmailSender(ILogger<LogOnlyEmailSender> logger, IOptions<EmailOutboxOptions> options) : IEmailSender
{
    public const string NotSentReason = "No email provider configured";

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var kind = EnumWire.ToWire(message.Kind);

        if (options.Value.LogBodies)
        {
            logger.LogInformation(
                "Email not sent (no provider configured). Kind={Kind} To={To} Subject={Subject}\n{Body}",
                kind,
                message.To,
                message.Subject,
                message.Body);
        }
        else
        {
            logger.LogInformation(
                "Email not sent (no provider configured). Kind={Kind} To={To} Subject={Subject}",
                kind,
                message.To,
                message.Subject);
        }

        return Task.FromResult(new EmailSendResult(EmailSendStatus.NotConfigured, Error: NotSentReason));
    }
}
