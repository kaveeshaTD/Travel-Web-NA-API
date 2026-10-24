namespace TravelWebAPI.Application.Abstractions;

/// <summary>
/// Counts failed sign-ins for emails that have no account, with the same limits as real accounts,
/// so the lockout response does not reveal which emails are registered.
/// </summary>
public interface ILoginAttemptTracker
{
    /// <summary>How long sign-in stays locked for this email, or null when it is not locked.</summary>
    TimeSpan? GetRetryAfter(string email);

    void RecordFailure(string email);

    void Reset(string email);
}
