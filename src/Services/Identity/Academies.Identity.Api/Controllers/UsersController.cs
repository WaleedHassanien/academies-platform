using Academies.BuildingBlocks.Application.Models;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Security;
using Academies.Identity.Application.Users;
using Microsoft.AspNetCore.Mvc;

namespace Academies.Identity.Api.Controllers;

/// <summary>Users inside an academy (US-019) and plan usage (US-017).</summary>
[ApiController]
[Route("users")]
public sealed class UsersController(IUserService users) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> List([FromQuery] UserQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<UserDto>>.Ok(await users.ListAsync(query, ct)));

    [HttpGet("roles")]
    [HasPermission(Permissions.Users.View)]
    public ActionResult<ApiResponse<IReadOnlyList<string>>> AssignableRoles() =>
        Ok(ApiResponse<IReadOnlyList<string>>.Ok(UserRoleCatalog.Assignable));

    /// <summary>Plan consumption, e.g. students 140/150. SuperAdmin passes ?academyId=.</summary>
    [HttpGet("usage")]
    [HasPermission(Permissions.Users.View)]
    public async Task<ActionResult<ApiResponse<AcademyUsageDto>>> Usage([FromQuery] long? academyId, CancellationToken ct) =>
        Ok(ApiResponse<AcademyUsageDto>.Ok(await users.GetUsageAsync(academyId, ct)));

    [HttpGet("{id:long}")]
    [HasPermission(Permissions.Users.View)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Get(long id, CancellationToken ct) =>
        Ok(ApiResponse<UserDto>.Ok(await users.GetAsync(id, ct)));

    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create(CreateUserRequest request, CancellationToken ct) =>
        Ok(ApiResponse<UserDto>.Ok(await users.CreateAsync(request, ct)));

    [HttpPut("{id:long}")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(long id, UpdateUserRequest request, CancellationToken ct) =>
        Ok(ApiResponse<UserDto>.Ok(await users.UpdateAsync(id, request, ct)));

    [HttpPut("{id:long}/active")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<ActionResult<ApiResponse<UserDto>>> SetActive(long id, SetUserActiveRequest request, CancellationToken ct) =>
        Ok(ApiResponse<UserDto>.Ok(await users.SetActiveAsync(id, request.IsActive, ct)));

    [HttpDelete("{id:long}")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return Ok(ApiResponse.Ok("User deleted."));
    }
}
