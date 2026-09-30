namespace Academies.Contracts.Security;

/// <summary>System roles seeded by Identity (US-009).</summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Supervisor = "Supervisor";
    public const string Teacher = "Teacher";
    public const string Student = "Student";
    public const string Parent = "Parent";
    public const string Accountant = "Accountant";
    public const string Staff = "Staff";

    /// <summary>Sales and customer service: leads, trial sessions and new sign-ups.</summary>
    public const string Sales = "Sales";

    public static readonly IReadOnlyList<string> All =
        [SuperAdmin, Admin, Manager, Supervisor, Teacher, Student, Parent, Accountant, Staff, Sales];
}
