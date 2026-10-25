using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Exceptions;

namespace TravelWebAPI.Application.Contact;

/// <summary>
/// One recorded email. <c>Status</c> is <c>queued</c>, <c>sent</c> or <c>failed</c>; <c>Error</c> says why a failed email was
/// not sent; <c>SentAt</c> is empty until it was sent. Emails with a secret (a reset link) are listed with the secret removed.
/// </summary>
public sealed record EmailLogDto(
    Guid Id,
    string From,
    string To,
    string Subject,
    string Body,
    string Kind,
    Guid? BookingId,
    Guid? MessageId,
    string Status,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt)
{
    public static EmailLogDto Of(EmailLog log) => new(
        log.Id,
        log.FromAddress,
        log.ToAddress,
        log.Subject,
        log.Body,
        EnumWire.ToWire(log.Kind),
        log.BookingId,
        log.ContactMessageId,
        EnumWire.ToWire(log.DeliveryStatus),
        log.Error,
        log.CreatedAt,
        log.SentAt);
}

public interface IEmailLogRepository
{
    /// <summary>One page of entries, searched, filtered and sorted in the database.</summary>
    Task<PagedResult<EmailLog>> PageAsync(EmailListFilter filter, ListQuery query, CancellationToken cancellationToken = default);

    /// <summary>One entry as it is stored now (not a tracked copy), or null.</summary>
    Task<EmailLog?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The entries linked to a booking, newest first.</summary>
    Task<IReadOnlyList<EmailLog>> ListForBookingAsync(Guid bookingId, CancellationToken cancellationToken = default);

    /// <summary>Whether an acknowledgement (<c>contact-ack</c>) to <paramref name="address"/> was recorded after <paramref name="since"/> (an entry made exactly at that time no longer counts).</summary>
    Task<bool> AcknowledgedSinceAsync(string address, DateTimeOffset since, CancellationToken cancellationToken = default);
}

/// <summary>Reading the email log.</summary>
public sealed class EmailLogService(IEmailLogRepository emailLogs)
{
    /// <summary>One page of the log, newest first unless sorted otherwise.</summary>
    public async Task<PagedResult<EmailLogDto>> ListAsync(EmailListRequest request, CancellationToken cancellationToken = default)
    {
        var (query, filter) = EmailLists.Parse(request);
        return (await emailLogs.PageAsync(filter, query, cancellationToken)).Map(EmailLogDto.Of);
    }
}
