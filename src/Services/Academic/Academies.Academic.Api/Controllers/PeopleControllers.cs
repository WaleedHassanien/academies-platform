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

    /// <summary>Who pays and receives invoices: the student, a parent account, or (null) the default.</summary>
    [HttpPut("students/{userId:long}/payer")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<StudentDto>>> SetPayer(long userId, SetPayerRequest request, CancellationToken ct) =>
        Ok(ApiResponse<StudentDto>.Ok(await profiles.SetPayerAsync(userId, request.PayerUserId, ct)));

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

/// <summary>Supervisor↔teacher (US-022), teacher↔student (US-023) and teachers' subjects.</summary>
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

    /// <summary>The subjects a teacher may teach (empty: any).</summary>
    [HttpGet("teachers/{userId:long}/courses")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<long>>>> TeacherCourses(long userId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<long>>.Ok(await relationships.TeacherCoursesAsync(userId, ct)));

    [HttpPut("teachers/{userId:long}/courses")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<long>>>> SetTeacherCourses(long userId, SetTeacherCoursesRequest request, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<long>>.Ok(await relationships.SetTeacherCoursesAsync(userId, request.CourseIds, ct)));
}

/// <summary>A student's subjects (enrollments) and the teacher's plan for each subject.</summary>
[ApiController]
[Authorize]
public sealed class StudentLearningController(IEnrollmentService enrollments, ILearningPlanService plans) : ControllerBase
{
    [HttpGet("students/{userId:long}/enrollments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EnrollmentDto>>>> Enrollments(long userId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<EnrollmentDto>>.Ok(await enrollments.ListAsync(userId, ct)));

    [HttpPost("students/{userId:long}/enrollments")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<EnrollmentDto>>> Enroll(long userId, SaveEnrollmentRequest request, CancellationToken ct) =>
        Ok(ApiResponse<EnrollmentDto>.Ok(await enrollments.AddAsync(userId, request, ct)));

    [HttpPut("enrollments/{id:long}")]
    [HasPermission(Permissions.Profiles.Manage)]
    public async Task<ActionResult<ApiResponse<EnrollmentDto>>> UpdateEnrollment(long id, SaveEnrollmentRequest request, CancellationToken ct) =>
        Ok(ApiResponse<EnrollmentDto>.Ok(await enrollments.UpdateAsync(id, request, ct)));

    [HttpGet("students/{userId:long}/plans")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LearningPlanDto>>>> Plans(long userId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<LearningPlanDto>>.Ok(await plans.ListAsync(userId, ct)));

    /// <summary>Staff or the student's teacher; checked in the service.</summary>
    [HttpPost("students/{userId:long}/plans")]
    public async Task<ActionResult<ApiResponse<LearningPlanDto>>> CreatePlan(long userId, SaveLearningPlanRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LearningPlanDto>.Ok(await plans.CreateAsync(userId, request, ct)));

    [HttpPut("plans/{id:long}")]
    public async Task<ActionResult<ApiResponse<LearningPlanDto>>> UpdatePlan(long id, SaveLearningPlanRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LearningPlanDto>.Ok(await plans.UpdateAsync(id, request, ct)));

    [HttpDelete("plans/{id:long}")]
    public async Task<ActionResult<ApiResponse>> DeletePlan(long id, CancellationToken ct)
    {
        await plans.DeleteAsync(id, ct);
        return Ok(ApiResponse.Ok("Plan deleted."));
    }
}

/// <summary>Sales: leads, trial sessions and conversion into students.</summary>
[ApiController]
[Authorize]
[Route("leads")]
public sealed class LeadsController(ILeadService leads) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Leads.View)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeadDto>>>> List([FromQuery] LeadQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<LeadDto>>.Ok(await leads.ListAsync(query, ct)));

    [HttpGet("summary")]
    [HasPermission(Permissions.Leads.View)]
    public async Task<ActionResult<ApiResponse<LeadSummaryDto>>> Summary(CancellationToken ct) =>
        Ok(ApiResponse<LeadSummaryDto>.Ok(await leads.SummaryAsync(ct)));

    [HttpGet("{id:long}")]
    [HasPermission(Permissions.Leads.View)]
    public async Task<ActionResult<ApiResponse<LeadDto>>> Get(long id, CancellationToken ct) =>
        Ok(ApiResponse<LeadDto>.Ok(await leads.GetAsync(id, ct)));

    [HttpPost]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse<LeadDto>>> Create(SaveLeadRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LeadDto>.Ok(await leads.CreateAsync(request, ct)));

    [HttpPut("{id:long}")]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse<LeadDto>>> Update(long id, SaveLeadRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LeadDto>.Ok(await leads.UpdateAsync(id, request, ct)));

    [HttpPut("{id:long}/status")]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse<LeadDto>>> SetStatus(long id, SetLeadStatusRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LeadDto>.Ok(await leads.SetStatusAsync(id, request, ct)));

    [HttpDelete("{id:long}")]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken ct)
    {
        await leads.DeleteAsync(id, ct);
        return Ok(ApiResponse.Ok("Lead deleted."));
    }

    [HttpGet("{id:long}/activity")]
    [HasPermission(Permissions.Leads.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeadActivityDto>>>> Activity(long id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<LeadActivityDto>>.Ok(await leads.ActivityAsync(id, ct)));

    [HttpPost("{id:long}/activity")]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse<LeadActivityDto>>> AddNote(long id, AddLeadNoteRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LeadActivityDto>.Ok(await leads.AddNoteAsync(id, request, ct)));

    [HttpPost("{id:long}/trial")]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse<LeadDto>>> ScheduleTrial(long id, ScheduleTrialRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LeadDto>.Ok(await leads.ScheduleTrialAsync(id, request, ct)));

    /// <summary>The trial's teacher or sales records the outcome; checked in the service.</summary>
    [HttpPut("{id:long}/trial/outcome")]
    public async Task<ActionResult<ApiResponse<LeadDto>>> TrialOutcome(long id, TrialOutcomeRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LeadDto>.Ok(await leads.RecordTrialOutcomeAsync(id, request, ct)));

    [HttpGet("{id:long}/trial/join-link")]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse<JoinLinkDto>>> TrialJoinLink(long id, CancellationToken ct) =>
        Ok(ApiResponse<JoinLinkDto>.Ok(await leads.TrialJoinLinkAsync(id, ct)));

    [HttpPost("{id:long}/convert")]
    [HasPermission(Permissions.Leads.Manage)]
    public async Task<ActionResult<ApiResponse<LeadDto>>> Convert(long id, ConvertLeadRequest request, CancellationToken ct) =>
        Ok(ApiResponse<LeadDto>.Ok(await leads.ConvertAsync(id, request, ct)));
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
