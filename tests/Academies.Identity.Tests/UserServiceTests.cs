using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Tests;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using Academies.Identity.Application;
using Academies.Identity.Application.Abstractions;
using Academies.Identity.Application.Users;
using Academies.Identity.Domain.Academies;
using Academies.Identity.Domain.Roles;
using Academies.Identity.Infrastructure.Persistence;
using Academies.Identity.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Academies.Identity.Tests;

/// <summary>User management inside an academy (US-019) with plan limits (US-017).</summary>
public sealed class UserServiceTests : IAsyncLifetime
{
    private ServiceHarness<IdentityDbContext> _h = null!;
    private long _academyId, _otherAcademyId;

    public async ValueTask InitializeAsync()
    {
        _h = new ServiceHarness<IdentityDbContext>(s =>
        {
            s.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<IdentityDbContext>());
            s.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
            s.AddIdentityApplication();
        });

        await _h.SeedAsync(async db =>
        {
            db.Roles.AddRange(Roles.All.Select(r => new Role { Name = r, NormalizedName = r.ToUpperInvariant(), IsSystem = true }));
            var academy = new Academy { Name = "Noor" };
            var other = new Academy { Name = "Other" };
            db.Academies.AddRange(academy, other);
            await db.SaveChangesAsync();
            _academyId = academy.Id;
            _otherAcademyId = other.Id;
        });

        _h.User.As(1, _academyId, Roles.Admin);
        _h.Entitlements.Value = _h.Entitlements.Value with
        {
            Limits = new Dictionary<string, int> { [LimitKeys.Users] = 10, [LimitKeys.Students] = 2, [LimitKeys.Teachers] = 1 },
        };
    }

    public async ValueTask DisposeAsync() => await _h.DisposeAsync();

    private static CreateUserRequest NewUser(string email, params string[] roles) => new(email, email, null, "Passw0rd!", roles);

    [Fact]
    public async Task Creating_a_user_publishes_UserCreated_in_the_admins_academy()
    {
        var user = await _h.RunAsync<IUserService, UserDto>(s => s.CreateAsync(NewUser("s1@noor.test", Roles.Student)));

        user.AcademyId.ShouldBe(_academyId);
        var created = _h.Events.OfType<UserCreated>().ShouldHaveSingleItem();
        created.UserId.ShouldBe(user.Id);
        created.Roles.ShouldBe([Roles.Student]);
    }

    [Fact]
    public async Task Student_limit_is_enforced_with_an_upgrade_hint()   // US-017
    {
        await _h.RunAsync<IUserService>(s => s.CreateAsync(NewUser("s1@noor.test", Roles.Student)));
        await _h.RunAsync<IUserService>(s => s.CreateAsync(NewUser("s2@noor.test", Roles.Student)));

        var error = await Should.ThrowAsync<BusinessRuleException>(() => _h.RunAsync<IUserService>(s => s.CreateAsync(NewUser("s3@noor.test", Roles.Student))));
        error.Message.ShouldContain("2/2");
        error.Code.ShouldBe("plan.limit_reached");

        // Other roles still fit.
        await _h.RunAsync<IUserService>(s => s.CreateAsync(NewUser("t1@noor.test", Roles.Teacher)));

        var usage = await _h.RunAsync<IUserService, AcademyUsageDto>(s => s.GetUsageAsync(null));
        usage.Items.Single(i => i.Key == LimitKeys.Students).ShouldSatisfyAllConditions(
            i => i.Used.ShouldBe(2), i => i.Limit.ShouldBe(2), i => i.Percent.ShouldBe(100));
    }

    [Fact]
    public async Task Adding_a_limited_role_to_an_existing_user_is_checked_too()
    {
        await _h.RunAsync<IUserService>(s => s.CreateAsync(NewUser("t1@noor.test", Roles.Teacher)));
        var staff = await _h.RunAsync<IUserService, UserDto>(s => s.CreateAsync(NewUser("x@noor.test", Roles.Staff)));

        await Should.ThrowAsync<BusinessRuleException>(() =>
            _h.RunAsync<IUserService>(s => s.UpdateAsync(staff.Id, new UpdateUserRequest("X", null, [Roles.Staff, Roles.Teacher]))));
    }

    [Fact]
    public async Task Admin_cannot_hand_out_SuperAdmin_or_see_other_academies()
    {
        await Should.ThrowAsync<BusinessRuleException>(() => _h.RunAsync<IUserService>(s => s.CreateAsync(NewUser("boss@noor.test", Roles.SuperAdmin))));

        _h.User.As(2, _otherAcademyId, Roles.Admin);
        await _h.RunAsync<IUserService>(s => s.CreateAsync(NewUser("o1@other.test", Roles.Student)));

        _h.User.As(1, _academyId, Roles.Admin);
        var list = await _h.RunAsync<IUserService, BuildingBlocks.Application.Models.PagedResult<UserDto>>(s => s.ListAsync(new UserQuery()));
        list.Items.ShouldAllBe(u => u.AcademyId == _academyId);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        await _h.RunAsync<IUserService>(s => s.CreateAsync(NewUser("dup@noor.test", Roles.Parent)));
        await Should.ThrowAsync<ConflictException>(() => _h.RunAsync<IUserService>(s => s.CreateAsync(NewUser("DUP@noor.test", Roles.Parent))));
    }
}
