using System.Globalization;
using System.Text.Json;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.BuildingBlocks.Domain;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using Academies.Engagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Academies.Engagement.Application;

public interface IEngagementDbContext
{
    DbSet<Person> People { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads KPI data from Academic and Finance (US-038).</summary>
public interface IStatsClient
{
    Task<JsonElement> AcademicAsync(long academyId, DateTime fromUtc, DateTime toUtc, long? supervisorUserId, CancellationToken ct = default);
    Task<JsonElement> FinanceAsync(long academyId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

// ---------- Notifications (US-035) ----------

public sealed record NotificationDto(
    long Id, string Type, string TitleAr, string TitleEn, string? BodyAr, string? BodyEn, string? Link, bool IsRead, DateTime CreatedOnUtc);

public sealed record NotificationPageDto(PagedResult<NotificationDto> Items, int Unread);

public sealed record NotificationMessage(
    string Type, string TitleAr, string TitleEn, string? BodyAr, string? BodyEn, string? Link, bool AlsoEmail);

public interface INotificationService
{
    Task<NotificationPageDto> MineAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task<int> UnreadCountAsync(CancellationToken ct = default);
    Task MarkReadAsync(long id, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);

    /// <summary>Creates one notification per recipient and optionally emails them. Called by event consumers.</summary>
    Task NotifyAsync(long academyId, IEnumerable<long> userIds, NotificationMessage message, CancellationToken ct = default);
}

internal sealed class NotificationService(
    IEngagementDbContext db, ICurrentUser currentUser, IEmailSender email, ILogger<NotificationService> logger) : INotificationService
{
    public async Task<NotificationPageDto> MineAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        var me = Me;
        var p = new PageRequest(page, pageSize);
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == me && (!unreadOnly || !n.IsRead));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.Id).Skip(p.Skip).Take(p.SafePageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.TitleAr, n.TitleEn, n.BodyAr, n.BodyEn, n.Link, n.IsRead, n.CreatedOnUtc))
            .ToListAsync(ct);
        return new NotificationPageDto(
            new PagedResult<NotificationDto> { Items = items, Page = p.SafePage, PageSize = p.SafePageSize, TotalCount = total },
            await UnreadCountAsync(ct));
    }

    public Task<int> UnreadCountAsync(CancellationToken ct = default)
    {
        var me = Me;
        return db.Notifications.CountAsync(n => n.UserId == me && !n.IsRead, ct);
    }

    public async Task MarkReadAsync(long id, CancellationToken ct = default)
    {
        var me = Me;
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == me, ct)
            ?? throw new NotFoundException(nameof(Notification), id);
        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadOnUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var me = Me;
        var unread = await db.Notifications.Where(n => n.UserId == me && !n.IsRead).ToListAsync(ct);
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadOnUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task NotifyAsync(long academyId, IEnumerable<long> userIds, NotificationMessage message, CancellationToken ct = default)
    {
        var recipients = userIds.Distinct().ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        foreach (var userId in recipients)
        {
            db.Notifications.Add(new Notification
            {
                AcademyId = academyId, UserId = userId, Type = message.Type, TitleAr = message.TitleAr, TitleEn = message.TitleEn,
                BodyAr = message.BodyAr, BodyEn = message.BodyEn, Link = message.Link,
            });
        }

        await db.SaveChangesAsync(ct);

        if (!message.AlsoEmail)
        {
            return;
        }

        var emails = await db.People.Where(p => recipients.Contains(p.UserId) && p.IsActive && p.Email != "").Select(p => p.Email).ToListAsync(ct);
        foreach (var to in emails)
        {
            try
            {
                await email.SendAsync(to, $"{message.TitleAr} | {message.TitleEn}", $"{message.BodyAr}\n\n{message.BodyEn}", ct);
            }
            catch (Exception ex)
            {
                // The in-app notification is already saved; one bad mailbox must not fail the batch.
                logger.LogWarning(ex, "Failed to email {Type} notification to {Email}", message.Type, to);
            }
        }
    }

