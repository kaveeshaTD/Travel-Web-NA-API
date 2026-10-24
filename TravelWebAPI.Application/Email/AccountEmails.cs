using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Email;

/// <summary>Texts of the emails sent for accounts.</summary>
public static class AccountEmails
{
    public const string RedactedToken = "[REDACTED]";

    public static EmailMessage Welcome(string to, string name, string siteName, string driverName) => new(
        EmailKind.Welcome,
        to,
        $"Welcome to {siteName}",
        $"Hi {name},\n\nYour account is ready. You can now sign in to manage your bookings.\n\n{driverName}");

    /// <summary>The email gets the real link; the copy kept in the email log has the token removed.</summary>
    public static EmailMessage PasswordReset(string to, string name, string webBaseUrl, string token, int validMinutes)
    {
        string Text(string link) =>
            $"Hi {name},\n\nUse this link to choose a new password: {link}\n\n" +
            $"The link works for {validMinutes} minutes. If you did not ask for this, you can ignore this email.";

        return new EmailMessage(EmailKind.PasswordReset, to, "Reset your password", Text(ResetLink(webBaseUrl, token)))
        {
            LogBody = Text($"{webBaseUrl.TrimEnd('/')}/reset-password?token={RedactedToken}"),
        };
    }

    public static string ResetLink(string webBaseUrl, string token)
        => $"{webBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}";
}
