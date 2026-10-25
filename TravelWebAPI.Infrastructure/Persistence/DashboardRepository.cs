using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Stats;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Infrastructure.Persistence;

/// <summary>Read-only dashboard queries. Everything is grouped or limited in the database.</summary>
public sealed class DashboardRepository(AppDbContext context) : IDashboardRepository
{
    private static string Key(int year, int month) => string.Create(CultureInfo.InvariantCulture, $"{year:0000}-{month:00}");

    public async Task<IReadOnlyDictionary<string, int>> BookingsPerMonthAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // The Sri Lanka month of a booking is the month of its creation time shifted by +05:30.
        var minutes = (int)DashboardWindow.SriLankaOffset.TotalMinutes;

        var rows = await context.Bookings
            .AsNoTracking()
            .Where(booking => booking.CreatedAt >= fromUtc && booking.CreatedAt < toUtc)
            .GroupBy(booking => new { booking.CreatedAt.UtcDateTime.AddMinutes(minutes).Year, booking.CreatedAt.UtcDateTime.AddMinutes(minutes).Month })
            .Select(group => new { group.Key.Year, group.Key.Month, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => Key(row.Year, row.Month), row => row.Count);
    }

    public async Task<IReadOnlyDictionary<string, decimal>> ReceivedPerMonthAsync(DateOnly firstDay, DateOnly afterLastDay, CancellationToken cancellationToken = default)
    {
        var rows = await context.Payments
            .AsNoTracking()
            .Where(payment => payment.PaidAt >= firstDay && payment.PaidAt < afterLastDay && payment.Status != PaymentStatus.Pending)
            .GroupBy(payment => new { payment.PaidAt.Year, payment.PaidAt.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Total = group.Sum(payment => payment.Status == PaymentStatus.Paid ? payment.AmountLkr : -payment.AmountLkr),
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => Key(row.Year, row.Month), row => row.Total);
    }

    public Task<int> CountPendingBookingsAsync(CancellationToken cancellationToken = default)
        => context.Bookings.CountAsync(booking => booking.Status == BookingStatus.Pending, cancellationToken);

    public Task<int> CountUnreadMessagesAsync(CancellationToken cancellationToken = default)
        => context.ContactMessages.CountAsync(message => message.Status == MessageStatus.New, cancellationToken);

    public Task<int> CountCustomersAsync(CancellationToken cancellationToken = default)
        => context.Users.CountAsync(user => user.Role == UserRole.Customer, cancellationToken);

    public async Task<decimal> OutstandingBalanceAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Bookings
            .AsNoTracking()
            .Where(booking => booking.Status == BookingStatus.Confirmed && booking.PriceLkr != null)
            .Select(booking => new
            {
                Price = booking.PriceLkr!.Value,
                Paid = booking.Payments.Sum(payment => payment.Status == PaymentStatus.Paid
                    ? payment.AmountLkr
                    : payment.Status == PaymentStatus.Refunded ? -payment.AmountLkr : 0m),
            })
            .ToListAsync(cancellationToken);

        return rows.Sum(row => Math.Max(row.Price - row.Paid, 0m));
    }

    public async Task<IReadOnlyList<DashboardServiceCount>> BookingsPerServiceAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Bookings
            .AsNoTracking()
            .Where(booking => booking.Status != BookingStatus.Cancelled)
            .GroupBy(booking => new { booking.ServiceId, booking.Service!.Title })
            .Select(group => new { group.Key.ServiceId, group.Key.Title, Count = group.Count() })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Title)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new DashboardServiceCount(row.ServiceId, row.Title, row.Count))];
    }

    public async Task<IReadOnlyList<Booking>> RecentBookingsAsync(int take, CancellationToken cancellationToken = default)
        => await context.Bookings
            .AsNoTracking()
            .OrderByDescending(booking => booking.CreatedAt)
            .ThenByDescending(booking => booking.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Booking>> UpcomingTripsAsync(DateOnly today, int take, CancellationToken cancellationToken = default)
        => await context.Bookings
            .AsNoTracking()
            .Where(booking => (booking.Status == BookingStatus.Pending || booking.Status == BookingStatus.Confirmed) && booking.TravelDate >= today)
            .OrderBy(booking => booking.TravelDate)
            .ThenBy(booking => booking.TravelTime)
            .ThenBy(booking => booking.Reference)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContactMessage>> RecentMessagesAsync(int take, CancellationToken cancellationToken = default)
        => await context.ContactMessages
            .AsNoTracking()
            .OrderByDescending(message => message.CreatedAt)
            .ThenByDescending(message => message.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
}
