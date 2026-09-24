using Academies.BuildingBlocks.Application.Models;
using Academies.Identity.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Academies.Identity.Api.Controllers;

/// <summary>Login and token lifecycle (US-010) plus password reset (US-012).</summary>
[ApiController]
[Route("auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(LoginRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AuthResponse>.Ok(await auth.LoginAsync(request, ct)));

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh(RefreshRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AuthResponse>.Ok(await auth.RefreshAsync(request, ct)));

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse>> Logout(LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request, ct);
        return Ok(ApiResponse.Ok("Logged out."));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse>> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request, ct);
        return Ok(ApiResponse.Ok("If the email is registered, a reset code has been sent."));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse>> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        return Ok(ApiResponse.Ok("Password has been reset."));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserInfo>>> Me(CancellationToken ct) =>
        Ok(ApiResponse<UserInfo>.Ok(await auth.GetCurrentAsync(ct)));
}
