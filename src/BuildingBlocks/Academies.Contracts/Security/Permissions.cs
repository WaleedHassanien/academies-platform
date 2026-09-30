using System.Reflection;

namespace Academies.Contracts.Security;

/// <summary>
/// Permission codes carried in the <c>perm</c> claim and checked by <c>[HasPermission]</c> (US-011).
/// Mirrored in client/src/app/core/auth/permissions.ts; keep both in sync.
/// </summary>
public static class Permissions
{
    public static class Academies
    {
        public const string View = "academies.view";
        public const string Manage = "academies.manage";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Manage = "users.manage";
    }

    public static class Plans
    {
        public const string View = "plans.view";
        public const string Manage = "plans.manage";
    }

    public static class Profiles
    {
        public const string View = "profiles.view";
        public const string Manage = "profiles.manage";
    }

    public static class Courses
    {
        public const string View = "courses.view";
        public const string Manage = "courses.manage";
    }

    public static class Sessions
    {
        public const string View = "sessions.view";
        public const string Manage = "sessions.manage";
    }

    public static class Attendance
    {
        public const string View = "attendance.view";
        public const string Record = "attendance.record";
    }

    public static class Feedback
    {
        public const string View = "feedback.view";
        public const string Write = "feedback.write";
    }

    public static class Payments
    {
        public const string View = "payments.view";
        public const string Manage = "payments.manage";
    }

    public static class Salaries
    {
        public const string View = "salaries.view";
        public const string Manage = "salaries.manage";
    }

    public static class Expenses
    {
        public const string Manage = "expenses.manage";
    }

    public static class Reports
    {
        public const string View = "reports.view";
    }

    public static class Dashboards
    {
        public const string View = "dashboards.view";
    }

    public static class AuditLogs
    {
        public const string View = "auditlogs.view";
    }

    /// <summary>Sales: prospective students, trial sessions and converting them into students.</summary>
    public static class Leads
    {
        public const string View = "leads.view";
        public const string Manage = "leads.manage";
    }

    /// <summary>Every permission code declared above.</summary>
    public static readonly IReadOnlyList<string> All = typeof(Permissions)
        .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();
}
