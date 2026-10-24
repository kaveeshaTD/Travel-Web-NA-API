using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Abstractions;

public sealed record EmailMessage(
    EmailKind Kind,
    string To,
    string Subject,
    string Body,
    Guid? BookingId = null,
    Guid? ContactMessageId = null)
{
    /// <summary>
    /// The text to keep in the email log instead of <see cref="Body"/>. Emails that carry a secret
    /// (a password reset link) set this to a copy with the secret removed, so the database never holds it.
    /// </summary>
    public string? LogBody { get; init; }
}

public enum EmailSendStatus
{
    Sent,
    Failed,

    /// <summary>No provider is set up (testing phase). The email is not sent.</summary>
    NotConfigured,
}

public sealed record EmailSendResult(EmailSendStatus Status, string? ProviderMessageId = null, string? Error = null);

public interface IEmailSender
{
    /// <summary>Sends the email. Implementations report failure in the result instead of throwing.</summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
