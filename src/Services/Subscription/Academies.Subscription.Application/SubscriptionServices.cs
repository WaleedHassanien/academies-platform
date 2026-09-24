using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Events;
using Academies.Contracts.Subscriptions;
using Academies.Subscription.Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Subscription.Application;

public interface ISubscriptionDbContext
{
    DbSet<SubscriptionPlan> Plans { get; }
    DbSet<PlanPriceHistory> PriceHistory { get; }
    DbSet<AcademySubscription> AcademySubscriptions { get; }
    DbSet<AcademyLimitOverride> LimitOverrides { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

// ---------- DTOs ----------

public sealed record PlanDto(
    long Id, string Code, string Name, decimal MonthlyPrice, int TrialDays, bool IsActive, int SortOrder,
    IReadOnlyDictionary<string, int> Limits, IReadOnlyDictionary<string, bool> Features);

public sealed record SavePlanRequest(
    string Code, string Name, decimal MonthlyPrice, int TrialDays, bool IsActive, int SortOrder,
    IReadOnlyDictionary<string, int> Limits, IReadOnlyDictionary<string, bool> Features);

public sealed record PriceHistoryDto(decimal OldPrice, decimal NewPrice, DateTime ChangedOnUtc, long? ChangedBy);

public sealed record AcademySubscriptionDto(
    long AcademyId, long PlanId, string PlanCode, string PlanName, string Status, DateTime StartsOnUtc,
    DateTime? TrialEndsOnUtc, DateTime? EndsOnUtc, IReadOnlyList<LimitOverrideDto> Overrides, Entitlements Effective);

public sealed record LimitOverrideDto(string LimitKey, int Value, string? Reason);

public sealed record AssignPlanRequest(long PlanId, DateTime? EndsOnUtc, bool StartAsTrial = false);

public sealed record ExtendTrialRequest(int Days);

public sealed record SetOverrideRequest(int Value, string? Reason);

// ---------- Entitlements (US-015) ----------

public interface IEntitlementsService
{
    Task<Entitlements> GetAsync(long academyId, CancellationToken ct = default);
    Task InvalidateAsync(long academyId, CancellationToken ct = default);
}

/// <summary>
/// Effective limit = academy override ?? plan limit, cached in Redis under
/// <c>subscription:entitlements:{academyId}</c> and dropped on any change.
/// </summary>
internal sealed class EntitlementsService(ISubscriptionDbContext db, ICacheService cache, TimeProvider clock, TrialProvisioner trials)
    : IEntitlementsService
{
    public Task<Entitlements> GetAsync(long academyId, CancellationToken ct = default) =>
        cache.GetOrCreateAsync(Key(academyId), token => ComputeAsync(academyId, token), TimeSpan.FromMinutes(10), ct);

    public Task InvalidateAsync(long academyId, CancellationToken ct = default) => cache.RemoveAsync(Key(academyId), ct);

    private async Task<Entitlements> ComputeAsync(long academyId, CancellationToken ct)
    {
        // An academy without a subscription (e.g. AcademyCreated not processed yet) starts its trial now.
        var subscription = await Query(academyId).FirstOrDefaultAsync(ct) ?? await trials.EnsureTrialAsync(academyId, ct);
        var overrides = await db.LimitOverrides.IgnoreQueryFilters()
            .Where(o => o.AcademyId == academyId && !o.IsDeleted)
            .ToDictionaryAsync(o => o.LimitKey, o => o.Value, ct);

        return Build(subscription, overrides, clock.GetUtcNow().UtcDateTime);
    }

    internal static Entitlements Build(AcademySubscription subscription, IReadOnlyDictionary<string, int> overrides, DateTime nowUtc)
    {
        var plan = subscription.Plan;
        var limits = LimitKeys.All.ToDictionary(
            k => k,
            k => overrides.TryGetValue(k, out var o) ? o : plan.Limits.FirstOrDefault(l => l.LimitKey == k && !l.IsDeleted)?.Value ?? 0);
        var features = FeatureKeys.All.ToDictionary(
            k => k,
            k => plan.Features.Any(f => f.FeatureKey == k && f.Enabled && !f.IsDeleted));

        return new Entitlements(
            subscription.AcademyId, plan.Code, plan.Name, subscription.EffectiveStatus(nowUtc).ToString(),
            subscription.TrialEndsOnUtc, limits, features);
    }

    private IQueryable<AcademySubscription> Query(long academyId) =>
        db.AcademySubscriptions.IgnoreQueryFilters()
            .Where(s => s.AcademyId == academyId && !s.IsDeleted)
            .Include(s => s.Plan).ThenInclude(p => p.Limits)
            .Include(s => s.Plan).ThenInclude(p => p.Features);

    private static string Key(long academyId) => $"entitlements:{academyId}";
}

/// <summary>Puts new academies on the Free plan's trial.</summary>
internal sealed class TrialProvisioner(ISubscriptionDbContext db, TimeProvider clock, IEventPublisher events)
{
    public const string DefaultPlanCode = "FREE";

    public async Task<AcademySubscription> EnsureTrialAsync(long academyId, CancellationToken ct = default)
    {
        var existing = await db.AcademySubscriptions.IgnoreQueryFilters()
            .Include(s => s.Plan).ThenInclude(p => p.Limits)
            .Include(s => s.Plan).ThenInclude(p => p.Features)
            .FirstOrDefaultAsync(s => s.AcademyId == academyId && !s.IsDeleted, ct);
        if (existing is not null)
        {
            return existing;
        }

        var plan = await db.Plans.Include(p => p.Limits).Include(p => p.Features)
            .FirstOrDefaultAsync(p => p.Code == DefaultPlanCode, ct)
            ?? throw new InvalidOperationException($"Plan {DefaultPlanCode} is not seeded.");

        var now = clock.GetUtcNow().UtcDateTime;
        var subscription = new AcademySubscription
        {
            AcademyId = academyId,
            Plan = plan,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Trial,
            StartsOnUtc = now,
            TrialEndsOnUtc = now.AddDays(plan.TrialDays),
        };
        db.AcademySubscriptions.Add(subscription);
        await events.PublishAsync(new AcademySubscriptionChanged(academyId, plan.Id, plan.Code), ct);
        await db.SaveChangesAsync(ct);
        return subscription;
    }
}

// ---------- Plans (US-014, US-016) ----------

public interface IPlanService
{
    Task<IReadOnlyList<PlanDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<PlanDto> GetAsync(long id, CancellationToken ct = default);
    Task<PlanDto> CreateAsync(SavePlanRequest request, CancellationToken ct = default);
    Task<PlanDto> UpdateAsync(long id, SavePlanRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PriceHistoryDto>> PriceHistoryAsync(long id, CancellationToken ct = default);
}

internal sealed class PlanService(ISubscriptionDbContext db, IEntitlementsService entitlements, IAuditTrail audit) : IPlanService
{
    public async Task<IReadOnlyList<PlanDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        var plans = await Plans().Where(p => includeInactive || p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(ct);
        return plans.Select(ToDto).ToList();
    }

    public async Task<PlanDto> GetAsync(long id, CancellationToken ct = default) => ToDto(await LoadAsync(id, ct));

    public async Task<PlanDto> CreateAsync(SavePlanRequest request, CancellationToken ct = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Plans.AnyAsync(p => p.Code == code, ct))
        {
            throw new ConflictException($"Plan code '{code}' already exists.");
        }

        var plan = new SubscriptionPlan { Code = code, Name = request.Name };
        Apply(plan, request);
        db.Plans.Add(plan);
        await audit.RecordAsync("plans.create", nameof(SubscriptionPlan), code, request, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    /// <summary>
    /// Changing a plan's price or limits affects every academy on it (US-016). The price change
    /// is recorded, and every subscriber's cached entitlements are dropped.
    /// </summary>
    public async Task<PlanDto> UpdateAsync(long id, SavePlanRequest request, CancellationToken ct = default)
    {
        var plan = await LoadAsync(id, ct);
        if (plan.MonthlyPrice != request.MonthlyPrice)
        {
            db.PriceHistory.Add(new PlanPriceHistory { PlanId = plan.Id, OldPrice = plan.MonthlyPrice, NewPrice = request.MonthlyPrice });
        }

        Apply(plan, request);
        await audit.RecordAsync("plans.update", nameof(SubscriptionPlan), id, request, ct);
        await db.SaveChangesAsync(ct);

        var subscribers = await db.AcademySubscriptions.IgnoreQueryFilters()
            .Where(s => s.PlanId == id && !s.IsDeleted).Select(s => s.AcademyId).ToListAsync(ct);
        foreach (var academyId in subscribers)
        {
            await entitlements.InvalidateAsync(academyId, ct);
        }

        return ToDto(plan);
    }

    public async Task<IReadOnlyList<PriceHistoryDto>> PriceHistoryAsync(long id, CancellationToken ct = default) =>
        await db.PriceHistory.Where(h => h.PlanId == id).OrderByDescending(h => h.CreatedOnUtc)
            .Select(h => new PriceHistoryDto(h.OldPrice, h.NewPrice, h.CreatedOnUtc, h.CreatedBy))
            .ToListAsync(ct);

    private static void Apply(SubscriptionPlan plan, SavePlanRequest request)
    {
        plan.Name = request.Name.Trim();
        plan.MonthlyPrice = request.MonthlyPrice;
        plan.TrialDays = request.TrialDays;
        plan.IsActive = request.IsActive;
        plan.SortOrder = request.SortOrder;

        foreach (var (key, value) in request.Limits)
        {
            var limit = plan.Limits.FirstOrDefault(l => l.LimitKey == key);
            if (limit is null)
            {
                plan.Limits.Add(new PlanLimit { LimitKey = key, Value = value });
            }
            else
            {
                limit.Value = value;
            }
        }

        foreach (var (key, enabled) in request.Features)
        {
            var feature = plan.Features.FirstOrDefault(f => f.FeatureKey == key);
            if (feature is null)
            {
                plan.Features.Add(new PlanFeature { FeatureKey = key, Enabled = enabled });
            }
            else
            {
                feature.Enabled = enabled;
            }
        }
    }

    private IQueryable<SubscriptionPlan> Plans() => db.Plans.Include(p => p.Limits).Include(p => p.Features);

    private async Task<SubscriptionPlan> LoadAsync(long id, CancellationToken ct) =>
        await Plans().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Plan", id);

    internal static PlanDto ToDto(SubscriptionPlan p) => new(
        p.Id, p.Code, p.Name, p.MonthlyPrice, p.TrialDays, p.IsActive, p.SortOrder,
        p.Limits.ToDictionary(l => l.LimitKey, l => l.Value),
        p.Features.ToDictionary(f => f.FeatureKey, f => f.Enabled));
}

// ---------- Academy subscriptions (US-015, US-018) ----------

public interface IAcademySubscriptionService
{
    Task<AcademySubscriptionDto> GetAsync(long academyId, CancellationToken ct = default);
    Task<AcademySubscriptionDto> AssignPlanAsync(long academyId, AssignPlanRequest request, CancellationToken ct = default);
    Task<AcademySubscriptionDto> ExtendTrialAsync(long academyId, int days, CancellationToken ct = default);
    Task<AcademySubscriptionDto> SetOverrideAsync(long academyId, string limitKey, SetOverrideRequest request, CancellationToken ct = default);
    Task<AcademySubscriptionDto> RemoveOverrideAsync(long academyId, string limitKey, CancellationToken ct = default);
    Task SetSuspendedAsync(long academyId, bool suspended, CancellationToken ct = default);
}

internal sealed class AcademySubscriptionService(
    ISubscriptionDbContext db,
    IEntitlementsService entitlements,
    TrialProvisioner trials,
    TimeProvider clock,
    IEventPublisher events,
    IAuditTrail audit) : IAcademySubscriptionService
{
    public async Task<AcademySubscriptionDto> GetAsync(long academyId, CancellationToken ct = default)
    {
        var subscription = await trials.EnsureTrialAsync(academyId, ct);
        var overrides = await db.LimitOverrides.IgnoreQueryFilters()
            .Where(o => o.AcademyId == academyId && !o.IsDeleted)
            .Select(o => new LimitOverrideDto(o.LimitKey, o.Value, o.Reason))
            .ToListAsync(ct);

        return new AcademySubscriptionDto(
            academyId, subscription.PlanId, subscription.Plan.Code, subscription.Plan.Name,
            subscription.EffectiveStatus(clock.GetUtcNow().UtcDateTime).ToString(), subscription.StartsOnUtc,
            subscription.TrialEndsOnUtc, subscription.EndsOnUtc, overrides, await entitlements.GetAsync(academyId, ct));
    }

    public async Task<AcademySubscriptionDto> AssignPlanAsync(long academyId, AssignPlanRequest request, CancellationToken ct = default)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == request.PlanId, ct) ?? throw new NotFoundException("Plan", request.PlanId);
        var subscription = await trials.EnsureTrialAsync(academyId, ct);
        var now = clock.GetUtcNow().UtcDateTime;

        subscription.PlanId = plan.Id;
        subscription.Plan = plan;
        subscription.StartsOnUtc = now;
        subscription.EndsOnUtc = request.EndsOnUtc;
        if (request.StartAsTrial)
        {
            subscription.Status = SubscriptionStatus.Trial;
            subscription.TrialEndsOnUtc = now.AddDays(plan.TrialDays);
        }
        else
        {
            subscription.Status = SubscriptionStatus.Active;
            subscription.TrialEndsOnUtc = null;
        }

        await events.PublishAsync(new AcademySubscriptionChanged(academyId, plan.Id, plan.Code), ct);
        await audit.RecordAsync("subscriptions.assign", nameof(AcademySubscription), academyId, new { plan.Code, request.EndsOnUtc }, ct);
        await db.SaveChangesAsync(ct);
        await entitlements.InvalidateAsync(academyId, ct);
        return await GetAsync(academyId, ct);
    }

    public async Task<AcademySubscriptionDto> ExtendTrialAsync(long academyId, int days, CancellationToken ct = default)
    {
        var subscription = await trials.EnsureTrialAsync(academyId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var from = subscription.TrialEndsOnUtc is { } end && end > now ? end : now;

        subscription.Status = SubscriptionStatus.Trial;
        subscription.TrialEndsOnUtc = from.AddDays(days);
        await audit.RecordAsync("subscriptions.extend_trial", nameof(AcademySubscription), academyId, new { days }, ct);
        await db.SaveChangesAsync(ct);
        await entitlements.InvalidateAsync(academyId, ct);
        return await GetAsync(academyId, ct);
    }

    public async Task<AcademySubscriptionDto> SetOverrideAsync(long academyId, string limitKey, SetOverrideRequest request, CancellationToken ct = default)
    {
        EnsureKnownLimit(limitKey);
        var existing = await db.LimitOverrides.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.AcademyId == academyId && o.LimitKey == limitKey && !o.IsDeleted, ct);
        if (existing is null)
        {
            db.LimitOverrides.Add(new AcademyLimitOverride { AcademyId = academyId, LimitKey = limitKey, Value = request.Value, Reason = request.Reason });
        }
        else
        {
            existing.Value = request.Value;
            existing.Reason = request.Reason;
        }

        await audit.RecordAsync("subscriptions.override", nameof(AcademyLimitOverride), academyId, new { limitKey, request.Value, request.Reason }, ct);
        await db.SaveChangesAsync(ct);
        await entitlements.InvalidateAsync(academyId, ct);
        return await GetAsync(academyId, ct);
    }

    public async Task<AcademySubscriptionDto> RemoveOverrideAsync(long academyId, string limitKey, CancellationToken ct = default)
    {
        var existing = await db.LimitOverrides.IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.AcademyId == academyId && o.LimitKey == limitKey && !o.IsDeleted, ct);
        if (existing is not null)
        {
            db.LimitOverrides.Remove(existing);
            await audit.RecordAsync("subscriptions.override_remove", nameof(AcademyLimitOverride), academyId, new { limitKey }, ct);
            await db.SaveChangesAsync(ct);
            await entitlements.InvalidateAsync(academyId, ct);
        }

        return await GetAsync(academyId, ct);
    }

    public async Task SetSuspendedAsync(long academyId, bool suspended, CancellationToken ct = default)
    {
        var subscription = await trials.EnsureTrialAsync(academyId, ct);
        if (suspended)
        {
            subscription.Status = SubscriptionStatus.Suspended;
        }
        else if (subscription.Status == SubscriptionStatus.Suspended)
        {
            subscription.Status = subscription.TrialEndsOnUtc is not null ? SubscriptionStatus.Trial : SubscriptionStatus.Active;
        }

        await db.SaveChangesAsync(ct);
        await entitlements.InvalidateAsync(academyId, ct);
    }

    private static void EnsureKnownLimit(string limitKey)
    {
        if (!LimitKeys.All.Contains(limitKey))
        {
            throw new BusinessRuleException($"Unknown limit '{limitKey}'. Known: {string.Join(", ", LimitKeys.All)}.");
        }
    }
}

internal sealed class SavePlanValidator : AbstractValidator<SavePlanRequest>
{
    public SavePlanValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9_-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MonthlyPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TrialDays).InclusiveBetween(0, 365);
        RuleForEach(x => x.Limits).Must(l => LimitKeys.All.Contains(l.Key) && l.Value >= Entitlements.Unlimited)
            .WithMessage("Limits must use known keys and values ≥ -1 (-1 = unlimited).");
        RuleForEach(x => x.Features).Must(f => FeatureKeys.All.Contains(f.Key)).WithMessage("Unknown feature key.");
    }
}

internal sealed class AssignPlanValidator : AbstractValidator<AssignPlanRequest>
{
    public AssignPlanValidator() => RuleFor(x => x.PlanId).GreaterThan(0);
}

internal sealed class ExtendTrialValidator : AbstractValidator<ExtendTrialRequest>
{
    public ExtendTrialValidator() => RuleFor(x => x.Days).InclusiveBetween(1, 365);
}

internal sealed class SetOverrideValidator : AbstractValidator<SetOverrideRequest>
{
    public SetOverrideValidator()
    {
        RuleFor(x => x.Value).GreaterThanOrEqualTo(Entitlements.Unlimited);
        RuleFor(x => x.Reason).MaximumLength(300);
    }
}
