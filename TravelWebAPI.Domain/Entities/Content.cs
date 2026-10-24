using TravelWebAPI.Domain.Common;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Domain.Entities;

/// <summary>Service shown on the website and chosen in the booking form. The id is a slug.</summary>
public class Service : IHasCreatedAt, IHasUpdatedAt
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<ServiceDetail> Details { get; set; } = [];
}

public class ServiceDetail
{
    public Guid Id { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class Vehicle : IHasCreatedAt, IHasUpdatedAt
{
    public string Id { get; set; } = string.Empty;
    public VehicleType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public int Seats { get; set; }
    public int Luggage { get; set; }
    public bool Ac { get; set; } = true;
    public decimal PricePerKmLkr { get; set; }
    public decimal PricePerDayLkr { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class Destination : IHasCreatedAt, IHasUpdatedAt
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
    public string Description { get; set; } = string.Empty;
    public string BestTime { get; set; } = string.Empty;
    public string TravelTimeFromAirport { get; set; } = string.Empty;
    public bool Featured { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<DestinationImage> Images { get; set; } = [];
}

public class DestinationImage
{
    public Guid Id { get; set; }
    public string DestinationId { get; set; } = string.Empty;
    public string Src { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class Package : IHasCreatedAt, IHasUpdatedAt
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public int DurationDays { get; set; }
    public decimal PriceLkr { get; set; }
    public string Image { get; set; } = string.Empty;
    public bool Featured { get; set; }

    /// <summary>Published. Only active packages are public.</summary>
    public bool Active { get; set; } = true;

    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<PackageDestination> Destinations { get; set; } = [];
    public List<ItineraryDay> ItineraryDays { get; set; } = [];
    public List<PackageInterest> Interests { get; set; } = [];
    public List<PackageImage> Images { get; set; } = [];
    public List<PackageItem> Items { get; set; } = [];
}

/// <summary>Ordered stop of a package route.</summary>
public class PackageDestination
{
    public string PackageId { get; set; } = string.Empty;
    public string DestinationId { get; set; } = string.Empty;
    public int Position { get; set; }

    public Package? Package { get; set; }
    public Destination? Destination { get; set; }
}

public class ItineraryDay
{
    public Guid Id { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public int Day { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class PackageInterest
{
    public string PackageId { get; set; } = string.Empty;
    public InterestType Interest { get; set; }
}

public class PackageImage
{
    public Guid Id { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string Src { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>Highlight, included or excluded line of a package.</summary>
public class PackageItem
{
    public Guid Id { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public PackageItemKind Kind { get; set; }
    public string Content { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class GalleryImage : IHasCreatedAt
{
    public string Id { get; set; } = string.Empty;
    public string Src { get; set; } = string.Empty;
    public string Alt { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;

    /// <summary>Width divided by height, used for the masonry layout.</summary>
    public decimal Aspect { get; set; }

    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Faq
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class Review : IHasCreatedAt
{
    public Guid Id { get; set; }

    /// <summary>Set when the author was signed in.</summary>
    public Guid? UserId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public short Rating { get; set; }
    public string? Photo { get; set; }
    public string TripType { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateOnly ReviewDate { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
}
