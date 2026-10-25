using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Contact;

/// <summary>The inbox request: the common paging, sorting and search plus the status filter.</summary>
public sealed class MessageListRequest : ListQueryRequest
{
    /// <summary>Only messages with this status: new, read or replied.</summary>
    public string? Status { get; set; }
}

/// <summary>The filters of the inbox after they have been checked. A missing value means "do not filter".</summary>
public sealed record MessageListFilter(MessageStatus? Status);

public static class MessageLists
{
    /// <summary>Sort by: createdAt (default, newest first), name, status. Facet: status.</summary>
    public static readonly ListDefinition Definition = new(["createdAt", "name", "status"], "-createdAt", ["status"]);

    public static (ListQuery Query, MessageListFilter Filter) Parse(MessageListRequest request)
    {
        var errors = new FieldErrors();
        var query = ListParser.Parse(request, Definition, errors);
        var filter = new MessageListFilter(ListFilters.Wire<MessageStatus>(request.Status, "status", ListFilters.OneOf<MessageStatus>("Status"), errors));

        errors.ThrowIfAny();
        return (query, filter);
    }
}
