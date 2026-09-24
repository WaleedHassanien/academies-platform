namespace Academies.Contracts.Security;

/// <summary>
/// Default permission set per role, seeded by Identity. SuperAdmin is not listed: it bypasses
/// permission checks and tenant filters entirely.
/// </summary>
public static class RolePermissionDefaults
{
    private static readonly string[] PlatformOnly = [Permissions.Academies.Manage, Permissions.Plans.Manage];

    public static readonly IReadOnlyDictionary<string, string[]> Map = new Dictionary<string, string[]>
    {
        [Roles.Admin] = Permissions.All.Except(PlatformOnly).ToArray(),
        [Roles.Manager] =
        [
            Permissions.Users.View, Permissions.Profiles.View, Permissions.Profiles.Manage,
            Permissions.Courses.View, Permissions.Courses.Manage, Permissions.Sessions.View,
            Permissions.Sessions.Manage, Permissions.Attendance.View, Permissions.Feedback.View,
            Permissions.Reports.View, Permissions.Dashboards.View,
        ],
        [Roles.Supervisor] =
        [
            Permissions.Profiles.View, Permissions.Sessions.View, Permissions.Attendance.View,
            Permissions.Feedback.View, Permissions.Dashboards.View, Permissions.Salaries.View,
        ],
        [Roles.Teacher] =
        [
            Permissions.Courses.View, Permissions.Sessions.View, Permissions.Attendance.View,
            Permissions.Attendance.Record, Permissions.Feedback.View, Permissions.Feedback.Write,
            Permissions.Salaries.View,
        ],
        [Roles.Student] = [Permissions.Courses.View, Permissions.Sessions.View, Permissions.Feedback.View, Permissions.Payments.View],
        [Roles.Parent] = [Permissions.Attendance.View, Permissions.Feedback.View, Permissions.Payments.View],
        [Roles.Accountant] =
        [
            Permissions.Profiles.View, Permissions.Payments.View, Permissions.Payments.Manage,
            Permissions.Salaries.View, Permissions.Salaries.Manage, Permissions.Expenses.Manage,
            Permissions.Reports.View,
        ],
        [Roles.Staff] = [Permissions.Salaries.View],
    };
}
