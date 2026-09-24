using Academies.Academic.Application;
using Academies.BuildingBlocks.Application.Models;
using Academies.BuildingBlocks.Infrastructure.Internal;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Academies.Academic.Api.Controllers;

/// <summary>Sessions, attendance and feedback (US-025..027, US-037).</summary>
[ApiController]
[Authorize]
[Route("sessions")]
public sealed class SessionsController(ISessionService sessions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SessionDto>>>> List([FromQuery] SessionQuery query, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<SessionDto>>.Ok(await sessions.ListAsync(query, ct)));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Get(long id, CancellationToken ct) =>
        Ok(ApiResponse<SessionDto>.Ok(await sessions.GetAsync(id, ct)));

    /// <summary>Staff schedule for anyone; teachers for themselves. Refuses clashing slots.</summary>
    [HttpPost]
    [HasPermission(Permissions.Sessions.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SessionDto>>>> Create(SaveSessionRequest request, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<SessionDto>>.Ok(await sessions.CreateAsync(request, ct)));

    [HttpPut("{id:long}")]
    [HasPermission(Permissions.Sessions.View)]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Update(long id, SaveSessionRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SessionDto>.Ok(await sessions.UpdateAsync(id, request, ct)));

    [HttpPost("{id:long}/complete")]
    [HasPermission(Permissions.Sessions.View)]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Complete(long id, CancellationToken ct) =>
        Ok(ApiResponse<SessionDto>.Ok(await sessions.CompleteAsync(id, ct)));

    [HttpPost("{id:long}/cancel")]
    [HasPermission(Permissions.Sessions.View)]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Cancel(long id, CancellationToken ct) =>
        Ok(ApiResponse<SessionDto>.Ok(await sessions.CancelAsync(id, ct)));

    [HttpPost("{id:long}/meeting-link")]
    [HasPermission(Permissions.Sessions.View)]
    public async Task<ActionResult<ApiResponse<SessionDto>>> MeetingLink(long id, CancellationToken ct) =>
        Ok(ApiResponse<SessionDto>.Ok(await sessions.GenerateMeetingLinkAsync(id, ct)));

    [HttpGet("{id:long}/roster")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RosterItemDto>>>> Roster(long id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<RosterItemDto>>.Ok(await sessions.RosterAsync(id, ct)));

    [HttpPut("{id:long}/attendance")]
    [HasPermission(Permissions.Attendance.Record)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RosterItemDto>>>> Attendance(long id, RecordAttendanceRequest request, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<RosterItemDto>>.Ok(await sessions.RecordAttendanceAsync(id, request, ct)));

    [HttpPut("{id:long}/feedback")]
    [HasPermission(Permissions.Feedback.Write)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RosterItemDto>>>> Feedback(long id, SaveFeedbackRequest request, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<RosterItemDto>>.Ok(await sessions.SaveFeedbackAsync(id, request, ct)));
}

/// <summary>Per-student views: feedback, points, and the role dashboards (US-027, US-028, US-040, US-042).</summary>
[ApiController]
[Authorize]
public sealed class StudentViewsController(ISessionService sessions, IGamificationService gamification, IOverviewService overview) : ControllerBase
{
    [HttpGet("students/{userId:long}/feedback")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FeedbackDto>>>> Feedback(long userId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(ApiResponse<IReadOnlyList<FeedbackDto>>.Ok(await sessions.StudentFeedbackAsync(userId, take, ct)));

    [HttpGet("students/{userId:long}/points")]
    public async Task<ActionResult<ApiResponse<StudentPointsDto>>> Points(long userId, CancellationToken ct) =>
        Ok(ApiResponse<StudentPointsDto>.Ok(await gamification.GetAsync(userId, ct)));

    [HttpGet("leaderboard")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LeaderboardEntryDto>>>> Leaderboard([FromQuery] int top = 10, CancellationToken ct = default) =>
        Ok(ApiResponse<IReadOnlyList<LeaderboardEntryDto>>.Ok(await gamification.LeaderboardAsync(top, ct)));

    [HttpGet("me/overview")]
    public async Task<ActionResult<ApiResponse<MyOverviewDto>>> MyOverview(CancellationToken ct) =>
        Ok(ApiResponse<MyOverviewDto>.Ok(await overview.MineAsync(ct)));
}

/// <summary>Assignments and submissions (US-036), certificates (US-041).</summary>
[ApiController]
[Authorize]
public sealed class LearningController(IAssignmentService assignments, ICertificateService certificates) : ControllerBase
{
    [HttpGet("assignments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AssignmentDto>>>> Assignments([FromQuery] long? courseId, [FromQuery] long? groupId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<AssignmentDto>>.Ok(await assignments.ListAsync(courseId, groupId, ct)));

    [HttpPost("assignments")]
    public async Task<ActionResult<ApiResponse<AssignmentDto>>> CreateAssignment(SaveAssignmentRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AssignmentDto>.Ok(await assignments.CreateAsync(request, ct)));

    [HttpPut("assignments/{id:long}")]
    public async Task<ActionResult<ApiResponse<AssignmentDto>>> UpdateAssignment(long id, SaveAssignmentRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AssignmentDto>.Ok(await assignments.UpdateAsync(id, request, ct)));

    [HttpDelete("assignments/{id:long}")]
    public async Task<ActionResult<ApiResponse>> DeleteAssignment(long id, CancellationToken ct)
    {
        await assignments.DeleteAsync(id, ct);
        return Ok(ApiResponse.Ok("Assignment deleted."));
    }

    [HttpGet("assignments/{id:long}/submissions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SubmissionDto>>>> Submissions(long id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<SubmissionDto>>.Ok(await assignments.SubmissionsAsync(id, ct)));

    [HttpPost("assignments/{id:long}/submissions")]
    public async Task<ActionResult<ApiResponse<SubmissionDto>>> Submit(long id, SubmitRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SubmissionDto>.Ok(await assignments.SubmitAsync(id, request, ct)));

    [HttpPut("submissions/{id:long}/grade")]
    public async Task<ActionResult<ApiResponse<SubmissionDto>>> Grade(long id, GradeRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SubmissionDto>.Ok(await assignments.GradeAsync(id, request, ct)));

    [HttpGet("certificates")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CertificateDto>>>> Certificates([FromQuery] long? studentUserId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<CertificateDto>>.Ok(await certificates.ListAsync(studentUserId, ct)));

    [HttpPost("certificates")]
    public async Task<ActionResult<ApiResponse<CertificateDto>>> Issue(IssueCertificateRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CertificateDto>.Ok(await certificates.IssueAsync(request, ct)));

    [HttpGet("certificates/{id:long}/pdf")]
    public async Task<IActionResult> Pdf(long id, CancellationToken ct)
    {
        var (fileName, content) = await certificates.PdfAsync(id, ct);
        return File(content, "application/pdf", fileName);
    }
}

/// <summary>Service-to-service data for Finance (salaries) and Engagement (dashboards).</summary>
[ApiController]
[InternalApi]
[Route("internal")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalAcademicController(IOverviewService overview) : ControllerBase
{
    [HttpGet("sessions/completed-counts")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TeacherSessionCountDto>>>> CompletedCounts(
        [FromQuery] long academyId, [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
    {
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(academyId));
        return Ok(ApiResponse<IReadOnlyList<TeacherSessionCountDto>>.Ok(await overview.CompletedSessionCountsAsync(year, month, ct)));
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<AcademicStatsDto>>> Stats(
        [FromQuery] long academyId, [FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, [FromQuery] long? supervisorUserId, CancellationToken ct)
    {
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(academyId));
        return Ok(ApiResponse<AcademicStatsDto>.Ok(await overview.StatsAsync(fromUtc, toUtc, supervisorUserId, ct)));
    }
}
