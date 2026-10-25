using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Application.Content;

public sealed record DestinationRequest(
    string? Name = null,
    string? Region = null,
    decimal? Lat = null,
    decimal? Lng = null,
    string? Description = null,
    string? BestTime = null,
    string? TravelTimeFromAirport = null,
    IReadOnlyList<string>? Images = null,
    bool? Featured = null);

public sealed record DestinationDraft(
    string Name,
    string Region,
    decimal Lat,
    decimal Lng,
    string Description,
    string BestTime,
    string TravelTimeFromAirport,
    IReadOnlyList<string> Images,
    bool Featured);

public sealed record DestinationDto(
    string Id,
    string Name,
    string Region,
    decimal Lat,
    decimal Lng,
    string Description,
    string BestTime,
    string TravelTimeFromAirport,
    IReadOnlyList<string> Images,
    bool Featured) : IContentDto;

public sealed class DestinationDraftValidator : AbstractValidator<DestinationDraft>
{
    public DestinationDraftValidator()
    {
        RuleFor(x => x.Name).MinLength(2, "Enter a destination name");
        RuleFor(x => x.Name).MaxLength(120, "The destination name is too long");
        RuleFor(x => x.Region).MinLength(1, "Enter a region");
        RuleFor(x => x.Region).MaxLength(80, "The region is too long");
        RuleFor(x => x.Lat).Must(lat => lat is >= 5 and <= 10).WithMessage("Latitude must be between 5 and 10 (Sri Lanka)");
        RuleFor(x => x.Lng).Must(lng => lng is >= 79 and <= 82.5m).WithMessage("Longitude must be between 79 and 82.5 (Sri Lanka)");
        RuleFor(x => x.Description).MinLength(10, "Add a short description");
        RuleFor(x => x.Description).MaxLength(4000, "The description is too long");
        RuleFor(x => x.BestTime).MaxLength(120, "Best time is too long");
        RuleFor(x => x.TravelTimeFromAirport).MaxLength(80, "Travel time is too long");
        RuleFor(x => x.Images).Must(images => images.Count > 0).WithMessage("Add at least one image");
        RuleFor(x => x.Images).Images("Every image must be a link, an uploaded file or a website photo");
    }
}

public sealed class DestinationService : IContentService<DestinationRequest, DestinationDto>
{
    private readonly ContentWriter<Destination> _writer;
    private readonly IContentRepository<Destination> _destinations;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<DestinationDraft> _validator;

    public DestinationService(IContentRepository<Destination> destinations, IUnitOfWork unitOfWork, IValidator<DestinationDraft> validator)
    {
        _destinations = destinations;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _writer = new ContentWriter<Destination>(destinations, unitOfWork, "destination");
    }

    public async Task<IReadOnlyList<DestinationDto>> ListAsync(bool includeUnpublished, CancellationToken ct = default)
        => [.. (await _destinations.ListAsync(ct)).Select(ToDto)];

    public async Task<DestinationDto> GetAsync(string id, CancellationToken ct = default) => ToDto(await _writer.GetAsync(id, ct));

    public async Task<DestinationDto> CreateAsync(DestinationRequest request, CancellationToken ct = default)
    {
        var draft = Apply(request, new DestinationDraft(string.Empty, string.Empty, 0, 0, string.Empty, string.Empty, string.Empty, [], Featured: false));
        _validator.ThrowIfInvalid(draft);

        var destination = new Destination();
        Copy(draft, destination);
        await _writer.AddAsync(destination, draft.Name, ct);
        return ToDto(destination);
    }

    public async Task<DestinationDto> UpdateAsync(string id, DestinationRequest request, CancellationToken ct = default)
    {
        var destination = await _writer.GetAsync(id, ct);
        var draft = Apply(request, ToDraft(destination));
        _validator.ThrowIfInvalid(draft);

        Copy(draft, destination);
        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto(destination);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) => _writer.DeleteAsync(id, ct);

    public async Task<IReadOnlyList<DestinationDto>> ReorderAsync(ReorderRequest request, CancellationToken ct = default)
        => [.. (await _writer.ReorderAsync(request, ct)).Select(ToDto)];

    private static DestinationDraft Apply(DestinationRequest request, DestinationDraft stored) => new(
        Merge.Text(request.Name, stored.Name),
        Merge.Text(request.Region, stored.Region),
        request.Lat ?? stored.Lat,
        request.Lng ?? stored.Lng,
        Merge.Text(request.Description, stored.Description),
        Merge.Text(request.BestTime, stored.BestTime),
        Merge.Text(request.TravelTimeFromAirport, stored.TravelTimeFromAirport),
        Merge.Lines(request.Images, stored.Images),
        request.Featured ?? stored.Featured);

    private static DestinationDraft ToDraft(Destination d) => new(
        d.Name, d.Region, d.Lat, d.Lng, d.Description, d.BestTime, d.TravelTimeFromAirport, Images(d), d.Featured);

    private static void Copy(DestinationDraft draft, Destination destination)
    {
        destination.Name = draft.Name;
        destination.Region = draft.Region;
        destination.Lat = decimal.Round(draft.Lat, 6);
        destination.Lng = decimal.Round(draft.Lng, 6);
        destination.Description = draft.Description;
        destination.BestTime = draft.BestTime;
        destination.TravelTimeFromAirport = draft.TravelTimeFromAirport;
        destination.Featured = draft.Featured;

        destination.Images.Clear();
        for (var position = 0; position < draft.Images.Count; position++)
        {
            destination.Images.Add(new DestinationImage { Src = draft.Images[position], SortOrder = position });
        }
    }

    private static List<string> Images(Destination d) => [.. d.Images.OrderBy(i => i.SortOrder).Select(i => i.Src)];

    private static DestinationDto ToDto(Destination d) => new(
        d.Id, d.Name, d.Region, d.Lat, d.Lng, d.Description, d.BestTime, d.TravelTimeFromAirport, Images(d), d.Featured);
}
