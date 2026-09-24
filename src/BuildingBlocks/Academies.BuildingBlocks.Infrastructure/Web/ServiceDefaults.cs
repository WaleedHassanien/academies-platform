using System.Text.Json;
using System.Text.Json.Serialization;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Models;
using Academies.BuildingBlocks.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;
using Serilog;

namespace Academies.BuildingBlocks.Infrastructure.Web;

public static class ServiceDefaults
{
    /// <summary>
    /// The shared host setup every service starts with: Serilog, controllers with an ApiResponse
    /// envelope for model-state errors, FluentValidation, the global exception handler, OpenAPI,
    /// health checks and <see cref="ICurrentUser"/>.
    /// </summary>
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Host.UseSerilog((context, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .WriteTo.Console());

        var services = builder.Services;
        services.AddSingleton(new ServiceIdentity(serviceName));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton(TimeProvider.System);

        services
            .AddControllers(o => o.Filters.Add<ValidationFilter>())
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = context =>
                new BadRequestObjectResult(ApiResponse.Fail(
                    "One or more validation errors occurred.",
                    context.ModelState
                        .Where(e => e.Value?.Errors.Count > 0)
                        .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray()))));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddOpenApi();
        services.AddHealthChecks();

        return builder;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealthAsync }).AllowAnonymous();
        app.MapControllers();
        return app;
    }

    private static readonly JsonSerializerOptions HealthJson = new(JsonSerializerDefaults.Web);

    private static Task WriteHealthAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            durationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), e.Value.Description, error = e.Value.Exception?.Message }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, HealthJson));
    }
}
