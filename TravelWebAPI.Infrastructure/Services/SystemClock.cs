using TravelWebAPI.Application.Abstractions;

namespace TravelWebAPI.Infrastructure.Services;

public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    private static readonly TimeZoneInfo SriLanka = FindSriLankaZone();

    public SystemClock() : this(TimeProvider.System)
    {
    }

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateOnly TodayInSriLanka
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, SriLanka).DateTime);

    private static TimeZoneInfo FindSriLankaZone()
    {
        // IANA id on Linux and macOS, Windows id on Windows (newer .NET resolves both, but be explicit).
        foreach (var id in new[] { "Asia/Colombo", "Sri Lanka Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Asia/Colombo", TimeSpan.FromHours(5.5), "Sri Lanka", "Sri Lanka");
    }
}
