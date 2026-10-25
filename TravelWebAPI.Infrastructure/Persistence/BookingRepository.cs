using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Infrastructure.Persistence.Lists;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Persistence;

public sealed class BookingRepository(AppDbContext context) : EfRepository<Booking>(context), IBookingRepository
{
    public Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken = default)
        => Context.Bookings.AnyAsync(booking => booking.Reference == reference, cancellationToken);

    /// <summary>A text that parses as a GUID is an id; a text that starts with <c>BK-</c> is a reference (stored in upper case).</summary>
    public Task<Booking?> FindAsync(string idOrReference, CancellationToken cancellationToken = default)
    {
        if (Guid.TryParse(idOrReference, out var id))
        {
            return Context.Bookings.FirstOrDefaultAsync(booking => booking.Id == id, cancellationToken);
        }

        if (BookingReference.Looks(idOrReference))
        {
            var reference = idOrReference.ToUpperInvariant();
            return Context.Bookings.FirstOrDefaultAsync(booking => booking.Reference == reference, cancellationToken);
        }

        return Task.FromResult<Booking?>(null);
    }

    private static readonly ListSpec<Booking> PageSpec = new ListSpec<Booking>(
        SortMap.TieBreakBy<Booking, Guid>(booking => booking.Id)
            .Add("createdAt", booking => booking.CreatedAt)
            .Add("date", booking => booking.TravelDate)
            .Add("reference", booking => booking.Reference)
            .Add("name", booking => booking.Name)
            .Add("status", booking => booking.Status)
            .AddNullsLast("priceLkr", booking => booking.PriceLkr),
        [booking => booking.Reference, booking => booking.Name, booking => booking.Email, booking => booking.Whatsapp])
        .AddFacet(new Facet<Booking, BookingStatus>("status", booking => booking.Status, EnumWire.ToWire, [.. Enum.GetValues<BookingStatus>().Select(EnumWire.ToWire)]));

    /// <summary>The sort fields and facets this list implements, for the test that compares them with the definition.</summary>
    public static (IReadOnlyCollection<string> SortFields, IReadOnlyCollection<string> Facets) ListNames => PageSpec.Names;

    public async Task<PagedResult<Booking>> PageAsync(Guid? userId, BookingListFilter filter, ListQuery query, CancellationToken cancellationToken = default)
    {
        var source = Context.Bookings.AsNoTracking();
        if (userId is { } owner)
        {
            source = source.Where(booking => booking.UserId == owner);
        }

        var status = filter.Status;
        var serviceId = filter.ServiceId;
        var from = filter.From;
        var to = filter.To;
        var filters = new FilterSet<Booking>()
            .Add("status", status is null ? null : booking => booking.Status == status)
            .Add("serviceId", serviceId is null ? null : booking => booking.ServiceId == serviceId)
            .Add("from", from is null ? null : booking => booking.TravelDate >= from)
            .Add("to", to is null ? null : booking => booking.TravelDate <= to);

        return await PageSpec.RunAsync(source, filters, query, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> PaidTotalsAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken cancellationToken = default)
    {
        if (bookingIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        return await Context.Payments
            .AsNoTracking()
            .Where(payment => bookingIds.Contains(payment.BookingId))
            .GroupBy(payment => payment.BookingId)
            .Select(group => new
            {
                BookingId = group.Key,
                Paid = group.Sum(payment => payment.Status == PaymentStatus.Paid
                    ? payment.AmountLkr
                    : payment.Status == PaymentStatus.Refunded ? -payment.AmountLkr : 0m),
            })
            .ToDictionaryAsync(row => row.BookingId, row => row.Paid, cancellationToken);
    }

    public async Task<IReadOnlyList<Payment>> GetPaymentsAsync(Guid bookingId, CancellationToken cancellationToken = default)
        => await Context.Payments
            .AsNoTracking()
            .Where(payment => payment.BookingId == bookingId)
            .OrderByDescending(payment => payment.PaidAt)
            .ThenByDescending(payment => payment.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DateTimeOffset>> RecentCreatedAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default)
        => await Context.Bookings
            .AsNoTracking()
            .Where(booking => booking.UserId == userId && booking.CreatedAt >= since)
            .Select(booking => booking.CreatedAt)
            .ToListAsync(cancellationToken);
}
