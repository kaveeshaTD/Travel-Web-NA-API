using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Entities;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Content;

public sealed record VehicleRequest(
    string? Type = null,
    string? Name = null,
    string? Description = null,
    string? Image = null,
    int? Seats = null,
    int? Luggage = null,
    bool? Ac = null,
    decimal? PricePerKmLkr = null,
    decimal? PricePerDayLkr = null);

public sealed record VehicleDraft(
    string Type,
    string Name,
    string Description,
    string Image,
    int Seats,
    int Luggage,
    bool Ac,
    decimal PricePerKmLkr,
    decimal PricePerDayLkr);

public sealed record VehicleDto(
    string Id,
    string Type,
    string Name,
    string Description,
    string Image,
    int Seats,
    int Luggage,
    bool Ac,
    decimal PricePerKmLkr,
    decimal PricePerDayLkr) : IContentDto;

public sealed class VehicleDraftValidator : AbstractValidator<VehicleDraft>
{
    public static readonly string TypeMessage = $"Type must be one of: {string.Join(", ", Enum.GetValues<VehicleType>().Select(EnumWire.ToWire))}";

    public VehicleDraftValidator()
    {
        RuleFor(x => x.Type).Must(type => EnumWire.TryFromWire<VehicleType>(type, out _)).WithMessage(TypeMessage);
        RuleFor(x => x.Name).MinLength(2, "Enter a vehicle name");
        RuleFor(x => x.Name).MaxLength(120, "The vehicle name is too long");
        RuleFor(x => x.Description).MaxLength(4000, "The description is too long");
        RuleFor(x => x.Image).Image("Choose a photo");
        RuleFor(x => x.Seats).InclusiveBetween(1, 100).WithMessage("Seats must be a whole number of 1 or more");
        RuleFor(x => x.Luggage).InclusiveBetween(0, 100).WithMessage("Luggage must be a whole number of 0 or more");
        RuleFor(x => x.PricePerKmLkr).Must(p => ContentRules.IsMoney(p)).WithMessage("Enter the price per km");
        RuleFor(x => x.PricePerDayLkr).Must(p => ContentRules.IsMoney(p) && p > 0).WithMessage("Enter the price per day");
    }
}

public sealed class VehicleService : IContentService<VehicleRequest, VehicleDto>
{
    private readonly ContentWriter<Vehicle> _writer;
    private readonly IContentRepository<Vehicle> _vehicles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<VehicleDraft> _validator;

    public VehicleService(IContentRepository<Vehicle> vehicles, IUnitOfWork unitOfWork, IValidator<VehicleDraft> validator)
    {
        _vehicles = vehicles;
        _unitOfWork = unitOfWork;
        _validator = validator;
        _writer = new ContentWriter<Vehicle>(vehicles, unitOfWork, "vehicle");
    }

    public async Task<IReadOnlyList<VehicleDto>> ListAsync(bool includeUnpublished, CancellationToken ct = default)
        => [.. (await _vehicles.ListAsync(ct)).Select(ToDto)];

    public async Task<VehicleDto> GetAsync(string id, CancellationToken ct = default) => ToDto(await _writer.GetAsync(id, ct));

    public async Task<VehicleDto> CreateAsync(VehicleRequest request, CancellationToken ct = default)
    {
        var draft = Apply(request, new VehicleDraft(string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, Ac: true, 0, 0));
        _validator.ThrowIfInvalid(draft);

        var vehicle = new Vehicle();
        Copy(draft, vehicle);
        await _writer.AddAsync(vehicle, draft.Name, ct);
        return ToDto(vehicle);
    }

    public async Task<VehicleDto> UpdateAsync(string id, VehicleRequest request, CancellationToken ct = default)
    {
        var vehicle = await _writer.GetAsync(id, ct);
        var draft = Apply(request, ToDraft(vehicle));
        _validator.ThrowIfInvalid(draft);

        Copy(draft, vehicle);
        await _unitOfWork.SaveChangesAsync(ct);
        return ToDto(vehicle);
    }

    public Task DeleteAsync(string id, CancellationToken ct = default) => _writer.DeleteAsync(id, ct);

    public async Task<IReadOnlyList<VehicleDto>> ReorderAsync(ReorderRequest request, CancellationToken ct = default)
        => [.. (await _writer.ReorderAsync(request, ct)).Select(ToDto)];

    private static VehicleDraft Apply(VehicleRequest request, VehicleDraft stored) => new(
        Merge.Text(request.Type, stored.Type).ToLowerInvariant(),
        Merge.Text(request.Name, stored.Name),
        Merge.Text(request.Description, stored.Description),
        Merge.Text(request.Image, stored.Image),
        request.Seats ?? stored.Seats,
        request.Luggage ?? stored.Luggage,
        request.Ac ?? stored.Ac,
        request.PricePerKmLkr ?? stored.PricePerKmLkr,
        request.PricePerDayLkr ?? stored.PricePerDayLkr);

    private static VehicleDraft ToDraft(Vehicle v) => new(
        EnumWire.ToWire(v.Type), v.Name, v.Description, v.Image, v.Seats, v.Luggage, v.Ac, v.PricePerKmLkr, v.PricePerDayLkr);

    private static void Copy(VehicleDraft draft, Vehicle vehicle)
    {
        vehicle.Type = EnumWire.FromWire<VehicleType>(draft.Type);
        vehicle.Name = draft.Name;
        vehicle.Description = draft.Description;
        vehicle.Image = draft.Image;
        vehicle.Seats = draft.Seats;
        vehicle.Luggage = draft.Luggage;
        vehicle.Ac = draft.Ac;
        vehicle.PricePerKmLkr = draft.PricePerKmLkr;
        vehicle.PricePerDayLkr = draft.PricePerDayLkr;
    }

    private static VehicleDto ToDto(Vehicle v) => new(
        v.Id, EnumWire.ToWire(v.Type), v.Name, v.Description, v.Image, v.Seats, v.Luggage, v.Ac, v.PricePerKmLkr, v.PricePerDayLkr);
}
