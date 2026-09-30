using Academies.BuildingBlocks.Application.Exceptions;
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

    /// <summary>Sales looks up student and parent accounts (e.g. a sibling's parent) when signing up a lead.</summary>
    [HttpGet("accounts")]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> Accounts([FromQuery] string? search, [FromQuery] string role, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<UserDto>>.Ok(await users.ListAsync(new UserQuery(search, SignupRole(role), true, PageSize: 20), ct)));

    /// <summary>Sales creates the student (and parent) accounts of a new sign-up; no other roles.</summary>
    [HttpPost("accounts")]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse<UserDto>>> CreateAccount(CreateUserRequest request, CancellationToken ct)
    {
        if (request.Roles.Count != 1)
        {
            throw new BusinessRuleException("A sign-up account is a student or a parent.");
        }

        return Ok(ApiResponse<UserDto>.Ok(await users.CreateAsync(request with { Roles = [SignupRole(request.Roles[0])], AcademyId = null }, ct)));
    }

    private static string SignupRole(string role) =>
        role.Equals(Roles.Student, StringComparison.OrdinalIgnoreCase) ? Roles.Student
        : role.Equals(Roles.Parent, StringComparison.OrdinalIgnoreCase) ? Roles.Parent
        : throw new BusinessRuleException("A sign-up account is a student or a parent.");

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
