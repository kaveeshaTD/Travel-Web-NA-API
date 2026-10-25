using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelWebAPI.Api.Auth;
using TravelWebAPI.Application.Lists;
using TravelWebAPI.Application.Users;

namespace TravelWebAPI.Api.Controllers;

/// <summary>Accounts, managed by the admin. Thin: the rules live in <see cref="UserAdminService"/>.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = AuthRoles.Admin)]
public sealed class UsersController(UserAdminService users) : ControllerBase
{
    /// <summary>
    /// One page of users, newest first, with the number of their bookings. Supports page, pageSize, search, sort, facets and
    /// the filters role and status.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> List([FromQuery] UserListRequest request, CancellationToken cancellationToken)
        => Ok(await users.ListAsync(request, cancellationToken));

    /// <summary>Creates an account. The answer points at the list because there is no single-user read.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest? request, CancellationToken cancellationToken)
        => Created("/api/users", await users.CreateAsync(request ?? new CreateUserRequest(), cancellationToken));

    /// <summary>Edits name, phone, country, role or status. Deactivating ends the user's sessions.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UserUpdateRequest? request, CancellationToken cancellationToken)
        => Ok(await users.UpdateAsync(id, request ?? new UserUpdateRequest(), cancellationToken));
}
