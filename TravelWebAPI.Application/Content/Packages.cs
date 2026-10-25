using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;
using TravelWebAPI.Domain.Exceptions;
using ValidationException = TravelWebAPI.Domain.Exceptions.ValidationException;

namespace TravelWebAPI.Application.Content;

/// <summary>A day of the itinerary as sent by the admin. The day number comes from the position, so any number sent is ignored.</summary>
public sealed record ItineraryDayRequest(string? Title = null, string? Description = null);

public sealed record PackageRequest(
    string? Name = null,
    string? Tagline = null,
    int? DurationDays = null,
    IReadOnlyList<string>? Highlights = null,
    IReadOnlyList<string>? Interests = null,
    decimal? PriceLkr = null,
    string? Image = null,
    IReadOnlyList<string>? Gallery = null,
    IReadOnlyList<ItineraryDayRequest>? Itinerary = null,
    IReadOnlyList<string>? Included = null,
    IReadOnlyList<string>? Excluded = null,
    bool? Featured = null,
    bool? Active = null,
    IReadOnlyList<string>? DestinationIds = null);

public sealed record ItineraryDayDraft(string Title, string Description);

public sealed record PackageDraft(
    string Name,
    string Tagline,
    int DurationDays,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> Interests,
    decimal PriceLkr,
    string Image,
    IReadOnlyList<string> Gallery,
    IReadOnlyList<ItineraryDayDraft> Itinerary,
    IReadOnlyList<string> Included,
    IReadOnlyList<string> Excluded,
    bool Featured,
    bool Active,
    IReadOnlyList<string> DestinationIds);

public sealed record ItineraryDayDto(int Day, string Title, string Description);

/// <summary><c>Route</c> holds the names of the destinations in <c>DestinationIds</c>, in the same order.</summary>
public sealed record PackageDto(
    string Id,
    string Name,
    string Tagline,
    int DurationDays,
    IReadOnlyList<string> Route,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<string> Interests,
    decimal PriceLkr,
    string Image,
    IReadOnlyList<string> Gallery,
    IReadOnlyList<ItineraryDayDto> Itinerary,
    IReadOnlyList<string> Included,
    IReadOnlyList<string> Excluded,
    bool Featured,
    bool Active,
    IReadOnlyList<string> DestinationIds) : IContentDto;

public interface IPackageRepository : IContentRepository<Package>
{
    /// <summary>Only the published packages, in display order, with their child rows.</summary>
    Task<IReadOnlyList<Package>> ListPublishedAsync(CancellationToken cancellationToken = default);

    /// <summary>Which of <paramref name="ids"/> are ids of existing destinations.</summary>
    Task<IReadOnlySet<string>> ExistingDestinationIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);
}

public sealed class PackageDraftValidator : AbstractValidator<PackageDraft>
{
    public static readonly string InterestsMessage = $"Interests must be: {string.Join(", ", Enum.GetValues<InterestType>().Select(EnumWire.ToWire))}";

