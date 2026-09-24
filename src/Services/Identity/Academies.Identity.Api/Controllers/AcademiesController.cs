using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Security;
using Academies.Identity.Application.Academies;
using Microsoft.AspNetCore.Mvc;

namespace Academies.Identity.Api.Controllers;

/// <summary>Academy management for the platform owner (US-018).</summary>
[ApiController]
[Route("academies")]
public sealed class AcademiesController(IAcademyService academies, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<PagedResult<AcademyDto>>>> List([FromQuery] AcademyQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<AcademyDto>>.Ok(await academies.ListAsync(query, ct)));

    /// <summary>The caller's own academy (any academy user).</summary>
    [HttpGet("current")]
    [HasPermission(Permissions.Academies.View)]
    public async Task<ActionResult<ApiResponse<AcademyDto>>> Current(CancellationToken ct) =>
        Ok(ApiResponse<AcademyDto>.Ok(await academies.GetAsync(currentUser.AcademyId ?? throw new NotFoundException("Academy", "current"), ct)));

    [HttpGet("{id:long}")]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<AcademyDto>>> Get(long id, CancellationToken ct) =>
        Ok(ApiResponse<AcademyDto>.Ok(await academies.GetAsync(id, ct)));

    [HttpPost]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<AcademyDto>>> Create(CreateAcademyRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AcademyDto>.Ok(await academies.CreateAsync(request, ct)));

    [HttpPut("{id:long}")]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<AcademyDto>>> Update(long id, UpdateAcademyRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AcademyDto>.Ok(await academies.UpdateAsync(id, request, ct)));

    [HttpPut("{id:long}/status")]
    [HasPermission(Permissions.Academies.Manage)]
    public async Task<ActionResult<ApiResponse<AcademyDto>>> SetStatus(long id, SetAcademyStatusRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AcademyDto>.Ok(await academies.SetStatusAsync(id, request.Status, ct)));
}