    private long Me => currentUser.UserId ?? throw new UnauthorizedException("Authentication is required.");
}

/// <summary>Turns integration events into notifications (US-035).</summary>
public interface IEventNotifier
{
    Task OnSessionReminderAsync(SessionReminderDue e, CancellationToken ct);
    Task OnAttendanceAsync(AttendanceRecorded e, CancellationToken ct);
    Task OnPaymentDueAsync(PaymentDue e, CancellationToken ct);
    Task OnPaymentRecordedAsync(PaymentRecorded e, CancellationToken ct);
    Task OnSalaryPaidAsync(SalaryPaid e, CancellationToken ct);
    Task OnSessionReportAsync(SessionReportReady e, CancellationToken ct);
    Task OnAutoPayFailedAsync(AutoPayFailed e, CancellationToken ct);
}

internal sealed class EventNotifier(INotificationService notifications) : IEventNotifier
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("ar-SA");

    public Task OnSessionReminderAsync(SessionReminderDue e, CancellationToken ct) =>
        notifications.NotifyAsync(e.AcademyId, e.UserIds, new NotificationMessage(
            "session_reminder",
            $"تذكير: {e.Title}", $"Reminder: {e.Title}",
            $"تبدأ الحصة في {e.StartsAtUtc:yyyy-MM-dd HH:mm} UTC", $"The session starts at {e.StartsAtUtc:yyyy-MM-dd HH:mm} UTC",
            "/", AlsoEmail: true), ct);

    public Task OnAttendanceAsync(AttendanceRecorded e, CancellationToken ct)
    {
        if (e.Status is not ("Absent" or "Late"))
        {
            return Task.CompletedTask;
        }

        var absent = e.Status == "Absent";
        var recipients = e.ParentUserId is { } parent ? new[] { e.StudentUserId, parent } : [e.StudentUserId];
        return notifications.NotifyAsync(e.AcademyId, recipients, new NotificationMessage(
            absent ? "absence" : "late",
            absent ? "تسجيل غياب" : "تسجيل تأخير", absent ? "Absence recorded" : "Late arrival recorded",
            $"حصة {e.SessionStartsAtUtc:yyyy-MM-dd HH:mm} UTC", $"Session on {e.SessionStartsAtUtc:yyyy-MM-dd HH:mm} UTC",
            "/", AlsoEmail: absent), ct);
    }

    /// <summary>Invoices and reminders go to whoever pays (older events: the student and their guardians).</summary>
    public Task OnPaymentDueAsync(PaymentDue e, CancellationToken ct) =>
        notifications.NotifyAsync(e.AcademyId, e.RecipientUserIds ?? e.ParentUserIds.Append(e.StudentUserId).ToList(), new NotificationMessage(
            e.IsOverdue ? "payment_overdue" : "payment_due",
            e.IsOverdue ? $"دفعة متأخرة (شهر {e.MonthNumber})" : $"دفعة مستحقة (شهر {e.MonthNumber})",
            e.IsOverdue ? $"Payment overdue (month {e.MonthNumber})" : $"Payment due (month {e.MonthNumber})",
            $"المبلغ {e.Amount.ToString("0.00", Ar)} {e.Currency} — تاريخ الاستحقاق {e.DueDate:yyyy-MM-dd}",
            $"Amount {e.Amount:0.00} {e.Currency} — due {e.DueDate:yyyy-MM-dd}",
            "/parent", AlsoEmail: true), ct);

    public Task OnPaymentRecordedAsync(PaymentRecorded e, CancellationToken ct)
    {
        var refund = e.Action == "Refunded";
        return notifications.NotifyAsync(e.AcademyId, e.RecipientUserIds ?? e.ParentUserIds.Append(e.StudentUserId).ToList(), new NotificationMessage(
            refund ? "payment_refunded" : "payment_received",
            refund ? "تم استرداد مبلغ" : "تم استلام دفعة", refund ? "Refund issued" : "Payment received",
            $"المبلغ {e.Amount.ToString("0.00", Ar)} {e.Currency}", $"Amount {e.Amount:0.00} {e.Currency}",
            "/parent", AlsoEmail: false), ct);
    }

    public Task OnAutoPayFailedAsync(AutoPayFailed e, CancellationToken ct) =>
        notifications.NotifyAsync(e.AcademyId, e.RecipientUserIds, new NotificationMessage(
            "autopay_failed",
            "تعذّر الدفع التلقائي بالبطاقة", "Automatic card payment failed",
            $"المبلغ {e.Amount.ToString("0.00", Ar)} {e.Currency}. يرجى الدفع يدويًا أو تحديث البطاقة. {e.Reason}".Trim(),
            $"Amount {e.Amount:0.00} {e.Currency}. Please pay by hand or update the card. {e.Reason}".Trim(),
            "/parent", AlsoEmail: true), ct);

    /// <summary>The report of a held session, to the guardian or the adult student, in both languages.</summary>
    public Task OnSessionReportAsync(SessionReportReady e, CancellationToken ct)
    {
        var ar = new List<string> { $"{e.StudentName} — {e.CourseName} — {e.StartsAtUtc:yyyy-MM-dd HH:mm} UTC" };
        var en = new List<string> { $"{e.StudentName} — {e.CourseName} — {e.StartsAtUtc:yyyy-MM-dd HH:mm} UTC" };
        void Add(string? value, string arLabel, string enLabel)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ar.Add($"{arLabel}: {value}");
                en.Add($"{enLabel}: {value}");
            }
        }

        Add(e.Attendance switch { "Present" => "حاضر", "Late" => "متأخر", "Absent" => "غائب", _ => null }, "الحضور", "Attendance");
        Add(e.Rating is { } r ? $"{r}/5" : null, "التقييم", "Rating");
        Add(e.Accomplished, "ما تم إنجازه", "Covered");
        Add(e.Memorization, "الحفظ", "Memorisation");
        Add(e.Revision, "المراجعة", "Revision");
        Add(e.Mistakes?.ToString(CultureInfo.InvariantCulture), "الأخطاء", "Mistakes");
        Add(e.Homework, "الواجب", "Homework");
        Add(e.Comment, "ملاحظات المعلم", "Teacher's notes");
        if (!string.IsNullOrWhiteSpace(e.TeacherName))
        {
            ar.Add($"المعلم: {e.TeacherName}");
            en.Add($"Teacher: {e.TeacherName}");
        }

        static string Fit(IEnumerable<string> lines) => string.Join('\n', lines) is { Length: > 1000 } text ? text[..997] + "..." : string.Join('\n', lines);
        return notifications.NotifyAsync(e.AcademyId, e.RecipientUserIds, new NotificationMessage(
            "session_report",
            $"تقرير حصة: {e.StudentName}", $"Session report: {e.StudentName}",
            Fit(ar), Fit(en), "/", AlsoEmail: true), ct);
    }

    public Task OnSalaryPaidAsync(SalaryPaid e, CancellationToken ct) =>
        notifications.NotifyAsync(e.AcademyId, [e.UserId], new NotificationMessage(
            "salary_paid",
            $"تم صرف راتب {e.Year}-{e.Month:00}", $"Salary for {e.Year}-{e.Month:00} paid",
            $"المبلغ {e.Amount.ToString("0.00", Ar)}", $"Amount {e.Amount:0.00}",
            "/me", AlsoEmail: false), ct);
}

