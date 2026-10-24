using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Domain.Entities;

public record SocialLink(string Label, string Href);

/// <summary>Website settings. There is only ever one row.</summary>
public class SiteSettings : IHasUpdatedAt
{
    public const short SingletonId = 1;

    public short Id { get; set; } = SingletonId;
    public string Name { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string DriverRole { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    /// <summary>Digits only.</summary>
    public string WhatsappNumber { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int TrustYears { get; set; }
    public int TrustTrips { get; set; }
    public decimal TrustRating { get; set; }
    public string TrustLicensed { get; set; } = string.Empty;
    public List<string> Languages { get; set; } = [];
    public List<string> Licenses { get; set; } = [];
    public List<SocialLink> Socials { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Travel tips document. There is only ever one row; the lists are stored as raw JSON.</summary>
public class TravelTips : IHasUpdatedAt
{
    public const short SingletonId = 1;

    public short Id { get; set; } = SingletonId;
    public string RegionsJson { get; set; } = "[]";
    public string InfoJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Live market rate. The newest row per currency is the one in use.</summary>
public class ExchangeRate
{
    public Guid Id { get; set; }
    public CurrencyCode Currency { get; set; }

    /// <summary>LKR per 1 unit of <see cref="Currency"/>.</summary>
    public decimal RateLkr { get; set; }

    public string Source { get; set; } = string.Empty;
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>An uploaded image. Content tables keep only the URL string.</summary>
public class MediaFile : IHasCreatedAt
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public Guid? UploadedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
