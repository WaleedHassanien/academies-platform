namespace Academies.BuildingBlocks.Application.Abstractions;

/// <summary>The caller of the current request, read from the JWT.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    long? UserId { get; }
    long? AcademyId { get; }
    IReadOnlyCollection<string> Roles { get; }
    IReadOnlyCollection<string> Permissions { get; }
    bool IsSuperAdmin { get; }

    bool IsInRole(string role);
    bool HasPermission(string permission);
}
