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

/// <summary>Content record with a slug id that the admin can put in a chosen order.</summary>
public interface IOrdered
{
    string Id { get; set; }

    /// <summary>Zero-based position in the list shown to visitors.</summary>
    int SortOrder { get; set; }
}
