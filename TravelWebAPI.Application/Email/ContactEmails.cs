using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Email;

/// <summary>Texts of the emails sent for contact messages.</summary>
public static class ContactEmails
{
    /// <summary>"We got your message", sent to the address that filled in the contact form and linked to the saved message.</summary>
    public static EmailMessage Acknowledgement(string to, string name, string siteName, string driverName, string message, Guid contactMessageId)
        => new(
            EmailKind.ContactAck,
            to,
            "We received your message",
            $"Hi {name},\n\nThanks for getting in touch. {driverName} will reply soon.\n\nYour message:\n{message}\n\n{siteName}",
            ContactMessageId: contactMessageId);

    public static string DefaultReplySubject(string siteName) => $"Re: your message to {siteName}";
}
