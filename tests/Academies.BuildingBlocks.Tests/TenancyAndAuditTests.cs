using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.Contracts.Security;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Academies.BuildingBlocks.Tests;

public sealed class TenancyAndAuditTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FakeCurrentUser _user = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Queries_only_return_rows_of_the_callers_academy()   // US-008
    {
        await using var db = _database.CreateContext(_user);

        _user.As(userId: 1, academyId: 10);
        db.Notes.Add(new TenantNote { Text = "academy 10" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _user.As(userId: 2, academyId: 20);
        db.Notes.Add(new TenantNote { Text = "academy 20" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Same context instance: the filter must re-read the caller on every query.
        _user.As(userId: 1, academyId: 10);
        var seenBy10 = await db.Notes.AsNoTracking().Select(n => n.Text).ToListAsync(TestContext.Current.CancellationToken);
        seenBy10.ShouldBe(["academy 10"]);

        _user.As(userId: 2, academyId: 20);
        var seenBy20 = await db.Notes.AsNoTracking().Select(n => n.Text).ToListAsync(TestContext.Current.CancellationToken);
        seenBy20.ShouldBe(["academy 20"]);
    }

    [Fact]
    public async Task Caller_without_academy_sees_no_tenant_rows()
    {
        await using var db = _database.CreateContext(_user.As(1, 10));
        db.Notes.Add(new TenantNote { Text = "x" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _user.As(userId: 5, academyId: null);
        (await db.Notes.AsNoTracking().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task SuperAdmin_sees_every_academy()
    {
        await using var db = _database.CreateContext(_user.As(1, 10));
        db.Notes.Add(new TenantNote { Text = "a" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _user.As(2, 20);
        db.Notes.Add(new TenantNote { Text = "b" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _user.As(99, null, Roles.SuperAdmin);
        (await db.Notes.AsNoTracking().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task Insert_stamps_academy_and_audit_fields()   // US-003a
    {
        await using var db = _database.CreateContext(_user.As(userId: 7, academyId: 10));
        var before = DateTime.UtcNow.AddSeconds(-1);

        var note = new TenantNote { Text = "hello" };
        db.Notes.Add(note);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        note.AcademyId.ShouldBe(10);
        note.CreatedBy.ShouldBe(7);
        note.CreatedOnUtc.ShouldBeGreaterThan(before);
        note.UpdatedOnUtc.ShouldBeNull();
        note.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task Update_stamps_modifier_and_cannot_move_row_to_another_academy()
    {
        await using var db = _database.CreateContext(_user.As(userId: 7, academyId: 10));
        var note = new TenantNote { Text = "v1" };
        db.Notes.Add(note);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _user.As(userId: 8, academyId: 10);
        note.Text = "v2";
        note.AcademyId = 20;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stored = await db.Notes.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        stored.Text.ShouldBe("v2");
        stored.AcademyId.ShouldBe(10);
        stored.UpdatedBy.ShouldBe(8);
        stored.UpdatedOnUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task Insert_into_another_academy_is_refused()
    {
        await using var db = _database.CreateContext(_user.As(userId: 7, academyId: 10));
        db.Notes.Add(new TenantNote { Text = "sneaky", AcademyId = 20 });

        await Should.ThrowAsync<ForbiddenAccessException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_is_soft_and_hidden_by_default()   // US-003a
    {
        await using var db = _database.CreateContext(_user.As(userId: 7, academyId: 10));
        var setting = new GlobalSetting { Key = "k" };
        db.Settings.Add(setting);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Settings.Remove(setting);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await db.Settings.AsNoTracking().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        var raw = await db.Settings.AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken);
        raw.IsDeleted.ShouldBeTrue();
        raw.UpdatedBy.ShouldBe(7);
    }

    [Fact]
    public async Task Tenant_filter_also_hides_soft_deleted_rows()
    {
        await using var db = _database.CreateContext(_user.As(1, 10));
        var keep = new TenantNote { Text = "keep" };
        var gone = new TenantNote { Text = "gone" };
        db.Notes.AddRange(keep, gone);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.Notes.Remove(gone);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var texts = await db.Notes.AsNoTracking().Select(n => n.Text).ToListAsync(TestContext.Current.CancellationToken);
        texts.ShouldBe(["keep"]);
    }
}
