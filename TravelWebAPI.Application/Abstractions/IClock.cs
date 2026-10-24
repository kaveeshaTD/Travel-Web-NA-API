namespace TravelWebAPI.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>The current date in Sri Lanka (Asia/Colombo), used by date-based rules such as "not in the past".</summary>
    DateOnly TodayInSriLanka { get; }
}
