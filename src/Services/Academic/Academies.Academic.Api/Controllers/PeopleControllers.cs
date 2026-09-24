using Academies.Academic.Application;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Academies.Academic.Api.Controllers;

/// <summary>Role profiles (US-020) and supervisor shifts (US-021). Reads are filtered by relationship.</summary>
[ApiController]
[Authorize]
public sealed class ProfilesController(IProfileService profiles, IWorkScheduleService schedules, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("students")]
    public async Task<ActionResult<ApiResponse<PagedResult<StudentDto>>>> Students([FromQuery] StudentQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<StudentDto>>.Ok(await profiles.ListStudentsAsync(query, ct)));

    [HttpGet("students/{userId:long}")]
    public async Task<ActionResult<ApiResponse<StudentDto>>> Student(long userId, CancellationToken ct) =>
        Ok(ApiResponse<StudentDto>.Ok(await profiles.GetStudentAsync(userId, ct)));

    [HttpPut("students/{userId:long}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<StudentDto>>> UpdateStudent(long userId, UpdateStudentRequest request, CancellationToken ct) =>
        Ok(ApiResponse<StudentDto>.Ok(await profiles.UpdateStudentAsync(userId, request, ct)));

    /// <summary>Links (or unlinks, with null) the student's guardian.</summary>
    [HttpPut("students/{userId:long}/parent")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<StudentDto>>> SetParent(long userId, SetParentRequest request, CancellationToken ct) =>
        Ok(ApiResponse<StudentDto>.Ok(await profiles.SetParentAsync(userId, request.ParentUserId, ct)));

    [HttpGet("teachers")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StaffProfileDto>>>> Teachers(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<StaffProfileDto>>.Ok(await profiles.ListTeachersAsync(ct)));

    [HttpPut("teachers/{userId:long}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<StaffProfileDto>>> UpdateTeacher(long userId, UpdateTeacherRequest request, CancellationToken ct) =>
        Ok(ApiResponse<StaffProfileDto>.Ok(await profiles.UpdateTeacherAsync(userId, request, ct)));

    [HttpGet("supervisors")]
    [HasPermission(Permissions.Profiles.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StaffProfileDto>>>> Supervisors(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<StaffProfileDto>>.Ok(await profiles.ListSupervisorsAsync(ct)));

    [HttpPut("supervisors/{userId:long}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<StaffProfileDto>>> UpdateSupervisor(long userId, UpdateSupervisorRequest request, CancellationToken ct) =>
        Ok(ApiResponse<StaffProfileDto>.Ok(await profiles.UpdateSupervisorAsync(userId, request, ct)));

    [HttpGet("parents")]
    [HasPermission(Permissions.Profiles.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ParentDto>>>> Parents(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ParentDto>>.Ok(await profiles.ListParentsAsync(ct)));

    [HttpGet("parents/{userId:long}")]
    public async Task<ActionResult<ApiResponse<ParentDto>>> Parent(long userId, CancellationToken ct) =>
        Ok(ApiResponse<ParentDto>.Ok(await profiles.GetParentAsync(userId, ct)));

    [HttpPut("parents/{userId:long}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<ParentDto>>> UpdateParent(long userId, UpdateParentRequest request, CancellationToken ct) =>
        Ok(ApiResponse<ParentDto>.Ok(await profiles.UpdateParentAsync(userId, request, ct)));

    [HttpGet("supervisors/{userId:long}/work-schedule")]
    [HasPermission(Permissions.Profiles.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkDayDto>>>> WorkSchedule(long userId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<WorkDayDto>>.Ok(await schedules.GetAsync(userId, ct)));

    /// <summary>Only managers edit shifts; the supervisor sees theirs at /me/work-schedule.</summary>
    [HttpPut("supervisors/{userId:long}/work-schedule")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkDayDto>>>> SaveWorkSchedule(long userId, SaveWorkScheduleRequest request, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<WorkDayDto>>.Ok(await schedules.SaveAsync(userId, request, ct)));

    [HttpGet("me/work-schedule")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkDayDto>>>> MyWorkSchedule(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<WorkDayDto>>.Ok(await schedules.GetAsync(currentUser.UserId ?? throw new UnauthorizedException(), ct)));
}

/// <summary>Supervisor↔teacher (US-022), teacher↔student and groups (US-023).</summary>
[ApiController]
[Authorize]
public sealed class RelationshipsController(IRelationshipService relationships) : ControllerBase
{
    [HttpGet("supervisors/{userId:long}/teachers")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PersonRefDto>>>> SupervisorTeachers(long userId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PersonRefDto>>.Ok(await relationships.SupervisorTeachersAsync(userId, ct)));

    [HttpPut("supervisors/{userId:long}/teachers")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PersonRefDto>>>> SetSupervisorTeachers(long userId, SetMembersRequest request, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PersonRefDto>>.Ok(await relationships.SetSupervisorTeachersAsync(userId, request.UserIds, ct)));

    [HttpGet("teachers/{userId:long}/students")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PersonRefDto>>>> TeacherStudents(long userId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PersonRefDto>>.Ok(await relationships.TeacherStudentsAsync(userId, ct)));

    [HttpPut("teachers/{userId:long}/students")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PersonRefDto>>>> SetTeacherStudents(long userId, SetMembersRequest request, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PersonRefDto>>.Ok(await relationships.SetTeacherStudentsAsync(userId, request.UserIds, ct)));

    [HttpGet("groups")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<GroupDto>>>> Groups(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<GroupDto>>.Ok(await relationships.ListGroupsAsync(ct)));

    [HttpGet("groups/{id:long}")]
    public async Task<ActionResult<ApiResponse<GroupDto>>> Group(long id, CancellationToken ct) =>
        Ok(ApiResponse<GroupDto>.Ok(await relationships.GetGroupAsync(id, ct)));

    [HttpPost("groups")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<GroupDto>>> CreateGroup(SaveGroupRequest request, CancellationToken ct) =>
        Ok(ApiResponse<GroupDto>.Ok(await relationships.CreateGroupAsync(request, ct)));

    [HttpPut("groups/{id:long}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<GroupDto>>> UpdateGroup(long id, SaveGroupRequest request, CancellationToken ct) =>
        Ok(ApiResponse<GroupDto>.Ok(await relationships.UpdateGroupAsync(id, request, ct)));

    [HttpDelete("groups/{id:long}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse>> DeleteGroup(long id, CancellationToken ct)
    {
        await relationships.DeleteGroupAsync(id, ct);
        return Ok(ApiResponse.Ok("Group deleted."));
    }

    [HttpPut("groups/{id:long}/students")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<GroupDto>>> SetGroupStudents(long id, SetMembersRequest request, CancellationToken ct) =>
        Ok(ApiResponse<GroupDto>.Ok(await relationships.SetGroupStudentsAsync(id, request.UserIds, ct)));
}

/// <summary>Courses and materials (US-024, US-036).</summary>
[ApiController]
[Authorize]
[Route("courses")]
public sealed class CoursesController(ICourseService courses) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CourseDto>>>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<CourseDto>>.Ok(await courses.ListAsync(includeInactive, ct)));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<CourseDto>>> Get(long id, CancellationToken ct) =>
        Ok(ApiResponse<CourseDto>.Ok(await courses.GetAsync(id, ct)));

    [HttpPost]
    [HasPermission(Permissions.Courses.Manage)]
    public async Task<ActionResult<ApiResponse<CourseDto>>> Create(SaveCourseRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CourseDto>.Ok(await courses.CreateAsync(request, ct)));

    [HttpPut("{id:long}")]
    [HasPermission(Permissions.Courses.Manage)]
    public async Task<ActionResult<ApiResponse<CourseDto>>> Update(long id, SaveCourseRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CourseDto>.Ok(await courses.UpdateAsync(id, request, ct)));

    [HttpDelete("{id:long}")]
    [HasPermission(Permissions.Courses.Manage)]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken ct)
    {
        await courses.DeleteAsync(id, ct);
        return Ok(ApiResponse.Ok("Course deleted."));
    }

    [HttpGet("{id:long}/materials")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MaterialDto>>>> Materials(long id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<MaterialDto>>.Ok(await courses.MaterialsAsync(id, ct)));

    [HttpPost("{id:long}/materials")]
    [HasPermission(Permissions.Courses.Manage)]
    public async Task<ActionResult<ApiResponse<MaterialDto>>> AddMaterial(long id, SaveMaterialRequest request, CancellationToken ct) =>
        Ok(ApiResponse<MaterialDto>.Ok(await courses.AddMaterialAsync(id, request, ct)));

    [HttpDelete("{id:long}/materials/{materialId:long}")]
    [HasPermission(Permissions.Courses.Manage)]
    public async Task<ActionResult<ApiResponse>> DeleteMaterial(long id, long materialId, CancellationToken ct)
    {
        await courses.DeleteMaterialAsync(id, materialId, ct);
        return Ok(ApiResponse.Ok("Material deleted."));
    }
}