// ---------- Dashboards (US-038) ----------

public sealed record DashboardDto(DateOnly From, DateOnly To, string Scope, JsonElement Academic, JsonElement? Finance);

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>
/// KPIs and chart data. Admins get academic and finance data. A supervisor gets academic data
/// for their own teachers only. Results are cached in Redis for 5 minutes per scope and
/// period. Gated by the plan's analytics feature.
/// </summary>
internal sealed class DashboardService(
    IStatsClient stats, ICacheService cache, ICurrentUser user, IEntitlementsProvider entitlements) : IDashboardService
{
    public async Task<DashboardDto> GetAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 400)
        {
            throw new BusinessRuleException("Choose a period of up to about a year.");
        }

        var academyId = user.AcademyId ?? throw new ForbiddenAccessException("Dashboards belong to an academy.");
        await entitlements.EnsureFeatureAsync(academyId, FeatureKeys.Analytics, ct);

        var isAdmin = user.IsSuperAdmin || user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Manager);
        long? supervisor = !isAdmin && user.IsInRole(Roles.Supervisor) ? user.UserId : null;
        if (!isAdmin && supervisor is null)
        {
            throw new ForbiddenAccessException("Dashboards are for admins and supervisors.");
        }

        var withFinance = user.HasPermission(Permissions.Reports.View);
        var scope = supervisor is { } s ? $"supervisor:{s}" : "academy";
        var key = $"dashboard:{academyId}:{scope}:{withFinance}:{from:yyyyMMdd}:{to:yyyyMMdd}";

        return await cache.GetOrCreateAsync(key, async token =>
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var academic = await stats.AcademicAsync(academyId, fromUtc, toUtc, supervisor, token);
            JsonElement? finance = withFinance ? await stats.FinanceAsync(academyId, from, to, token) : null;
            return new DashboardDto(from, to, scope, academic, finance);
        }, TimeSpan.FromMinutes(5), ct);
    }
}

