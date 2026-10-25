using Microsoft.EntityFrameworkCore;
using TravelWebAPI.Application.Content;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Infrastructure.Persistence;

/// <summary>What the six ordered content collections share. Each repository adds its child rows and what blocks a delete.</summary>
public abstract class ContentRepository<T>(AppDbContext context) : EfRepository<T>(context), IContentRepository<T>
    where T : class, IOrdered
{
    /// <summary>The query with the child rows loaded.</summary>
    protected virtual IQueryable<T> WithChildren(IQueryable<T> query) => query;

    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken = default)
        => await WithChildren(Context.Set<T>()).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(cancellationToken);

    public Task<T?> FindAsync(string id, CancellationToken cancellationToken = default)
        => WithChildren(Context.Set<T>()).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlySet<string>> ExistingIdsAsync(string prefix, CancellationToken cancellationToken = default)
        => (await Context.Set<T>().Where(x => x.Id.StartsWith(prefix)).Select(x => x.Id).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

    public async Task<int> NextSortOrderAsync(CancellationToken cancellationToken = default)
        => await Context.Set<T>().MaxAsync(x => (int?)x.SortOrder, cancellationToken) + 1 ?? 0;

    public virtual Task<IReadOnlyList<string>> GetDependentsAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    /// <summary>Labels like <c>booking BK-ABC123</c> for the bookings that match.</summary>
    protected async Task<IReadOnlyList<string>> BookingDependentsAsync(
        System.Linq.Expressions.Expression<Func<Booking, bool>> filter,
        CancellationToken cancellationToken)
        => (await Context.Bookings
                .Where(filter)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => b.Reference)
                .Take(ContentLimits.MaxDependents)
                .ToListAsync(cancellationToken))
            .Select(reference => $"booking {reference}")
            .ToList();
}

public sealed class FaqRepository(AppDbContext context) : ContentRepository<Faq>(context);

public sealed class GalleryImageRepository(AppDbContext context) : ContentRepository<GalleryImage>(context);

public sealed class VehicleRepository(AppDbContext context) : ContentRepository<Vehicle>(context)
{
    public override Task<IReadOnlyList<string>> GetDependentsAsync(string id, CancellationToken cancellationToken = default)
        => BookingDependentsAsync(b => b.VehicleId == id, cancellationToken);
}

public sealed class ServiceRepository(AppDbContext context) : ContentRepository<Service>(context)
{
    protected override IQueryable<Service> WithChildren(IQueryable<Service> query) => query.Include(s => s.Details);

    public override Task<IReadOnlyList<string>> GetDependentsAsync(string id, CancellationToken cancellationToken = default)
        => BookingDependentsAsync(b => b.ServiceId == id, cancellationToken);
}

public sealed class DestinationRepository(AppDbContext context) : ContentRepository<Destination>(context)
{
    protected override IQueryable<Destination> WithChildren(IQueryable<Destination> query) => query.Include(d => d.Images);

    public override async Task<IReadOnlyList<string>> GetDependentsAsync(string id, CancellationToken cancellationToken = default)
        => (await Context.Packages
                .Where(p => p.Destinations.Any(d => d.DestinationId == id))
                .OrderBy(p => p.Name)
                .Select(p => p.Name)
                .Take(ContentLimits.MaxDependents)
                .ToListAsync(cancellationToken))
            .Select(name => $"package \"{name}\"")
            .ToList();
}

public sealed class PackageRepository(AppDbContext context) : ContentRepository<Package>(context), IPackageRepository
{
    protected override IQueryable<Package> WithChildren(IQueryable<Package> query)
        => query
            .Include(p => p.Destinations).ThenInclude(d => d.Destination)
            .Include(p => p.ItineraryDays)
            .Include(p => p.Interests)
            .Include(p => p.Images)
            .Include(p => p.Items)
            .AsSplitQuery();

    public async Task<IReadOnlyList<Package>> ListPublishedAsync(CancellationToken cancellationToken = default)
        => await WithChildren(Context.Packages)
            .Where(p => p.Active)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<string>> ExistingDestinationIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
        => (await Context.Destinations.Where(d => ids.Contains(d.Id)).Select(d => d.Id).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

    public override Task<IReadOnlyList<string>> GetDependentsAsync(string id, CancellationToken cancellationToken = default)
        => BookingDependentsAsync(b => b.PackageId == id, cancellationToken);
}
