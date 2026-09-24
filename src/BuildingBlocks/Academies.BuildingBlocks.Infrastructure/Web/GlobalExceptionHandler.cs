using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Academies.BuildingBlocks.Infrastructure.Web;

/// <summary>Maps exceptions to <see cref="ApiResponse"/> bodies with the matching status code.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, body) = exception switch
        {
            ValidationException v => (StatusCodes.Status400BadRequest, ApiResponse.Fail(
                "One or more validation errors occurred.",
                v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))),
            NotFoundException => (StatusCodes.Status404NotFound, ApiResponse.Fail(exception.Message)),
            ConflictException => (StatusCodes.Status409Conflict, ApiResponse.Fail(exception.Message)),
            BusinessRuleException => (StatusCodes.Status422UnprocessableEntity, ApiResponse.Fail(exception.Message)),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, ApiResponse.Fail(exception.Message)),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, ApiResponse.Fail(exception.Message)),
            _ => (StatusCodes.Status500InternalServerError, ApiResponse.Fail("An unexpected error occurred.")),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(body, cancellationToken);
        return true;
    }
}
