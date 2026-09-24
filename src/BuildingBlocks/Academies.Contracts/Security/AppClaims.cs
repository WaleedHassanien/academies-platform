namespace Academies.Contracts.Security;

/// <summary>JWT claim names issued by Identity and read by every service.</summary>
public static class AppClaims
{
    public const string UserId = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string AcademyId = "academy_id";
    public const string Role = "role";
    public const string Permission = "perm";
}
