using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Tests;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using Academies.Subscription.Application;
using Academies.Subscription.Domain;
using Academies.Subscription.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Academies.Subscription.Tests;

public sealed class SubscriptionTests : IAsyncLifetime
{
    private const long Academy = 7;
    private ServiceHarness<SubscriptionDbContext> _h = null!;
    private long _startPlanId;

    public async ValueTask InitializeAsync()
    {
        _h = new ServiceHarness<SubscriptionDbContext>(s =>
        {
            s.AddScoped<ISubscriptionDbContext>(sp => sp.GetRequiredService<SubscriptionDbContext>());
            s.AddSubscriptionApplication();
        });

        await _h.SeedAsync(async db =>
        {
            db.Plans.Add(Plan("FREE", 0, 14, students: 20, teachers: 3, users: 30));
            var start = Plan("START", 299, 0, students: 100, teachers: 10, users: 130, FeatureKeys.OnlineSessions);
            db.Plans.Add(start);
            await db.SaveChangesAsync();
            _startPlanId = start.Id;
        });

        _h.User.As(1, null, Roles.SuperAdmin);
    }

    public async ValueTask DisposeAsync() => await _h.DisposeAsync();

    [Fact]
    public async Task New_academy_starts_on_the_free_trial()   // US-014, US-015
    {
        var e = await _h.RunAsync<IEntitlementsService, Entitlements>(s => s.GetAsync(Academy));

        e.PlanCode.ShouldBe("FREE");
        e.Status.ShouldBe(SubscriptionStatuses.Trial);
        e.TrialEndsOnUtc.ShouldBe(_h.Clock.Now.UtcDateTime.AddDays(14));
        e.LimitFor(LimitKeys.Students).ShouldBe(20);
        _h.Events.OfType<AcademySubscriptionChanged>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Academy_override_beats_the_plan_limit()   // US-015
    {
        await _h.RunAsync<IAcademySubscriptionService>(s => s.AssignPlanAsync(Academy, new AssignPlanRequest(_startPlanId, null)));
        await _h.RunAsync<IAcademySubscriptionService>(s => s.SetOverrideAsync(Academy, LimitKeys.Students, new SetOverrideRequest(150, "Big school")));

        var e = await _h.RunAsync<IEntitlementsService, Entitlements>(s => s.GetAsync(Academy));
        e.LimitFor(LimitKeys.Students).ShouldBe(150);
        e.LimitFor(LimitKeys.Teachers).ShouldBe(10);
        e.HasFeature(FeatureKeys.OnlineSessions).ShouldBeTrue();

        await _h.RunAsync<IAcademySubscriptionService>(s => s.RemoveOverrideAsync(Academy, LimitKeys.Students));
        (await _h.RunAsync<IEntitlementsService, Entitlements>(s => s.GetAsync(Academy))).LimitFor(LimitKeys.Students).ShouldBe(100);
    }

    [Fact]
    public async Task Changing_a_plan_limit_applies_to_all_its_academies_and_logs_price()   // US-016
    {
        await _h.RunAsync<IAcademySubscriptionService>(s => s.AssignPlanAsync(Academy, new AssignPlanRequest(_startPlanId, null)));
        (await _h.RunAsync<IEntitlementsService, Entitlements>(s => s.GetAsync(Academy))).LimitFor(LimitKeys.Students).ShouldBe(100);

        var plan = await _h.RunAsync<IPlanService, PlanDto>(s => s.GetAsync(_startPlanId));
        await _h.RunAsync<IPlanService>(s => s.UpdateAsync(_startPlanId, new SavePlanRequest(
            plan.Code, plan.Name, 349, 0, true, 2,
            new Dictionary<string, int>(plan.Limits) { [LimitKeys.Students] = 120 }, plan.Features)));

        (await _h.RunAsync<IEntitlementsService, Entitlements>(s => s.GetAsync(Academy))).LimitFor(LimitKeys.Students).ShouldBe(120);
        var history = await _h.RunAsync<IPlanService, IReadOnlyList<PriceHistoryDto>>(s => s.PriceHistoryAsync(_startPlanId));
        history.ShouldHaveSingleItem().NewPrice.ShouldBe(349);
    }

    [Fact]
    public async Task Trial_expires_and_blocks_additions()   // US-017
    {
        await _h.RunAsync<IEntitlementsService>(s => s.GetAsync(Academy));
        _h.Clock.Now = _h.Clock.Now.AddDays(15);
        await _h.RunAsync<IEntitlementsService>(s => s.InvalidateAsync(Academy));

        var e = await _h.RunAsync<IEntitlementsService, Entitlements>(s => s.GetAsync(Academy));
        e.Status.ShouldBe(SubscriptionStatuses.Expired);

        var provider = new FixedEntitlements(e);
        await Should.ThrowAsync<BusinessRuleException>(() => provider.EnsureWithinLimitAsync(Academy, LimitKeys.Students, 1, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Limit_check_rejects_going_over_the_cap()   // US-017
    {
        var e = await _h.RunAsync<IEntitlementsService, Entitlements>(s => s.GetAsync(Academy));
        var provider = new FixedEntitlements(e);

        await provider.EnsureWithinLimitAsync(Academy, LimitKeys.Students, currentCount: 19, ct: TestContext.Current.CancellationToken);
        var error = await Should.ThrowAsync<BusinessRuleException>(() => provider.EnsureWithinLimitAsync(Academy, LimitKeys.Students, currentCount: 20, ct: TestContext.Current.CancellationToken));
        error.Message.ShouldContain("20/20");
    }

    private static SubscriptionPlan Plan(string code, decimal price, int trial, int students, int teachers, int users, params string[] features) => new()
    {
        Code = code, Name = code, MonthlyPrice = price, TrialDays = trial,
        Limits = [new() { LimitKey = LimitKeys.Students, Value = students }, new() { LimitKey = LimitKeys.Teachers, Value = teachers }, new() { LimitKey = LimitKeys.Users, Value = users }],
        Features = FeatureKeys.All.Select(f => new PlanFeature { FeatureKey = f, Enabled = features.Contains(f) }).ToList(),
    };

    private sealed class FixedEntitlements(Entitlements value) : IEntitlementsProvider
    {
        public Task<Entitlements> GetAsync(long academyId, CancellationToken ct = default) => Task.FromResult(value);
    }
}
