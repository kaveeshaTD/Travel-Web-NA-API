using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Search;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Infrastructure.Persistence;

/// <summary>Case-insensitive "contains" searches (<c>ILIKE</c>). The pattern arrives escaped and is always sent as a parameter.</summary>
public sealed class SearchRepository(AppDbContext context) : ISearchRepository
{
    private const string Escape = "\\";

    public async Task<IReadOnlyList<Booking>> SearchBookingsAsync(string pattern, int take, CancellationToken cancellationToken = default)
        => await context.Bookings
            .AsNoTracking()
            .Where(booking => EF.Functions.ILike(booking.Reference, pattern, Escape)
                || EF.Functions.ILike(booking.Name, pattern, Escape)
                || EF.Functions.ILike(booking.Email, pattern, Escape))
            .OrderByDescending(booking => booking.CreatedAt)
            .ThenByDescending(booking => booking.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SearchPackageDto>> SearchPackagesAsync(string pattern, int take, CancellationToken cancellationToken = default)
    {
        var rows = await context.Packages
            .AsNoTracking()
            .Where(package => EF.Functions.ILike(package.Name, pattern, Escape) || EF.Functions.ILike(package.Tagline, pattern, Escape))
            .OrderBy(package => package.Name)
            .ThenBy(package => package.Id)
            .Take(take)
            .Select(package => new { package.Id, package.Name })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new SearchPackageDto(row.Id, row.Name))];
    }

    public async Task<IReadOnlyList<SearchDestinationDto>> SearchDestinationsAsync(string pattern, int take, CancellationToken cancellationToken = default)
    {
        var rows = await context.Destinations
            .AsNoTracking()
            .Where(destination => EF.Functions.ILike(destination.Name, pattern, Escape) || EF.Functions.ILike(destination.Region, pattern, Escape))
            .OrderBy(destination => destination.Name)
            .ThenBy(destination => destination.Id)
            .Take(take)
            .Select(destination => new { destination.Id, destination.Name })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new SearchDestinationDto(row.Id, row.Name))];
    }

    public async Task<IReadOnlyList<SearchUserDto>> SearchUsersAsync(string pattern, int take, CancellationToken cancellationToken = default)
    {
        var rows = await context.Users
            .AsNoTracking()
            .Where(user => EF.Functions.ILike(user.Name, pattern, Escape) || EF.Functions.ILike(user.Email, pattern, Escape))
            .OrderBy(user => user.Name)
            .ThenBy(user => user.Id)
            .Take(take)
            .Select(user => new { user.Id, user.Name, user.Email, user.Role })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new SearchUserDto(row.Id, row.Name, row.Email, row.Role))];
    }
}
