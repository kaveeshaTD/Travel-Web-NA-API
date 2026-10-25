using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Domain.Entities;
using ValidationException = TravelWebAPI.Domain.Exceptions.ValidationException;

namespace TravelWebAPI.Application.Content;

public sealed record TipMonthRequest(string? Suitability = null, string? Note = null);

public sealed record TipRegionRequest(string? Id = null, string? Region = null, string? Summary = null, IReadOnlyList<TipMonthRequest>? Months = null);

public sealed record TipLinkRequest(string? Href = null, string? Label = null);

public sealed record TipInfoRequest(string? Id = null, string? Title = null, string? Body = null, TipLinkRequest? Link = null);

/// <summary>The body of <c>PUT /api/tips</c>. Both lists are required; the whole document is replaced.</summary>
public sealed record TipsRequest(IReadOnlyList<TipRegionRequest>? Regions = null, IReadOnlyList<TipInfoRequest>? Info = null);

public sealed record TipMonthDto(string Suitability, string Note);

public sealed record TipRegionDto(string Id, string Region, string Summary, IReadOnlyList<TipMonthDto> Months);

public sealed record TipLinkDto(string Href, string Label);

public sealed record TipInfoDto(
    string Id,
    string Title,
    string Body,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TipLinkDto? Link);

/// <summary>Travel tips: when to go where (12 months per region) and practical information.</summary>
public sealed record TipsDto(IReadOnlyList<TipRegionDto> Regions, IReadOnlyList<TipInfoDto> Info);

public sealed class TipsDtoValidator : AbstractValidator<TipsDto>
{
    public static readonly IReadOnlyList<string> Suitabilities = ["great", "good", "fair", "poor"];

    public const int MaxRegions = 50;
    public const int MaxInfo = 100;

    public TipsDtoValidator()
    {
        RuleFor(x => x.Regions).Must(regions => regions.Count <= MaxRegions).WithMessage("Use at most 50 regions");
        RuleFor(x => x.Regions).Must(regions => regions.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() == regions.Count)
            .WithMessage("Region ids must be unique");
        RuleForEach(x => x.Regions).ChildRules(region =>
        {
            region.RuleFor(r => r.Id).MinLength(1, "Every region needs an id");
            region.RuleFor(r => r.Id).MaxLength(60, "The region id is too long");
            region.RuleFor(r => r.Region).MinLength(1, "Enter the region name");
            region.RuleFor(r => r.Region).MaxLength(120, "The region name is too long");
            region.RuleFor(r => r.Summary).MaxLength(1000, "The summary is too long");
            region.RuleFor(r => r.Months).Must(months => months.Count == 12).WithMessage("Every region needs exactly 12 months, January to December");
            region.RuleFor(r => r.Months)
                .Must(months => months.All(m => Suitabilities.Contains(m.Suitability)))
                .WithMessage($"Suitability must be one of: {string.Join(", ", Suitabilities)}");
            region.RuleFor(r => r.Months).Must(months => months.All(m => m.Note.Length <= 200)).WithMessage("Month notes can be up to 200 characters");
        });

        RuleFor(x => x.Info).Must(info => info.Count <= MaxInfo).WithMessage("Use at most 100 information items");
        RuleFor(x => x.Info).Must(info => info.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() == info.Count)
            .WithMessage("Information ids must be unique");
        RuleForEach(x => x.Info).ChildRules(item =>
        {
            item.RuleFor(i => i.Id).MinLength(1, "Every item needs an id");
            item.RuleFor(i => i.Id).MaxLength(60, "The item id is too long");
            item.RuleFor(i => i.Title).MinLength(1, "Enter a title");
            item.RuleFor(i => i.Title).MaxLength(120, "The title is too long");
            item.RuleFor(i => i.Body).MinLength(1, "Enter the text");
            item.RuleFor(i => i.Body).MaxLength(4000, "The text is too long");
            item.RuleFor(i => i.Link).Must(link => link is null || IsWebLink(link.Href) && link.Label.Length is > 0 and <= 120)
                .WithMessage("A link needs a label and a full address starting with https:// or http://");
        });
    }

    private static bool IsWebLink(string href)
        => href.Length <= ImageReference.MaxLength
            && Uri.TryCreate(href, UriKind.Absolute, out var uri)
            && uri.Scheme is "https" or "http"
            && !string.IsNullOrEmpty(uri.Host)
            && href.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c));
}

/// <summary>The single travel-tips document: read by everybody, replaced by an admin. Stored as two JSON columns in a fixed shape.</summary>
public sealed class TipsService(IRepository<TravelTips> tips, IUnitOfWork unitOfWork, IValidator<TipsDto> validator)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<TipsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await tips.GetByIdAsync(TravelTips.SingletonId, cancellationToken);
        return row is null
            ? new TipsDto([], [])
            : new TipsDto(
                JsonSerializer.Deserialize<List<TipRegionDto>>(row.RegionsJson, Json) ?? [],
                JsonSerializer.Deserialize<List<TipInfoDto>>(row.InfoJson, Json) ?? []);
    }

    public async Task<TipsDto> UpdateAsync(TipsRequest request, CancellationToken cancellationToken = default)
    {
        var missing = new Dictionary<string, string>();
        if (request.Regions is null)
        {
            missing["regions"] = "The regions list is required";
        }

        if (request.Info is null)
        {
            missing["info"] = "The info list is required";
        }

        if (missing.Count > 0)
        {
            throw new ValidationException(missing);
        }

        var draft = ToDraft(request);
        validator.ThrowIfInvalid(draft);

        var row = await tips.GetByIdAsync(TravelTips.SingletonId, cancellationToken);
        if (row is null)
        {
            row = new TravelTips();
            await tips.AddAsync(row, cancellationToken);
        }

        row.RegionsJson = JsonSerializer.Serialize(draft.Regions, Json);
        row.InfoJson = JsonSerializer.Serialize(draft.Info, Json);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return draft;
    }

    private static TipsDto ToDraft(TipsRequest request) => new(
        [.. request.Regions!.Select(region => new TipRegionDto(
            Merge.Text(region?.Id, string.Empty),
            Merge.Text(region?.Region, string.Empty),
            Merge.Text(region?.Summary, string.Empty),
            [.. (region?.Months ?? []).Select(month => new TipMonthDto(
                Merge.Text(month?.Suitability, string.Empty).ToLowerInvariant(),
                Merge.Text(month?.Note, string.Empty)))]))],
        [.. request.Info!.Select(item => new TipInfoDto(
            Merge.Text(item?.Id, string.Empty),
            Merge.Text(item?.Title, string.Empty),
            Merge.Text(item?.Body, string.Empty),
            item?.Link is null ? null : new TipLinkDto(Merge.Text(item.Link.Href, string.Empty), Merge.Text(item.Link.Label, string.Empty))))]);
}
