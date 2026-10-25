using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Infrastructure.Persistence.Lists;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class ContactMessageRepository(AppDbContext context) : EfRepository<ContactMessage>(context), IContactMessageRepository
{
    private static readonly ListSpec<ContactMessage> PageSpec = new ListSpec<ContactMessage>(
        SortMap.TieBreakBy<ContactMessage, Guid>(message => message.Id)
            .Add("createdAt", message => message.CreatedAt)
            .Add("name", message => message.Name)
            .Add("status", message => message.Status),
        [message => message.Name, message => message.Email, message => message.Message])
        .AddFacet(new Facet<ContactMessage, MessageStatus>("status", message => message.Status, EnumWire.ToWire, [.. Enum.GetValues<MessageStatus>().Select(EnumWire.ToWire)]));

    /// <summary>The sort fields and facets this list implements, for the test that compares them with the definition.</summary>
    public static (IReadOnlyCollection<string> SortFields, IReadOnlyCollection<string> Facets) ListNames => PageSpec.Names;

    public async Task<PagedResult<ContactMessage>> PageAsync(MessageListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
    {
        var status = filter.Status;
        var filters = new FilterSet<ContactMessage>().Add("status", status is null ? null : message => message.Status == status);
        return await PageSpec.RunAsync(Context.ContactMessages.AsNoTracking(), filters, query, cancellationToken);
    }
}

public sealed class EmailLogRepository(AppDbContext context) : IEmailLogRepository
{
    private static readonly ListSpec<EmailLog> PageSpec = new ListSpec<EmailLog>(
        SortMap.TieBreakBy<EmailLog, Guid>(log => log.Id)
            .Add("createdAt", log => log.CreatedAt)
            .Add("to", log => log.ToAddress)
            .Add("subject", log => log.Subject)
            .Add("kind", log => log.Kind)
            .Add("status", log => log.DeliveryStatus),
        [log => log.ToAddress, log => log.Subject])
        .AlsoMatch(KindMatches)
        .AddFacet(new Facet<EmailLog, EmailDeliveryStatus>("status", log => log.DeliveryStatus, EnumWire.ToWire, [.. Enum.GetValues<EmailDeliveryStatus>().Select(EnumWire.ToWire)]))
        .AddFacet(new Facet<EmailLog, EmailKind>("kind", log => log.Kind, EnumWire.ToWire, [.. Enum.GetValues<EmailKind>().Select(EnumWire.ToWire)]));

    /// <summary>The kind is stored as its written form (<c>booking-received</c>); the text matches the kinds whose written form contains it.</summary>
    private static Expression<Func<EmailLog, bool>>? KindMatches(string text)
    {
        var kinds = Enum.GetValues<EmailKind>().Where(kind => EnumWire.ToWire(kind).Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        return kinds.Length == 0 ? null : log => kinds.Contains(log.Kind);
    }

    /// <summary>The sort fields and facets this list implements, for the test that compares them with the definition.</summary>
    public static (IReadOnlyCollection<string> SortFields, IReadOnlyCollection<string> Facets) ListNames => PageSpec.Names;

    public async Task<PagedResult<EmailLog>> PageAsync(EmailListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
    {
        var status = filter.Status;
        var kind = filter.Kind;
        var bookingId = filter.BookingId;
        var filters = new FilterSet<EmailLog>()
            .Add("status", status is null ? null : log => log.DeliveryStatus == status)
            .Add("kind", kind is null ? null : log => log.Kind == kind)
            .Add("booking", bookingId is null ? null : log => log.BookingId == bookingId);

        return await PageSpec.RunAsync(context.EmailLogs.AsNoTracking(), filters, query, cancellationToken);
    }

    /// <summary>Read from the database, not from the change tracker, so the delivery result written by the outbox is included.</summary>
    public Task<EmailLog?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => context.EmailLogs.AsNoTracking().FirstOrDefaultAsync(log => log.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EmailLog>> ListForBookingAsync(Guid bookingId, CancellationToken cancellationToken = default)
        => await context.EmailLogs
            .AsNoTracking()
            .Where(log => log.BookingId == bookingId)
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> AcknowledgedSinceAsync(string address, DateTimeOffset since, CancellationToken cancellationToken = default)
        => context.EmailLogs.AnyAsync(
            log => log.Kind == EmailKind.ContactAck && log.ToAddress == address && log.CreatedAt > since,
            cancellationToken);
}
