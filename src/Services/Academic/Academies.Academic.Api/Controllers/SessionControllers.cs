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

    /// <summary>The caller's personal link into the online room (no Jitsi login needed with a token provider).</summary>
    [HttpGet("{id:long}/join")]
    public async Task<ActionResult<ApiResponse<JoinLinkDto>>> Join(long id, CancellationToken ct) =>
        Ok(ApiResponse<JoinLinkDto>.Ok(await sessions.JoinAsync(id, ct)));

    /// <summary>A teacher's own permanent room, to open any time.</summary>
    [HttpGet("~/me/meeting-room")]
    public async Task<ActionResult<ApiResponse<JoinLinkDto>>> MyRoom(CancellationToken ct) =>
        Ok(ApiResponse<JoinLinkDto>.Ok(await sessions.MyRoomAsync(ct)));

    /// <summary>A teacher's room link for their supervisor or staff to send them (valid for <paramref name="days"/> days).</summary>
    [HttpGet("~/teachers/{teacherUserId:long}/meeting-room-link")]
    public async Task<ActionResult<ApiResponse<JoinLinkDto>>> TeacherRoomLink(long teacherUserId, [FromQuery] int days = 30, CancellationToken ct = default) =>
        Ok(ApiResponse<JoinLinkDto>.Ok(await sessions.TeacherRoomLinkAsync(teacherUserId, days, ct)));

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

/// <summary>
/// Excuses and unexcused absences on one-to-one sessions, and the session report on each role's
/// dashboard. Who may do what is checked in <see cref="ISessionOutcomeService"/>.
/// </summary>
[ApiController]
[Authorize]
public sealed class SessionOutcomesController(ISessionOutcomeService outcomes) : ControllerBase
{
    /// <summary>The student (or parent, teacher, supervisor, staff) excuses the student from the session.</summary>
    [HttpPost("sessions/{id:long}/excuse")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Excuse(long id, RequestExcuseRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SessionDto>.Ok(await outcomes.RequestExcuseAsync(id, request, ct)));

    [HttpGet("excuses")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExcuseItemDto>>>> Excuses([FromQuery] string? status, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ExcuseItemDto>>.Ok(await outcomes.ExcusesAsync(status, ct)));

    /// <summary>Reschedule, carry over to next month, don't count, deduct from next month, or reject.</summary>
    [HttpPost("excuses/{id:long}/resolve")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> Resolve(long id, ResolveExcuseRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SessionDto>.Ok(await outcomes.ResolveExcuseAsync(id, request, ct)));

    /// <summary>Whether an unexcused absence counts (billed and paid) or not.</summary>
    [HttpPut("sessions/{id:long}/absence-decision")]
    public async Task<ActionResult<ApiResponse<SessionDto>>> DecideAbsence(long id, AbsenceDecisionRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SessionDto>.Ok(await outcomes.DecideAbsenceAsync(id, request, ct)));

    [HttpGet("absences/pending")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SessionDto>>>> PendingAbsences(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<SessionDto>>.Ok(await outcomes.PendingAbsencesAsync(ct)));

    [HttpGet("me/session-report")]
    public async Task<ActionResult<ApiResponse<SessionReportDto>>> Report([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct) =>
        Ok(ApiResponse<SessionReportDto>.Ok(await outcomes.ReportAsync(fromUtc, toUtc, ct)));
}

/// <summary>Per-student views: feedback, points, and the role dashboards (US-027, US-028, US-040, US-042).</summary>
[ApiController]
[Authorize]
public sealed class StudentViewsController(ISessionService sessions, IGamificationService gamification, IOverviewService overview) : ControllerBase
{
    [HttpGet("students/{userId:long}/feedback")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<FeedbackDto>>>> Feedback(long userId, [FromQuery] int take = 50, CancellationToken ct = default) =>
        Ok(ApiResponse<IReadOnlyList<FeedbackDto>>.Ok(await sessions.StudentFeedbackAsync(userId, take, ct)));

    /// <summary>Every session the student belongs to (past and upcoming), with their attendance and feedback.</summary>
    [HttpGet("students/{userId:long}/sessions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StudentSessionDto>>>> Sessions(long userId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<StudentSessionDto>>.Ok(await sessions.StudentSessionsAsync(userId, ct)));

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
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AssignmentDto>>>> Assignments([FromQuery] long? courseId, [FromQuery] long? studentUserId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<AssignmentDto>>.Ok(await assignments.ListAsync(courseId, studentUserId, ct)));

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
public sealed class InternalAcademicController(IOverviewService overview, ISessionOutcomeService outcomes) : ControllerBase
{
    /// <summary>One-to-one sessions with their outcome, for student billing and teacher payouts.</summary>
    [HttpGet("sessions/ledger")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LedgerSessionDto>>>> Ledger(
        [FromQuery] long academyId, [FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, [FromQuery] long? teacherUserId,
        [FromQuery] long? studentUserId, CancellationToken ct)
    {
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(academyId));
        return Ok(ApiResponse<IReadOnlyList<LedgerSessionDto>>.Ok(await outcomes.LedgerAsync(fromUtc, toUtc, teacherUserId, studentUserId, ct)));
    }

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
