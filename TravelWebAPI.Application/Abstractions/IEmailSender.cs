using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Abstractions;

public sealed record EmailMessage(
    EmailKind Kind,
    string To,
    string Subject,
    string Body,
    Guid? BookingId = null,
    Guid? ContactMessageId = null);

public sealed record EmailSendResult(bool Sent, string? ProviderMessageId = null, string? Error = null);

public interface IEmailSender
{
    /// <summary>Sends the email. Implementations report failure in the result instead of throwing.</summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
