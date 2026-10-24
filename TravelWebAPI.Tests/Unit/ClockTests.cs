using Microsoft.Extensions.Time.Testing;
using TravelWebAPI.Infrastructure.Services;

namespace TravelWebAPI.Tests.Unit;

public class ClockTests
{
    [Fact]
    public void Today_in_Sri_Lanka_is_the_next_day_late_in_the_evening_UTC()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 20, 0, 0, TimeSpan.Zero));
        var clock = new SystemClock(time);

        Assert.Equal(new DateOnly(2026, 3, 11), clock.TodayInSriLanka);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 20, 0, 0, TimeSpan.Zero), clock.UtcNow);
    }

    [Fact]
    public void Today_in_Sri_Lanka_is_the_same_day_in_the_morning_UTC()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 6, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 3, 10), new SystemClock(time).TodayInSriLanka);
    }

    [Fact]
    public void Sri_Lanka_midnight_boundary_is_at_18_30_UTC()
    {
        var before = new SystemClock(new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 18, 29, 0, TimeSpan.Zero)));
        var after = new SystemClock(new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 18, 30, 0, TimeSpan.Zero)));

        Assert.Equal(new DateOnly(2026, 3, 10), before.TodayInSriLanka);
        Assert.Equal(new DateOnly(2026, 3, 11), after.TodayInSriLanka);
    }
}
