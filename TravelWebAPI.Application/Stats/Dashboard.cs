using System.Globalization;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Application.Contact;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Application.Stats;

public sealed record DashboardKpis(
    int BookingsThisMonth,
    int BookingsLastMonth,
    int PendingBookings,
    decimal RevenueThisMonthLkr,
    decimal RevenueLastMonthLkr,
    int UnreadMessages,
    int TotalCustomers,
    decimal OutstandingBalanceLkr);

/// <summary>One month of the 12-month series. <c>Month</c> is <c>YYYY-MM</c> and <c>Label</c> is like <c>Oct 26</c>.</summary>
public sealed record DashboardMonth(string Month, string Label, int Bookings, decimal RevenueLkr);

public sealed record DashboardServiceCount(string ServiceId, string Title, int Count);

public sealed record DashboardBookingDto(
    Guid Id,
    string Reference,
    string Name,
    string ServiceId,
    string Status,
    string Date,
    string Time,
    int Adults,
    int Children,
    decimal? PriceLkr,
    DateTimeOffset CreatedAt)
{
    public static DashboardBookingDto Of(Booking b) => new(
        b.Id,
        b.Reference,
        b.Name,
        b.ServiceId,
        EnumWire.ToWire(b.Status),
        BookingFormats.Date(b.TravelDate),
        BookingFormats.Time(b.TravelTime),
        b.Adults,
        b.Children,
        b.PriceLkr,
        b.CreatedAt);
}

public sealed record DashboardDto(
    DashboardKpis Kpis,
    IReadOnlyList<DashboardMonth> Months,
    IReadOnlyList<DashboardServiceCount> ByService,
    IReadOnlyList<DashboardBookingDto> RecentBookings,
    IReadOnlyList<DashboardBookingDto> UpcomingTrips,
    IReadOnlyList<MessageDto> RecentMessages);

/// <summary>A calendar month in Sri Lanka time, with the instants (UTC) it starts and ends at.</summary>
public sealed record MonthWindow(string Key, string Label, DateOnly Start, DateOnly NextStart)
{
    public DateTimeOffset StartUtc => DashboardWindow.StartOfDay(Start);

    public DateTimeOffset NextStartUtc => DashboardWindow.StartOfDay(NextStart);
}

public static class DashboardWindow
{
    public const int MonthCount = 12;

    /// <summary>Sri Lanka has no daylight saving, so a fixed +05:30 offset is exact.</summary>
    public static readonly TimeSpan SriLankaOffset = TimeSpan.FromMinutes(330);

    private static readonly string[] Labels = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>The moment a Sri Lanka calendar day starts, as a UTC instant (the database only takes UTC).</summary>
    public static DateTimeOffset StartOfDay(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), SriLankaOffset).ToUniversalTime();

    /// <summary>The last 12 months ending with the month of <paramref name="today"/> (Sri Lanka date), oldest first.</summary>
    public static IReadOnlyList<MonthWindow> Months(DateOnly today)
    {
        var current = new DateOnly(today.Year, today.Month, 1);
        var result = new List<MonthWindow>(MonthCount);
        for (var back = MonthCount - 1; back >= 0; back--)
        {
            var start = current.AddMonths(-back);
            result.Add(new MonthWindow(
                start.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                $"{Labels[start.Month - 1]} {start.Year % 100:00}",
                start,
                start.AddMonths(1)));
        }

        return result;
    }
}

/// <summary>Read-only questions the dashboard asks. Months are Sri Lanka calendar months.</summary>
public interface IDashboardRepository
{
    /// <summary>Bookings created per month, keyed <c>YYYY-MM</c>, for the months between the two instants (UTC).</summary>
    Task<IReadOnlyDictionary<string, int>> BookingsPerMonthAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>Paid payments minus refunds (LKR) per month of <c>paid_at</c>, keyed <c>YYYY-MM</c>; pending payments are ignored.</summary>
    Task<IReadOnlyDictionary<string, decimal>> ReceivedPerMonthAsync(DateOnly firstDay, DateOnly afterLastDay, CancellationToken cancellationToken = default);

    Task<int> CountPendingBookingsAsync(CancellationToken cancellationToken = default);

    Task<int> CountUnreadMessagesAsync(CancellationToken cancellationToken = default);

    Task<int> CountCustomersAsync(CancellationToken cancellationToken = default);

    /// <summary>For confirmed bookings with a price: price minus paid (never below zero per booking), added up.</summary>
    Task<decimal> OutstandingBalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>Bookings that are not cancelled, counted per service that has at least one.</summary>
    Task<IReadOnlyList<DashboardServiceCount>> BookingsPerServiceAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Booking>> RecentBookingsAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Pending or confirmed bookings with a date from <paramref name="today"/> on, soonest first.</summary>
    Task<IReadOnlyList<Booking>> UpcomingTripsAsync(DateOnly today, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactMessage>> RecentMessagesAsync(int take, CancellationToken cancellationToken = default);
}

/// <summary>The admin dashboard: headline numbers, a 12-month series and the recent and upcoming lists.</summary>
public sealed class DashboardService(IDashboardRepository repository, IClock clock)
{
    public const int RecentBookings = 6;
    public const int UpcomingTrips = 5;
    public const int RecentMessages = 5;

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.TodayInSriLanka;
        var windows = DashboardWindow.Months(today);
        var first = windows[0];
        var last = windows[^1];

        var bookingsPerMonth = await repository.BookingsPerMonthAsync(first.StartUtc, last.NextStartUtc, cancellationToken);
        var receivedPerMonth = await repository.ReceivedPerMonthAsync(first.Start, last.NextStart, cancellationToken);

        var months = windows
            .Select(w => new DashboardMonth(
                w.Key,
                w.Label,
                bookingsPerMonth.GetValueOrDefault(w.Key),
                BookingMoney.Round(receivedPerMonth.GetValueOrDefault(w.Key))))
            .ToList();

        var kpis = new DashboardKpis(
            months[^1].Bookings,
            months[^2].Bookings,
            await repository.CountPendingBookingsAsync(cancellationToken),
            months[^1].RevenueLkr,
            months[^2].RevenueLkr,
            await repository.CountUnreadMessagesAsync(cancellationToken),
            await repository.CountCustomersAsync(cancellationToken),
            BookingMoney.Round(await repository.OutstandingBalanceAsync(cancellationToken)));

        return new DashboardDto(
            kpis,
            months,
            await repository.BookingsPerServiceAsync(cancellationToken),
            [.. (await repository.RecentBookingsAsync(RecentBookings, cancellationToken)).Select(DashboardBookingDto.Of)],
            [.. (await repository.UpcomingTripsAsync(today, UpcomingTrips, cancellationToken)).Select(DashboardBookingDto.Of)],
            [.. (await repository.RecentMessagesAsync(RecentMessages, cancellationToken)).Select(MessageDto.Of)]);
    }
}
