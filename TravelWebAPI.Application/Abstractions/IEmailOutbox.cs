namespace TravelWebAPI.Application.Abstractions;

/// <summary>
/// Records emails in the email log together with the business change, then sends them after the commit.
/// An email that is not sent (no provider configured, or the provider failed) is marked failed and is never retried.
/// </summary>
public interface IEmailOutbox
{
    /// <summary>Adds a <c>queued</c> email log row to the current unit of work. Nothing is saved or sent yet.</summary>
    Task<Guid> QueueAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>Sends the queued emails and records <c>sent</c> or <c>failed</c>. Never throws for delivery problems.</summary>
    Task DispatchAsync(IEnumerable<Guid> emailLogIds, CancellationToken cancellationToken = default);
}