// ---------- Audit log (US-043) ----------

public sealed record AuditLogDto(
    long Id, long? AcademyId, long? UserId, string? UserName, string Service, string Action, string EntityName, string? EntityId,
    string? DataJson, DateTime OccurredOnUtc);

public sealed record AuditLogQuery(
    string? Service = null, string? Action = null, long? UserId = null, long? AcademyId = null, DateOnly? From = null, DateOnly? To = null,
    int Page = 1, int PageSize = 50);

public interface IAuditLogService
{
    Task RecordAsync(AuditEvent e, CancellationToken ct = default);
    Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct = default);
}

internal sealed class AuditLogService(IEngagementDbContext db, ICurrentUser user) : IAuditLogService
{
    public async Task RecordAsync(AuditEvent e, CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            AcademyId = e.AcademyId, UserId = e.UserId, Service = e.Service, Action = e.Action, EntityName = e.EntityName,
            EntityId = e.EntityId, DataJson = e.DataJson is { Length: > 8000 } json ? json[..8000] : e.DataJson, OccurredOnUtc = e.OccurredOnUtc,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>An academy admin sees their academy's trail; SuperAdmin sees everything.</summary>
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.AuditLogs.AsNoTracking().AsQueryable();

        if (user.IsSuperAdmin && query.AcademyId is { } academyId)
        {
            q = q.Where(a => a.AcademyId == academyId);
        }

        if (!string.IsNullOrWhiteSpace(query.Service))
        {
            q = q.Where(a => a.Service == query.Service);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            q = q.Where(a => a.Action.StartsWith(query.Action));
        }

        if (query.UserId is { } uid)
        {
            q = q.Where(a => a.UserId == uid);
        }

        if (query.From is { } from)
        {
            var f = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(a => a.OccurredOnUtc >= f);
        }

        if (query.To is { } to)
        {
            var t = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(a => a.OccurredOnUtc < t);
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.OccurredOnUtc).ThenByDescending(a => a.Id).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = await db.People.IgnoreQueryFilters().Where(p => userIds.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.FullName, ct);

        return new PagedResult<AuditLogDto>
        {
            Items = rows.Select(a => new AuditLogDto(
                a.Id, a.AcademyId, a.UserId, a.UserId is { } u ? names.GetValueOrDefault(u) : null, a.Service, a.Action, a.EntityName,
                a.EntityId, a.DataJson, a.OccurredOnUtc)).ToList(),
            Page = page.SafePage,
            PageSize = page.SafePageSize,
            TotalCount = total,
        };
    }
}
