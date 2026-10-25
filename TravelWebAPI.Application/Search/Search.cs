using TravelWebAPI.Application.Bookings;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Search;

public sealed record SearchBookingDto(Guid Id, string Reference, string Name, string Status, string Date)
{
    public static SearchBookingDto Of(Booking b) => new(b.Id, b.Reference, b.Name, EnumWire.ToWire(b.Status), BookingFormats.Date(b.TravelDate));
}

public sealed record SearchPackageDto(string Id, string Name);

public sealed record SearchDestinationDto(string Id, string Name);

public sealed record SearchUserDto(Guid Id, string Name, string Email, UserRole Role);

public sealed record SearchResultDto(
    IReadOnlyList<SearchBookingDto> Bookings,
    IReadOnlyList<SearchPackageDto> Packages,
    IReadOnlyList<SearchDestinationDto> Destinations,
    IReadOnlyList<SearchUserDto> Users)
{
    public static readonly SearchResultDto Empty = new([], [], [], []);
}

/// <summary>
/// Read-only searches. Each takes a ready <c>LIKE</c> pattern (already escaped, with a backslash as the escape character)
/// and matches it ignoring letter case.
/// </summary>
public interface ISearchRepository
{
    /// <summary>By reference, name or email; newest first.</summary>
    Task<IReadOnlyList<Booking>> SearchBookingsAsync(string pattern, int take, CancellationToken cancellationToken = default);

    /// <summary>By name or tagline, published or not.</summary>
    Task<IReadOnlyList<SearchPackageDto>> SearchPackagesAsync(string pattern, int take, CancellationToken cancellationToken = default);

    /// <summary>By name or region.</summary>
    Task<IReadOnlyList<SearchDestinationDto>> SearchDestinationsAsync(string pattern, int take, CancellationToken cancellationToken = default);

    /// <summary>By name or email.</summary>
    Task<IReadOnlyList<SearchUserDto>> SearchUsersAsync(string pattern, int take, CancellationToken cancellationToken = default);
}

public static class SearchRules
{
    public const int MinLength = 2;
    public const int MaxLength = 100;
    public const int BookingLimit = 6;
    public const int OtherLimit = 5;

    /// <summary>The trimmed text cut to <see cref="MaxLength"/>; empty when the search is too short to run.</summary>
    public static string Clean(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length > MaxLength)
        {
            trimmed = trimmed[..MaxLength].Trim();
        }

        return trimmed.Length < MinLength ? string.Empty : trimmed;
    }

    /// <summary>A "contains" pattern in which percent, underscore and backslash in the text are plain characters.</summary>
    public static string ContainsPattern(string text) => Lists.LikePattern.Contains(text);
}

/// <summary>The admin's global search over bookings, packages, destinations and users.</summary>
public sealed class SearchService(ISearchRepository repository)
{
    public async Task<SearchResultDto> SearchAsync(string? text, CancellationToken cancellationToken = default)
    {
        var clean = SearchRules.Clean(text);
        if (clean.Length == 0)
        {
            return SearchResultDto.Empty;
        }

        var pattern = SearchRules.ContainsPattern(clean);

        // One database connection runs one query at a time, so these run one after the other.
        var bookings = await repository.SearchBookingsAsync(pattern, SearchRules.BookingLimit, cancellationToken);
        var packages = await repository.SearchPackagesAsync(pattern, SearchRules.OtherLimit, cancellationToken);
        var destinations = await repository.SearchDestinationsAsync(pattern, SearchRules.OtherLimit, cancellationToken);
        var users = await repository.SearchUsersAsync(pattern, SearchRules.OtherLimit, cancellationToken);

        return new SearchResultDto([.. bookings.Select(SearchBookingDto.Of)], packages, destinations, users);
    }
}