    public PackageDraftValidator()
    {
        RuleFor(x => x.Name).MinLength(2, "Enter a package name");
        RuleFor(x => x.Name).MaxLength(150, "The package name is too long");
        RuleFor(x => x.Tagline).MaxLength(250, "The tagline is too long");
        RuleFor(x => x.DurationDays).InclusiveBetween(1, 60).WithMessage("Duration must be 1 to 60 days");
        RuleFor(x => x.PriceLkr).Must(price => price > 0 && ContentRules.IsMoney(price)).WithMessage("Enter a price greater than 0");
        RuleFor(x => x.Image).Image("Choose a cover image");
        RuleFor(x => x.Gallery).Images("Every gallery image must be a link, an uploaded file or a website photo");
        RuleFor(x => x.Highlights).Lines("Use at most 50 highlights of up to 300 characters each");
        RuleFor(x => x.Included).Lines("Use at most 50 included items of up to 300 characters each");
        RuleFor(x => x.Excluded).Lines("Use at most 50 excluded items of up to 300 characters each");
        RuleFor(x => x.Interests).Must(interests => interests.All(i => EnumWire.TryFromWire<InterestType>(i, out _))).WithMessage(InterestsMessage);
        RuleFor(x => x.Itinerary).Must(days => days.All(d => d.Title.Length > 0)).WithMessage("Every itinerary day needs a title");
        RuleFor(x => x.Itinerary)
            .Must(days => days.Count <= 60 && days.All(d => d.Title.Length <= 150 && d.Description.Length <= 4000))
            .WithMessage("Use at most 60 itinerary days, with titles up to 150 characters");
        RuleFor(x => x.DestinationIds)
            .Must(ids => ids.Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage("A destination can appear only once in the route");
    }
}

public sealed class PackageService : IContentService<PackageRequest, PackageDto>
{
    private readonly ContentWriter<Package> _writer;
    private readonly IPackageRepository _packages;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<PackageDraft> _validator;
    private readonly ICurrentUser _currentUser;

    public PackageService(IPackageRepository packages, IUnitOfWork unitOfWork, IValidator<PackageDraft> validator, ICurrentUser currentUser)
    {
        _packages = packages;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _currentUser = currentUser;
        _writer = new ContentWriter<Package>(packages, unitOfWork, "package");
    }

    /// <summary>Published packages; admins can ask for every package.</summary>
    public async Task<IReadOnlyList<PackageDto>> ListAsync(bool includeUnpublished, CancellationToken ct = default)
    {
        var packages = includeUnpublished && _currentUser.IsAdmin
            ? await _packages.ListAsync(ct)
            : await _packages.ListPublishedAsync(ct);
        return [.. packages.Select(ToDto)];
    }

    /// <summary>An unpublished package looks like a missing one to everybody except admins.</summary>
    public async Task<PackageDto> GetAsync(string id, CancellationToken ct = default)
    {
        var package = await _writer.GetAsync(id, ct);
        if (!package.Active && !_currentUser.IsAdmin)
        {
            throw new NotFoundException(_writer.NotFoundMessage);
        }

        return ToDto(package);
    }

    public async Task<PackageDto> CreateAsync(PackageRequest request, CancellationToken ct = default)
    {
        var draft = Apply(request, new PackageDraft(
            string.Empty, string.Empty, 0, [], [], 0, string.Empty, [], [], [], [], Featured: false, Active: true, []));
        await ValidateAsync(draft, ct);

        var package = new Package();
        CopyScalars(draft, package);
        package.Destinations.AddRange(NewRoute(draft));
        package.ItineraryDays.AddRange(NewItinerary(draft));
        package.Interests.AddRange(NewInterests(draft));
        package.Images.AddRange(NewImages(draft));
        package.Items.AddRange(NewItems(draft));

        await _writer.AddAsync(package, draft.Name, ct);

        // Read it back so the route carries the destination names.
        return ToDto(await _writer.GetAsync(package.Id, ct));
    }

    public async Task<PackageDto> UpdateAsync(string id, PackageRequest request, CancellationToken ct = default)
    {
        var package = await _writer.GetAsync(id, ct);
        var draft = Apply(request, ToDraft(package));
        await ValidateAsync(draft, ct);

        CopyScalars(draft, package);

        // Replacing a child list is a delete followed by inserts that reuse keys (the route position, the day number,
        // the interest). The deletes are saved first, in the same transaction, so a unique key never clashes with itself.
        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var replaced = false;
            replaced |= Replace(package.Destinations, Route(package), draft.DestinationIds);
            replaced |= Replace(package.ItineraryDays, Itinerary(package).Select(d => (d.Title, d.Description)).ToList(), draft.Itinerary.Select(d => (d.Title, d.Description)).ToList());
            replaced |= Replace(package.Interests, Interests(package), draft.Interests);
            replaced |= Replace(package.Images, Gallery(package), draft.Gallery);
            replaced |= ReplaceItems(package, draft);

            if (replaced)
            {
                await _unitOfWork.SaveChangesAsync(token);
                AddMissingChildren(package, draft);
            }

            await _unitOfWork.SaveChangesAsync(token);
        }, ct);

