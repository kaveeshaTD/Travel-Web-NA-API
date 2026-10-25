using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Application.Content;

public sealed record ServiceRequest(
    string? Title = null,
    string? Description = null,
    IReadOnlyList<string>? Details = null,
    string? Icon = null,
    string? Image = null);

public sealed record ServiceDraft(string Title, string Description, IReadOnlyList<string> Details, string Icon, string Image);

public sealed record ServiceDto(string Id, string Title, string Description, IReadOnlyList<string> Details, string Icon, string Image) : IContentDto;

public sealed class ServiceDraftValidator : AbstractValidator<ServiceDraft>
{
    /// <summary>Icon names the website knows how to draw.</summary>
    public static readonly IReadOnlyList<string> Icons = ["plane", "sun", "route", "compass", "car", "users", "pin", "calendar", "star", "check"];

    public static readonly string IconMessage = $"Icon must be one of: {string.Join(", ", Icons)}";

    public ServiceDraftValidator()
    {
        RuleFor(x => x.Title).MinLength(2, "Enter a service title");
        RuleFor(x => x.Title).MaxLength(120, "The service title is too long");
        RuleFor(x => x.Description).MinLength(10, "Add a short description");
        RuleFor(x => x.Description).MaxLength(4000, "The description is too long");
        RuleFor(x => x.Details).Lines("Use at most 50 details of up to 300 characters each");
        RuleFor(x => x.Icon).Must(icon => Icons.Contains(icon)).WithMessage(IconMessage);
        RuleFor(x => x.Image).Image("Choose an image");
    }
}

public sealed class ServiceOfferingService : IContentService<ServiceRequest, ServiceDto>
{
    private readonly ContentWriter<Service> _writer;
    private readonly IContentRepository<Service> _services;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<ServiceDraft> _validator;

    public ServiceOfferingService(IContentRepository<Service> services, IUnitOfWork unitOfWork, IValidator<ServiceDraft> validator)
    {
        _services = services;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _writer = new ContentWriter<Service>(services, unitOfWork, "service");
    }

    public async Task<IReadOnlyList<ServiceDto>> ListAsync(bool includeUnpublished, CancellationToken ct = default)
        => [.. (await _services.ListAsync(ct)).Select(ToDto)];

    public async Task<ServiceDto> GetAsync(string id, CancellationToken ct = default) => ToDto(await _writer.GetAsync(id, ct));

    public async Task<ServiceDto> CreateAsync(ServiceRequest request, CancellationToken ct = default)
    {
        var draft = Apply(request, new ServiceDraft(string.Empty, string.Empty, [], string.Empty, string.Empty));
        _validator.ThrowIfInvalid(draft);

        var service = new Service();
        Copy(draft, service);
        await _writer.AddAsync(service, draft.Title, ct);
        return ToDto(service);
    }

    public async Task<ServiceDto> UpdateAsync(string id, ServiceRequest request, CancellationToken ct = default)
    {
        var service = await _writer.GetAsync(id, ct);
        var draft = Apply(request, new ServiceDraft(service.Title, service.Description, Details(service), service.Icon, service.Image));
        _validator.ThrowIfInvalid(draft);

        Copy(draft, service);
        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto(service);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) => _writer.DeleteAsync(id, ct);

    public async Task<IReadOnlyList<ServiceDto>> ReorderAsync(ReorderRequest request, CancellationToken ct = default)
        => [.. (await _writer.ReorderAsync(request, ct)).Select(ToDto)];

    private static ServiceDraft Apply(ServiceRequest request, ServiceDraft stored) => new(
        Merge.Text(request.Title, stored.Title),
        Merge.Text(request.Description, stored.Description),
        Merge.Lines(request.Details, stored.Details),
        Merge.Text(request.Icon, stored.Icon),
        Merge.Text(request.Image, stored.Image));

    private static void Copy(ServiceDraft draft, Service service)
    {
        service.Title = draft.Title;
        service.Description = draft.Description;
        service.Icon = draft.Icon;
        service.Image = draft.Image;

        // The details are small and always sent as a whole, so they are replaced rather than diffed.
        service.Details.Clear();
        for (var position = 0; position < draft.Details.Count; position++)
        {
            service.Details.Add(new ServiceDetail { Detail = draft.Details[position], SortOrder = position });
        }
    }

    private static List<string> Details(Service service) => [.. service.Details.OrderBy(d => d.SortOrder).Select(d => d.Detail)];

    private static ServiceDto ToDto(Service s) => new(s.Id, s.Title, s.Description, Details(s), s.Icon, s.Image);
}
