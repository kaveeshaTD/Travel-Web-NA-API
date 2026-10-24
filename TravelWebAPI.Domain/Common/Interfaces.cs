namespace TravelWebAPI.Domain.Common;

/// <summary>Entity with a creation time that the data layer fills in.</summary>
public interface IHasCreatedAt
{
    DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Entity with a last-change time that the data layer fills in.</summary>
public interface IHasUpdatedAt
{
    DateTimeOffset UpdatedAt { get; set; }
}
