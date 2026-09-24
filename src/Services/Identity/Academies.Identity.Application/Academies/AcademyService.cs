using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Identity.Application.Abstractions;
using Academies.Identity.Domain.Academies;
using Academies.Identity.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Identity.Application.Academies;

public sealed record AcademyDto(long Id, string Name, string? LogoUrl, string? Address, string Status, DateTime CreatedOnUtc, int UserCount);

public sealed record AcademyQuery(string? Search = null, string? Status = null, int Page = 1, int PageSize = 20);

/// <summary>Optionally creates the academy's first Admin in the same call.</summary>
public sealed record CreateAcademyRequest(
    string Name, string? Address, string? LogoUrl, string? AdminFullName, string? AdminEmail, string? AdminPassword);

public sealed record UpdateAcademyRequest(string Name, string? Address, string? LogoUrl);

public sealed record SetAcademyStatusRequest(AcademyStatus Status);

public interface IAcademyService
{
    Task<PagedResult<AcademyDto>> ListAsync(AcademyQuery query, CancellationToken ct = default);
    Task<AcademyDto> GetAsync(long id, CancellationToken ct = default);
    Task<AcademyDto> CreateAsync(CreateAcademyRequest request, CancellationToken ct = default);
    Task<AcademyDto> UpdateAsync(long id, UpdateAcademyRequest request, CancellationToken ct = default);
    Task<AcademyDto> SetStatusAsync(long id, AcademyStatus status, CancellationToken ct = default);
}

/// <summary>
/// Platform-level academy management (US-018). Plan assignment and trial extension live in
/// the Subscription service. A new academy starts a trial there when it receives <see cref="AcademyCreated"/>.
/// </summary>
internal sealed class AcademyService(
    IIdentityDbContext db, IPasswordHasher hasher, IEventPublisher events, IAuditTrail audit) : IAcademyService
{
    public async Task<PagedResult<AcademyDto>> ListAsync(AcademyQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.Academies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            q = q.Where(a => a.Name.Contains(query.Search));
        }

        if (Enum.TryParse<AcademyStatus>(query.Status, true, out var status))
        {
            q = q.Where(a => a.Status == status);
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.Id)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(a => new AcademyDto(
                a.Id, a.Name, a.LogoUrl, a.Address, a.Status.ToString(), a.CreatedOnUtc,
                db.Users.Count(u => u.AcademyId == a.Id)))
            .ToListAsync(ct);

        return new PagedResult<AcademyDto> { Items = items, Page = page.SafePage, PageSize = page.SafePageSize, TotalCount = total };
    }

    public async Task<AcademyDto> GetAsync(long id, CancellationToken ct = default)
    {
        var academy = await db.Academies.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Academy", id);
        return ToDto(academy, await db.Users.CountAsync(u => u.AcademyId == id, ct));
    }

    public async Task<AcademyDto> CreateAsync(CreateAcademyRequest request, CancellationToken ct = default)
    {
        if (await db.Academies.AnyAsync(a => a.Name == request.Name.Trim(), ct))
        {
            throw new ConflictException($"An academy named '{request.Name}' already exists.");
        }

        var academy = new Academy { Name = request.Name.Trim(), Address = request.Address, LogoUrl = request.LogoUrl };
        db.Academies.Add(academy);
        await db.SaveChangesAsync(ct);

        User? admin = null;
        if (!string.IsNullOrWhiteSpace(request.AdminEmail))
        {
            var normalized = User.Normalize(request.AdminEmail);
            if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalized, ct))
            {
                throw new ConflictException($"The email '{request.AdminEmail}' is already registered.");
            }

            var adminRole = await db.Roles.SingleAsync(r => r.Name == Roles.Admin, ct);
            admin = new User
            {
                AcademyId = academy.Id,
                Email = request.AdminEmail.Trim(),
                NormalizedEmail = normalized,
                FullName = request.AdminFullName?.Trim() ?? request.AdminEmail.Trim(),
            };
            admin.PasswordHash = hasher.Hash(admin, request.AdminPassword!);
            admin.UserRoles.Add(new UserRole { Role = adminRole });
            db.Users.Add(admin);
            await db.SaveChangesAsync(ct);
        }

        await events.PublishAsync(new AcademyCreated(academy.Id, academy.Name), ct);
        if (admin is not null)
        {
            await events.PublishAsync(new UserCreated(admin.Id, academy.Id, admin.Email, admin.FullName, [Roles.Admin]), ct);
        }

        await audit.RecordAsync("academies.create", nameof(Academy), academy.Id, new { academy.Name, AdminEmail = admin?.Email }, ct);
        await db.SaveChangesAsync(ct);

        return ToDto(academy, admin is null ? 0 : 1);
    }

    public async Task<AcademyDto> UpdateAsync(long id, UpdateAcademyRequest request, CancellationToken ct = default)
    {
        var academy = await db.Academies.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Academy", id);
        academy.Name = request.Name.Trim();
        academy.Address = request.Address;
        academy.LogoUrl = request.LogoUrl;
        await audit.RecordAsync("academies.update", nameof(Academy), id, request, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<AcademyDto> SetStatusAsync(long id, AcademyStatus status, CancellationToken ct = default)
    {
        var academy = await db.Academies.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("Academy", id);
        if (academy.Status != status)
        {
            academy.Status = status;
            await events.PublishAsync(new AcademyStatusChanged(id, status.ToString()), ct);
            await audit.RecordAsync("academies.status", nameof(Academy), id, new { Status = status.ToString() }, ct);
            await db.SaveChangesAsync(ct);
        }

        return await GetAsync(id, ct);
    }

    private static AcademyDto ToDto(Academy a, int userCount) =>
        new(a.Id, a.Name, a.LogoUrl, a.Address, a.Status.ToString(), a.CreatedOnUtc, userCount);
}

internal sealed class CreateAcademyValidator : AbstractValidator<CreateAcademyRequest>
{
    public CreateAcademyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.LogoUrl).MaximumLength(500);
        When(x => !string.IsNullOrWhiteSpace(x.AdminEmail), () =>
        {
            RuleFor(x => x.AdminEmail).EmailAddress().MaximumLength(256);
            RuleFor(x => x.AdminPassword!).SetValidator(new PasswordValidator());
        });
    }
}

internal sealed class UpdateAcademyValidator : AbstractValidator<UpdateAcademyRequest>
{
    public UpdateAcademyValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.LogoUrl).MaximumLength(500);
    }
}

/// <summary>Shared password policy: 8+ chars with upper, lower and digit.</summary>
internal sealed class PasswordValidator : AbstractValidator<string>
{
    public PasswordValidator()
    {
        RuleFor(x => x).NotEmpty().MinimumLength(8).MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.")
            .OverridePropertyName("Password");
    }
}
