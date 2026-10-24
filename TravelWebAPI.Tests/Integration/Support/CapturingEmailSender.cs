using System.Collections.Concurrent;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Tests.Integration.Support;

/// <summary>Stands in for the email provider: keeps what would have been sent and reports "no provider configured".</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public IReadOnlyList<EmailMessage> Messages => [.. _messages];

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(message);
        return Task.FromResult(new EmailSendResult(EmailSendStatus.NotConfigured, Error: "No email provider configured"));
    }

    /// <summary>The reset token from the newest password-reset email.</summary>
    public string LatestResetToken()
    {
        var email = Messages.Last(m => m.Kind == EmailKind.PasswordReset);
        return email.Body.Split("token=")[1].Split('\n')[0].Trim();
    }
}
