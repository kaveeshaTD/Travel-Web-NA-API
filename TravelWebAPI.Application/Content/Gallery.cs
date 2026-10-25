using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Application.Content;

public sealed record GalleryImageRequest(string? Src = null, string? Alt = null, string? Caption = null, decimal? Aspect = null);

public sealed record GalleryImageDraft(string Src, string Alt, string Caption, decimal Aspect);

/// <summary><c>Aspect</c> is width divided by height.</summary>
public sealed record GalleryImageDto(string Id, string Src, string Alt, string Caption, decimal Aspect) : IContentDto;

public sealed class GalleryImageDraftValidator : AbstractValidator<GalleryImageDraft>
{
    public const decimal DefaultAspect = 1.5m;

    public GalleryImageDraftValidator()
    {
        RuleFor(x => x.Src).Image("Choose an image");
        RuleFor(x => x.Alt).MinLength(3, "Describe the image for accessibility");
        RuleFor(x => x.Alt).MaxLength(250, "The description is too long");
        RuleFor(x => x.Caption).MinLength(1, "Enter a caption");
        RuleFor(x => x.Caption).MaxLength(250, "The caption is too long");
        RuleFor(x => x.Aspect).Must(a => a is >= 0.3m and <= 3.5m).WithMessage("Aspect ratio must be between 0.3 and 3.5");
    }
}

public sealed class GalleryService : IContentService<GalleryImageRequest, GalleryImageDto>
{
    private readonly ContentWriter<GalleryImage> _writer;
    private readonly IContentRepository<GalleryImage> _images;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<GalleryImageDraft> _validator;

    public GalleryService(IContentRepository<GalleryImage> images, IUnitOfWork unitOfWork, IValidator<GalleryImageDraft> validator)
    {
        _images = images;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _writer = new ContentWriter<GalleryImage>(images, unitOfWork, "gallery image");
    }

    public async Task<IReadOnlyList<GalleryImageDto>> ListAsync(bool includeUnpublished, CancellationToken ct = default)
        => [.. (await _images.ListAsync(ct)).Select(ToDto)];

    public async Task<GalleryImageDto> GetAsync(string id, CancellationToken ct = default) => ToDto(await _writer.GetAsync(id, ct));

    public async Task<GalleryImageDto> CreateAsync(GalleryImageRequest request, CancellationToken ct = default)
    {
        var draft = Apply(request, new GalleryImageDraft(string.Empty, string.Empty, string.Empty, GalleryImageDraftValidator.DefaultAspect));
        _validator.ThrowIfInvalid(draft);

        var image = new GalleryImage();
        Copy(draft, image);
        await _writer.AddAsync(image, draft.Caption, ct);
        return ToDto(image);
    }

    public async Task<GalleryImageDto> UpdateAsync(string id, GalleryImageRequest request, CancellationToken ct = default)
    {
        var image = await _writer.GetAsync(id, ct);
        var draft = Apply(request, new GalleryImageDraft(image.Src, image.Alt, image.Caption, image.Aspect));
        _validator.ThrowIfInvalid(draft);

        Copy(draft, image);
        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto(image);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) => _writer.DeleteAsync(id, ct);

    public async Task<IReadOnlyList<GalleryImageDto>> ReorderAsync(ReorderRequest request, CancellationToken ct = default)
        => [.. (await _writer.ReorderAsync(request, ct)).Select(ToDto)];

    private static GalleryImageDraft Apply(GalleryImageRequest request, GalleryImageDraft stored) => new(
        Merge.Text(request.Src, stored.Src),
        Merge.Text(request.Alt, stored.Alt),
        Merge.Text(request.Caption, stored.Caption),
        request.Aspect ?? stored.Aspect);

    private static void Copy(GalleryImageDraft draft, GalleryImage image)
    {
        image.Src = draft.Src;
        image.Alt = draft.Alt;
        image.Caption = draft.Caption;
        image.Aspect = decimal.Round(draft.Aspect, 3);
    }

    private static GalleryImageDto ToDto(GalleryImage image) => new(image.Id, image.Src, image.Alt, image.Caption, image.Aspect);
}
