using System.Text.RegularExpressions;
using FluentValidation;
using TravelWebAPI.Application.Abstractions;
using TravelWebAPI.Application.Auth.Validators;
using TravelWebAPI.Domain.Entities;

namespace TravelWebAPI.Application.Content;

public sealed record SocialLinkRequest(string? Label = null, string? Href = null);

public sealed record TrustRequest(int? YearsExperience = null, int? TripsCompleted = null, decimal? Rating = null, string? Licensed = null);

/// <summary>The body of <c>PUT /api/settings</c>. The whole document is replaced, so what is not sent becomes empty.</summary>
public sealed record SettingsRequest(
    string? Name = null,
    string? Tagline = null,
    string? DriverName = null,
    string? DriverRole = null,
    string? Phone = null,
    string? WhatsappNumber = null,
    string? Email = null,
    string? Address = null,
    IReadOnlyList<SocialLinkRequest>? Socials = null,
    TrustRequest? Trust = null,
    IReadOnlyList<string>? Languages = null,
    IReadOnlyList<string>? Licenses = null);

public sealed record SocialLinkDto(string Label, string Href);

public sealed record TrustDto(int YearsExperience, int TripsCompleted, decimal Rating, string Licensed);

/// <summary>The site settings as the website and the admin see them.</summary>
public sealed record SettingsDto(
    string Name,
    string Tagline,
    string DriverName,
    string DriverRole,
    string Phone,
    string WhatsappNumber,
    string Email,
    string Address,
    IReadOnlyList<SocialLinkDto> Socials,
    TrustDto Trust,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Licenses);

public sealed partial class SettingsDtoValidator : AbstractValidator<SettingsDto>
{
    public const int MaxListEntries = 50;
    public const int MaxEntryLength = 120;

    public SettingsDtoValidator()
    {
        RuleFor(x => x.Name).MinLength(2, "Enter the business name");
        RuleFor(x => x.Name).MaxLength(120, "The business name is too long");
        RuleFor(x => x.Tagline).MaxLength(250, "The tagline is too long");
        RuleFor(x => x.DriverName).MinLength(2, "Enter the owner name");
        RuleFor(x => x.DriverName).MaxLength(120, "The owner name is too long");
        RuleFor(x => x.DriverRole).MaxLength(120, "The role is too long");
        RuleFor(x => x.Phone).MinLength(6, "Enter a phone number");
        RuleFor(x => x.Phone).MaxLength(40, "The phone number is too long");
        RuleFor(x => x.WhatsappNumber).Must(number => WhatsappPattern().IsMatch(number))
            .WithMessage("WhatsApp number must be digits only, with country code (e.g. 94771234567)");
        RuleFor(x => x.Email).ValidEmail();
        RuleFor(x => x.Address).MaxLength(250, "The address is too long");

        RuleFor(x => x.Trust.YearsExperience).GreaterThanOrEqualTo(0).WithMessage("Enter a whole number of 0 or more");
        RuleFor(x => x.Trust.TripsCompleted).GreaterThanOrEqualTo(0).WithMessage("Enter a whole number of 0 or more");
        RuleFor(x => x.Trust.Rating).InclusiveBetween(0m, 5m).WithMessage("Rating must be between 0 and 5");
        RuleFor(x => x.Trust.Licensed).MaxLength(120, "The licence text is too long");

        RuleFor(x => x.Socials)
            .Must(links => links.All(link => link.Label.Length <= 60 && IsSecureLink(link.Href)))
            .WithMessage("Social links must start with https://");
        RuleFor(x => x.Socials).Must(links => links.Count <= 20).WithMessage("Use at most 20 social links");

        RuleFor(x => x.Languages).Must(AreShortEntries).WithMessage("Use at most 50 entries of up to 120 characters each");
        RuleFor(x => x.Licenses).Must(AreShortEntries).WithMessage("Use at most 50 entries of up to 120 characters each");
    }

