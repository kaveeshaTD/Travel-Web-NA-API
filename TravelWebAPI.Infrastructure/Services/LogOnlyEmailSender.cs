using Microsoft.Extensions.Logging;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Common;

namespace TravelWebAPI.Infrastructure.Services;

/// <summary>
/// Stand-in used until an email provider (SendGrid) is configured. It writes the email to the log and
/// reports "not sent", so the calling operation still succeeds.
/// </summary>
public sealed class LogOnlyEmailSender(ILogger<LogOnlyEmailSender> logger) : IEmailSender
{
    public const string NotSentReason = "No email provider is configured";

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var kind = EnumWire.ToWire(message.Kind);

        logger.LogInformation(
            "Email not sent (no provider configured). Kind={Kind} To={To} Subject={Subject}\n{Body}",
            kind,
            message.To,
            message.Subject,
            message.Body);

        return Task.FromResult(new EmailSendResult(Sent: false, Error: NotSentReason));
    }
}
