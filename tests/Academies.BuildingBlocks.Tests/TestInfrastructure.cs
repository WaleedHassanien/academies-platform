using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Domain;
using Academies.BuildingBlocks.Infrastructure.Caching;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.BuildingBlocks.Tests;

public sealed class TenantNote : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class GlobalSetting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
}

/// <summary>
/// A caller the test can switch between queries. Permissions default to the role's seeded
/// set (<see cref="RolePermissionDefaults"/>), the same as a real token.
/// </summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public long? UserId { get; set; }
    public long? AcademyId { get; set; }
    public List<string> RoleList { get; } = [];
    public List<string> PermissionList { get; } = [];

    public bool IsAuthenticated => UserId is not null;
    public IReadOnlyCollection<string> Roles => RoleList;
    public IReadOnlyCollection<string> Permissions => PermissionList;
    public bool IsSuperAdmin => RoleList.Contains(Contracts.Security.Roles.SuperAdmin);
    public bool IsInRole(string role) => RoleList.Contains(role);
    public bool HasPermission(string permission) => IsSuperAdmin || PermissionList.Contains(permission);

    public FakeCurrentUser As(long userId, long? academyId, params string[] roles)
    {
        UserId = userId;
        AcademyId = academyId;
        RoleList.Clear();
        RoleList.AddRange(roles);
        PermissionList.Clear();
        PermissionList.AddRange(roles.SelectMany(r => RolePermissionDefaults.Map.GetValueOrDefault(r) ?? []).Distinct());
        return this;
    }
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser)
{
    protected override string ServiceName => "test";

    public DbSet<TenantNote> Notes => Set<TenantNote>();
    public DbSet<GlobalSetting> Settings => Set<GlobalSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantNote>();
        modelBuilder.Entity<GlobalSetting>();
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>An in-memory SQLite database that lives as long as the fixture.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDatabase()
    {
        _connection.Open();
        using var db = CreateContext(new FakeCurrentUser());
        db.Database.EnsureCreated();
    }

    public TestDbContext CreateContext(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new AuditableTenantInterceptor(user, TimeProvider.System))
                .Options,
            user);

    public void Dispose() => _connection.Dispose();
}

// ---------- Service test harness ----------

public sealed class CapturingEventPublisher : IEventPublisher
{
    public List<object> Published { get; } = [];

    public Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class
    {
        Published.Add(message);
        return Task.CompletedTask;
    }

    public IEnumerable<T> OfType<T>() => Published.OfType<T>();
}

public sealed class NullAuditTrail : IAuditTrail
{
    public Task RecordAsync(string action, string entityName, object? entityId, object? data = null, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Plan entitlements a test can change. Defaults to "everything on, no limits".</summary>
public sealed class FakeEntitlements : IEntitlementsProvider
{
    public Entitlements Value { get; set; } = new(
        1, "TEST", "Test", SubscriptionStatuses.Active, null,
        LimitKeys.All.ToDictionary(k => k, _ => Entitlements.Unlimited),
        FeatureKeys.All.ToDictionary(k => k, _ => true));

    public Task<Entitlements> GetAsync(long academyId, CancellationToken ct = default) => Task.FromResult(Value with { AcademyId = academyId });
}

public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// DI container for exercising one service's Application layer against its real DbContext on
/// in-memory SQLite, with outside dependencies faked: events, audit, entitlements and time.
/// </summary>
public sealed class ServiceHarness<TContext> : IAsyncDisposable where TContext : ServiceDbContext
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _services;

    public FakeCurrentUser User { get; } = new();
    public CapturingEventPublisher Events { get; } = new();
    public FakeEntitlements Entitlements { get; } = new();
    public MutableTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));

    public ServiceHarness(Action<IServiceCollection> configure)
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ICurrentUser>(User);
        services.AddSingleton<IEventPublisher>(Events);
        services.AddSingleton<IAuditTrail, NullAuditTrail>();
        services.AddSingleton<IEntitlementsProvider>(Entitlements);
        services.AddPlatformCaching(new ConfigurationBuilder().Build(), "test");
        services.AddScoped<AuditableTenantInterceptor>();
        services.AddDbContext<TContext>((sp, o) => o
            .UseSqlite(_connection)
            .AddInterceptors(sp.GetRequiredService<AuditableTenantInterceptor>()));
        configure(services);
        _services = services.BuildServiceProvider();

        using var scope = _services.CreateScope();
        AsPlatform(() => scope.ServiceProvider.GetRequiredService<TContext>().Database.EnsureCreated());
    }

    /// <summary>Runs <paramref name="work"/> with a fresh scope (like one HTTP request).</summary>
    public async Task<T> RunAsync<TService, T>(Func<TService, Task<T>> work) where TService : notnull
    {
        await using var scope = _services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public Task RunAsync<TService>(Func<TService, Task> work) where TService : notnull =>
        RunAsync<TService, int>(async s => { await work(s); return 0; });

    /// <summary>Seeds data as a platform user (tenant filter off), then restores the caller.</summary>
    public async Task SeedAsync(Func<TContext, Task> seed)
    {
        var (id, academy, roles) = (User.UserId, User.AcademyId, User.RoleList.ToArray());
        User.As(0, null, Contracts.Security.Roles.SuperAdmin);
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            await seed(db);
            await db.SaveChangesAsync();
        }
        finally
        {
            User.As(id ?? 0, academy, roles);
            User.UserId = id;
        }
    }

    private void AsPlatform(Action action)
    {
        User.As(0, null, Contracts.Security.Roles.SuperAdmin);
        action();
        User.As(0, null);
        User.UserId = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

public static class PeopleSeed
{
    public static Person Person(long academyId, long userId, string name, params string[] roles) => new()
    {
        AcademyId = academyId, UserId = userId, FullName = name, Email = $"u{userId}@test.local", Roles = string.Join(',', roles),
    };
}
