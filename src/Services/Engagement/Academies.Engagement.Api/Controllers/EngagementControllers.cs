using Academies.BuildingBlocks.Application.Models;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Security;
using Academies.Engagement.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Academies.Engagement.Api.Controllers;

/// <summary>The caller's in-app notifications (US-035).</summary>
[ApiController]
[Authorize]
[Route("notifications")]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<NotificationPageDto>>> Mine(
        [FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(ApiResponse<NotificationPageDto>.Ok(await notifications.MineAsync(unreadOnly, page, pageSize, ct)));

    [HttpGet("me/unread-count")]
    public async Task<ActionResult<ApiResponse<int>>> UnreadCount(CancellationToken ct) =>
        Ok(ApiResponse<int>.Ok(await notifications.UnreadCountAsync(ct)));

    [HttpPost("{id:long}/read")]
    public async Task<ActionResult<ApiResponse>> Read(long id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return Ok(ApiResponse.Ok());
    }

    [HttpPost("me/read-all")]
    public async Task<ActionResult<ApiResponse>> ReadAll(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return Ok(ApiResponse.Ok());
    }
}

/// <summary>KPIs and charts for admins and supervisors (US-038).</summary>
[ApiController]
[Route("dashboards")]
public sealed class DashboardsController(IDashboardService dashboards) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Dashboards.View)]
    public async Task<ActionResult<ApiResponse<DashboardDto>>> Get([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(ApiResponse<DashboardDto>.Ok(await dashboards.GetAsync(from, to, ct)));
}

/// <summary>Who did what and when (US-043).</summary>
[ApiController]
[Route("audit-logs")]
public sealed class AuditLogsController(IAuditLogService audit) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.AuditLogs.View)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> List([FromQuery] AuditLogQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(await audit.ListAsync(query, ct)));
}
