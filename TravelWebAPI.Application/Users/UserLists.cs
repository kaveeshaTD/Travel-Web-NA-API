using TravelWebAPI.Application.Lists;
using TravelWebAPI.Domain.Enums;

namespace TravelWebAPI.Application.Users;

/// <summary>The user list request: the common paging, sorting and search plus the role and status filters.</summary>
public sealed class UserListRequest : ListQueryRequest
{
    /// <summary>Only users with this role: admin or customer.</summary>
    public string? Role { get; set; }

    /// <summary>Only users with this status: active or inactive.</summary>
    public string? Status { get; set; }
}

/// <summary>The filters of the user list after they have been checked. A missing value means "do not filter".</summary>
public sealed record UserListFilter(UserRole? Role, UserStatus? Status);

public static class UserLists
{
    /// <summary>Sort by: createdAt (default, newest first), name, email, role, status, bookingCount. Facets: role, status.</summary>
    public static readonly ListDefinition Definition = new(
        ["createdAt", "name", "email", "role", "status", "bookingCount"],
        "-createdAt",
        ["role", "status"]);

    public static (ListQuery Query, UserListFilter Filter) Parse(UserListRequest request)
    {
        var errors = new FieldErrors();
        var query = ListParser.Parse(request, Definition, errors);
        var filter = new UserListFilter(
            ListFilters.Wire<UserRole>(request.Role, "role", UserRules.RoleMessage, errors),
            ListFilters.Wire<UserStatus>(request.Status, "status", UserRules.StatusMessage, errors));

        errors.ThrowIfAny();
        return (query, filter);
    }
}
