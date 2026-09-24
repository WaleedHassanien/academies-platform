using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.BuildingBlocks.Infrastructure.Internal;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using Academies.Subscription.Application;
using Microsoft.AspNetCore.Mvc;

namespace Academies.Subscription.Api.Controllers;

/// <summary>Plans, limits, features and prices (US-014, US-016).</summary>
[ApiController]
[Route("plans")]
public sealed class PlansController(IPlanService plans, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Plans.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PlanDto>>>> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PlanDto>>.Ok(await plans.ListAsync(includeInactive: currentUser.IsSuperAdmin, ct)));

    [HttpGet("{id:long}")]
    [HasPermission(Permissions.Plans.View)]
    public async Task<ActionResult<ApiResponse<PlanDto>>> Get(long id, CancellationToken ct) =>
        Ok(ApiResponse<PlanDto>.Ok(await plans.GetAsync(id, ct)));

    [HttpPost]
    [HasPermission(Permissions.Plans.Manage)]
    public async Task<ActionResult<ApiResponse<PlanDto>>> Create(SavePlanRequest request, CancellationToken ct) =>
        Ok(ApiResponse<PlanDto>.Ok(await plans.CreateAsync(request, ct)));

    /// <summary>Changes apply to every academy on the plan.</summary>
    [HttpPut("{id:long}")]
    [HasPermission(Permissions.Plans.Manage)]
    public async Task<ActionResult<ApiResponse<PlanDto>>> Update(long id, SavePlanRequest request, CancellationToken ct) =>
        Ok(ApiResponse<PlanDto>.Ok(await plans.UpdateAsync(id, request, ct)));

    [HttpGet("{id:long}/price-history")]
    [HasPermission(Permissions.Plans.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PriceHistoryDto>>>> PriceHistory(long id, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PriceHistoryDto>>.Ok(await plans.PriceHistoryAsync(id, ct)));

    [HttpGet("catalog")]
    [HasPermission(Permissions.Plans.View)]
    public ActionResult<ApiResponse<object>> Catalog() =>
        Ok(ApiResponse<object>.Ok(new { limits = LimitKeys.All, features = FeatureKeys.All }));
}

/// <summary>An academy's plan, trial and per-academy overrides (US-015, US-018).</summary>
[ApiController]
[Route("academies")]
public sealed class AcademySubscriptionsController(IAcademySubscriptionService subscriptions, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("current/subscription")]
    [HasPermission(Permissions.Plans.View)]
    public async Task<ActionResult<ApiResponse<AcademySubscriptionDto>>> Current(CancellationToken ct) =>
        Ok(ApiResponse<AcademySubscriptionDto>.Ok(await subscriptions.GetAsync(
            currentUser.AcademyId ?? throw new NotFoundException("Academy", "current"), ct)));

    [HttpGet("{academyId:long}/subscription")]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<AcademySubscriptionDto>>> Get(long academyId, CancellationToken ct) =>
        Ok(ApiResponse<AcademySubscriptionDto>.Ok(await subscriptions.GetAsync(academyId, ct)));

    [HttpPut("{academyId:long}/subscription")]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<AcademySubscriptionDto>>> Assign(long academyId, AssignPlanRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AcademySubscriptionDto>.Ok(await subscriptions.AssignPlanAsync(academyId, request, ct)));

    [HttpPost("{academyId:long}/subscription/extend-trial")]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<AcademySubscriptionDto>>> ExtendTrial(long academyId, ExtendTrialRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AcademySubscriptionDto>.Ok(await subscriptions.ExtendTrialAsync(academyId, request.Days, ct)));

    /// <summary>Academy-specific limit, e.g. 150 students on a 100-student plan.</summary>
    [HttpPut("{academyId:long}/overrides/{limitKey}")]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<AcademySubscriptionDto>>> SetOverride(long academyId, string limitKey, SetOverrideRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AcademySubscriptionDto>.Ok(await subscriptions.SetOverrideAsync(academyId, limitKey, request, ct)));

    [HttpDelete("{academyId:long}/overrides/{limitKey}")]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<AcademySubscriptionDto>>> RemoveOverride(long academyId, string limitKey, CancellationToken ct) =>
        Ok(ApiResponse<AcademySubscriptionDto>.Ok(await subscriptions.RemoveOverrideAsync(academyId, limitKey, ct)));
}

/// <summary>Service-to-service: effective limits and features for enforcement (US-017).</summary>
[ApiController]
[InternalApi]
[Route("internal/academies")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalEntitlementsController(IEntitlementsService entitlements) : ControllerBase
{
    [HttpGet("{academyId:long}/entitlements")]
    public async Task<ActionResult<ApiResponse<Entitlements>>> Get(long academyId, CancellationToken ct)
    {
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.Platform);
        return Ok(ApiResponse<Entitlements>.Ok(await entitlements.GetAsync(academyId, ct)));
    }
}