    private static bool AreShortEntries(IReadOnlyList<string> entries)
        => entries.Count <= MaxListEntries && entries.All(entry => entry.Length <= MaxEntryLength);

    private static bool IsSecureLink(string href)
        => href.Length <= ImageReference.MaxLength
            && Uri.TryCreate(href, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrEmpty(uri.Host)
            && href.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c));

    [GeneratedRegex(@"^\d{8,15}$")]
    private static partial Regex WhatsappPattern();
}

/// <summary>The single site-settings document: read by everybody, replaced by an admin.</summary>
public sealed class SettingsService(IRepository<SiteSettings> settings, IUnitOfWork unitOfWork, IValidator<SettingsDto> validator)
{
    public async Task<SettingsDto> GetAsync(CancellationToken cancellationToken = default)
        => ToDto(await settings.GetByIdAsync(SiteSettings.SingletonId, cancellationToken));

    public async Task<SettingsDto> UpdateAsync(SettingsRequest request, CancellationToken cancellationToken = default)
    {
        var draft = ToDraft(request);
        validator.ThrowIfInvalid(draft);

        var row = await settings.GetByIdAsync(SiteSettings.SingletonId, cancellationToken);
        if (row is null)
        {
            row = new SiteSettings();
            await settings.AddAsync(row, cancellationToken);
        }

        row.Name = draft.Name;
        row.Tagline = draft.Tagline;
        row.DriverName = draft.DriverName;
        row.DriverRole = draft.DriverRole;
        row.Phone = draft.Phone;
        row.WhatsappNumber = draft.WhatsappNumber;
        row.Email = draft.Email;
        row.Address = draft.Address;
        row.TrustYears = draft.Trust.YearsExperience;
        row.TrustTrips = draft.Trust.TripsCompleted;
        row.TrustRating = decimal.Round(draft.Trust.Rating, 1);
        row.TrustLicensed = draft.Trust.Licensed;
        row.Languages = [.. draft.Languages];
        row.Licenses = [.. draft.Licenses];
        row.Socials = [.. draft.Socials.Select(link => new SocialLink(link.Label, link.Href))];

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(row);
    }

    /// <summary>The request with text trimmed, blank list entries and incomplete social links dropped, and missing values made empty.</summary>
    private static SettingsDto ToDraft(SettingsRequest request) => new(
        Merge.Text(request.Name, string.Empty),
        Merge.Text(request.Tagline, string.Empty),
        Merge.Text(request.DriverName, string.Empty),
        Merge.Text(request.DriverRole, string.Empty),
        Merge.Text(request.Phone, string.Empty),
        Merge.Text(request.WhatsappNumber, string.Empty),
        Merge.Text(request.Email, string.Empty),
        Merge.Text(request.Address, string.Empty),
        [.. (request.Socials ?? [])
            .Select(link => new SocialLinkDto(Merge.Text(link?.Label, string.Empty), Merge.Text(link?.Href, string.Empty)))
            .Where(link => link.Label.Length > 0 && link.Href.Length > 0)],
        new TrustDto(
            request.Trust?.YearsExperience ?? 0,
            request.Trust?.TripsCompleted ?? 0,
            request.Trust?.Rating ?? 0,
            Merge.Text(request.Trust?.Licensed, string.Empty)),
        Merge.Lines(request.Languages, []),
        Merge.Lines(request.Licenses, []));

    private static SettingsDto ToDto(SiteSettings? row) => row is null
        ? new SettingsDto(
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            [], new TrustDto(0, 0, 0, string.Empty), [], [])
        : new SettingsDto(
            row.Name,
            row.Tagline,
            row.DriverName,
            row.DriverRole,
            row.Phone,
            row.WhatsappNumber,
            row.Email,
            row.Address,
            [.. row.Socials.Select(link => new SocialLinkDto(link.Label, link.Href))],
            new TrustDto(row.TrustYears, row.TrustTrips, row.TrustRating, row.TrustLicensed),
            [.. row.Languages],
            [.. row.Licenses]);
}