        return ToDto(await _writer.GetAsync(id, ct));
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) => _writer.DeleteAsync(id, ct);

    public async Task<IReadOnlyList<PackageDto>> ReorderAsync(ReorderRequest request, CancellationToken ct = default)
        => [.. (await _writer.ReorderAsync(request, ct)).Select(ToDto)];

    // ---------------------------------------------------------------- validation and merge

    private async Task ValidateAsync(PackageDraft draft, CancellationToken ct)
    {
        var fields = _validator.Check(draft);

        if (!fields.ContainsKey("destinationIds") && draft.DestinationIds.Count > 0)
        {
            var existing = await _packages.ExistingDestinationIdsAsync(draft.DestinationIds.ToList(), ct);
            var unknown = draft.DestinationIds.Where(id => !existing.Contains(id)).ToList();
            if (unknown.Count > 0)
            {
                fields["destinationIds"] = $"Unknown destinations: {string.Join(", ", unknown)}";
            }
        }

        if (fields.Count > 0)
        {
            throw new ValidationException(fields);
        }
    }

    private static PackageDraft Apply(PackageRequest request, PackageDraft stored) => new(
        Merge.Text(request.Name, stored.Name),
        Merge.Text(request.Tagline, stored.Tagline),
        request.DurationDays ?? stored.DurationDays,
        Merge.Lines(request.Highlights, stored.Highlights),
        Merge.Lines(request.Interests, stored.Interests).Select(i => i.ToLowerInvariant()).Distinct().ToList(),
        request.PriceLkr ?? stored.PriceLkr,
        Merge.Text(request.Image, stored.Image),
        Merge.Lines(request.Gallery, stored.Gallery),
        request.Itinerary is null
            ? stored.Itinerary
            : [.. request.Itinerary.Select(d => new ItineraryDayDraft(Merge.Text(d?.Title, string.Empty), Merge.Text(d?.Description, string.Empty)))],
        Merge.Lines(request.Included, stored.Included),
        Merge.Lines(request.Excluded, stored.Excluded),
        request.Featured ?? stored.Featured,
        request.Active ?? stored.Active,
        Merge.Lines(request.DestinationIds, stored.DestinationIds));

    private static PackageDraft ToDraft(Package p) => new(
        p.Name,
        p.Tagline,
        p.DurationDays,
        Items(p, PackageItemKind.Highlight),
        Interests(p),
        p.PriceLkr,
        p.Image,
        Gallery(p),
        [.. Itinerary(p).Select(d => new ItineraryDayDraft(d.Title, d.Description))],
        Items(p, PackageItemKind.Included),
        Items(p, PackageItemKind.Excluded),
        p.Featured,
        p.Active,
        Route(p));

    // ---------------------------------------------------------------- child rows

    private static void CopyScalars(PackageDraft draft, Package package)
    {
        package.Name = draft.Name;
        package.Tagline = draft.Tagline;
        package.DurationDays = draft.DurationDays;
        package.PriceLkr = draft.PriceLkr;
        package.Image = draft.Image;
        package.Featured = draft.Featured;
        package.Active = draft.Active;
    }

    private static IEnumerable<PackageDestination> NewRoute(PackageDraft draft)
        => draft.DestinationIds.Select((destinationId, position) => new PackageDestination { DestinationId = destinationId, Position = position });

    private static IEnumerable<ItineraryDay> NewItinerary(PackageDraft draft)
        => draft.Itinerary.Select((day, index) => new ItineraryDay { Day = index + 1, Title = day.Title, Description = day.Description });

    private static IEnumerable<PackageInterest> NewInterests(PackageDraft draft)
        => draft.Interests.Select(interest => new PackageInterest { Interest = EnumWire.FromWire<InterestType>(interest) });

    private static IEnumerable<PackageImage> NewImages(PackageDraft draft)
        => draft.Gallery.Select((src, position) => new PackageImage { Src = src, SortOrder = position });

    private static IEnumerable<PackageItem> NewItems(PackageDraft draft)
        => NewItems(PackageItemKind.Highlight, draft.Highlights)
            .Concat(NewItems(PackageItemKind.Included, draft.Included))
            .Concat(NewItems(PackageItemKind.Excluded, draft.Excluded));

    private static IEnumerable<PackageItem> NewItems(PackageItemKind kind, IReadOnlyList<string> lines)
        => lines.Select((content, position) => new PackageItem { Kind = kind, Content = content, SortOrder = position });

    /// <summary>Clears <paramref name="current"/> when it differs from what the draft wants. Returns whether it did.</summary>
    private static bool Replace<TRow, TValue>(List<TRow> current, IReadOnlyList<TValue> stored, IReadOnlyList<TValue> wanted)
    {
        if (stored.SequenceEqual(wanted))
        {
            return false;
        }

        current.Clear();
        return true;
    }

    private static bool ReplaceItems(Package package, PackageDraft draft)
    {
        var same = Items(package, PackageItemKind.Highlight).SequenceEqual(draft.Highlights)
            && Items(package, PackageItemKind.Included).SequenceEqual(draft.Included)
            && Items(package, PackageItemKind.Excluded).SequenceEqual(draft.Excluded);
        if (same)
        {
            return false;
        }

        package.Items.Clear();
        return true;
    }

    /// <summary>Adds the rows of every child list that was just emptied.</summary>
    private static void AddMissingChildren(Package package, PackageDraft draft)
    {
        if (package.Destinations.Count == 0)
        {
            package.Destinations.AddRange(NewRoute(draft));
        }

        if (package.ItineraryDays.Count == 0)
        {
            package.ItineraryDays.AddRange(NewItinerary(draft));
        }

        if (package.Interests.Count == 0)
        {
            package.Interests.AddRange(NewInterests(draft));
        }

        if (package.Images.Count == 0)
        {
            package.Images.AddRange(NewImages(draft));
        }

        if (package.Items.Count == 0)
        {
            package.Items.AddRange(NewItems(draft));
        }
    }

    // ---------------------------------------------------------------- output

    private static List<string> Route(Package p) => [.. p.Destinations.OrderBy(d => d.Position).Select(d => d.DestinationId)];

    private static List<ItineraryDay> Itinerary(Package p) => [.. p.ItineraryDays.OrderBy(d => d.Day)];

    private static List<string> Interests(Package p) => [.. p.Interests.Select(i => i.Interest).Order().Select(EnumWire.ToWire)];

    private static List<string> Gallery(Package p) => [.. p.Images.OrderBy(i => i.SortOrder).Select(i => i.Src)];

    private static List<string> Items(Package p, PackageItemKind kind)
        => [.. p.Items.Where(i => i.Kind == kind).OrderBy(i => i.SortOrder).Select(i => i.Content)];

    private static PackageDto ToDto(Package p) => new(
        p.Id,
        p.Name,
        p.Tagline,
        p.DurationDays,
        [.. p.Destinations.OrderBy(d => d.Position).Select(d => d.Destination?.Name ?? d.DestinationId)],
        Items(p, PackageItemKind.Highlight),
        Interests(p),
        p.PriceLkr,
        p.Image,
        Gallery(p),
        [.. Itinerary(p).Select((d, index) => new ItineraryDayDto(index + 1, d.Title, d.Description))],
        Items(p, PackageItemKind.Included),
        Items(p, PackageItemKind.Excluded),
        p.Featured,
        p.Active,
        Route(p));
}
