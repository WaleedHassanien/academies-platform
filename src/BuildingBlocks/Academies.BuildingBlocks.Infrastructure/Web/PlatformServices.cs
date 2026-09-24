using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Academies.BuildingBlocks.Infrastructure.Web;

/// <summary>Which service this process is (e.g. "finance"). Registered by AddServiceDefaults.</summary>
public sealed record ServiceIdentity(string Name);

/// <summary>Publishes <see cref="AuditEvent"/> through the outbox; Engagement stores it.</summary>
internal sealed class AuditTrail(IPublishEndpoint publisher, ICurrentUser user, ServiceIdentity service, TimeProvider clock) : IAuditTrail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task RecordAsync(string action, string entityName, object? entityId, object? data = null, CancellationToken ct = default) =>
        publisher.Publish(
            new AuditEvent(
                user.AcademyId,
                user.UserId,
                service.Name,
                action,
                entityName,
                entityId?.ToString(),
                data is null ? null : JsonSerializer.Serialize(data, Json),
                clock.GetUtcNow().UtcDateTime),
            ct);
}

public sealed class EmailOptions
{
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@academies.local";
}

/// <summary>Used when no SMTP host is configured (development): writes the email to the log.</summary>
internal sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL to {To} | {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}

internal sealed class SmtpEmailSender(EmailOptions options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        using var client = new SmtpClient(options.SmtpHost, options.SmtpPort) { EnableSsl = options.UseSsl };
        if (!string.IsNullOrEmpty(options.UserName))
        {
            client.Credentials = new NetworkCredential(options.UserName, options.Password);
        }

        using var message = new MailMessage(options.From, to, subject, body);
        await client.SendMailAsync(message, ct);
    }
}

/// <summary>
/// Runs <see cref="RunAsync"/> every <see cref="Interval"/> in a fresh DI scope. Failures are
/// logged and retried on the next tick. With several replicas each one runs the job, so jobs
/// must be idempotent (e.g. stamp "reminder sent").
/// </summary>
public abstract class RecurringJob(IServiceScopeFactory scopes, ILogger logger) : BackgroundService
{
    protected abstract TimeSpan Interval { get; }

    protected abstract Task RunAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await RunAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Recurring job {Job} failed", GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public static class PlatformServiceExtensions
{
    public static IServiceCollection AddPlatformEmail(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Email").Get<EmailOptions>() ?? new EmailOptions();
        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else
        {
            services.AddSingleton(options);
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }

    /// <summary>Requires AddPlatformMessaging (the audit event goes through its outbox).</summary>
    public static IServiceCollection AddPlatformAudit(this IServiceCollection services) =>
        services.AddScoped<IAuditTrail, AuditTrail>();
}
