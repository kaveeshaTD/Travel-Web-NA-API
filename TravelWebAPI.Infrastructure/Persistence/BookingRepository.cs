using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Bookings;
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

    public async Task<IReadOnlyList<Booking>> ListAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        var query = Context.Bookings.AsNoTracking();
        if (userId is { } owner)
        {
            query = query.Where(booking => booking.UserId == owner);
        }

        return await query
            .OrderByDescending(booking => booking.CreatedAt)
            .ThenByDescending(booking => booking.Id)
            .ToListAsync(cancellationToken);
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
