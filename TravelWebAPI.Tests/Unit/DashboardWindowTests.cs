using TravelWebAPI.Application.Stats;

namespace TravelWebAPI.Tests.Unit;

public sealed class DashboardWindowTests
{
    [Fact]
    public void There_are_twelve_months_oldest_first_ending_with_the_current_one()
    {
        var months = DashboardWindow.Months(new DateOnly(2026, 10, 25));

        Assert.Equal(12, months.Count);
        Assert.Equal("2025-11", months[0].Key);
        Assert.Equal("Nov 25", months[0].Label);
        Assert.Equal("2026-10", months[^1].Key);
        Assert.Equal("Oct 26", months[^1].Label);
        Assert.Equal(months.Select(m => m.Key).Order(), months.Select(m => m.Key));
    }

    [Fact]
    public void The_window_crosses_the_year_change()
    {
        var months = DashboardWindow.Months(new DateOnly(2026, 1, 1));

        Assert.Equal("2025-02", months[0].Key);
        Assert.Equal("Feb 25", months[0].Label);
        Assert.Equal("2025-12", months[^2].Key);
        Assert.Equal("Dec 25", months[^2].Label);
        Assert.Equal("2026-01", months[^1].Key);
    }

    [Fact]
    public void Any_day_of_the_month_gives_the_same_window_and_each_month_ends_where_the_next_starts()
    {
        var first = DashboardWindow.Months(new DateOnly(2026, 3, 1));
        var last = DashboardWindow.Months(new DateOnly(2026, 3, 31));

        Assert.Equal(first.Select(m => m.Key), last.Select(m => m.Key));
        for (var index = 0; index < first.Count - 1; index++)
        {
            Assert.Equal(first[index].NextStart, first[index + 1].Start);
        }
    }

    [Fact]
    public void A_month_starts_at_midnight_in_sri_lanka_which_is_the_evening_before_in_utc()
    {
        var october = DashboardWindow.Months(new DateOnly(2026, 10, 25))[^1];

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 18, 30, 0, TimeSpan.Zero), october.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 18, 30, 0, TimeSpan.Zero), october.NextStartUtc);
    }
}
