using Academies.Academic.Application;
using Academies.Academic.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Caching;
using Academies.BuildingBlocks.Infrastructure.Internal;
using Academies.BuildingBlocks.Infrastructure.Messaging;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Academies.Academic.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAcademicInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceDbContext<AcademicDbContext>(configuration, AcademicServiceInfo.Name);
        services.AddScoped<IAcademicDbContext>(sp => sp.GetRequiredService<AcademicDbContext>());
        services.AddPlatformCaching(configuration, AcademicServiceInfo.Name);
        services.AddPlatformMessaging<AcademicDbContext>(configuration, AcademicServiceInfo.Name, bus =>
        {
            bus.AddPeopleDirectory<AcademicDbContext>(AcademicServiceInfo.Name);
            bus.AddConsumer<ProfileSyncConsumer>();
        });
        services.AddPlatformAudit();
        services.AddEntitlements(configuration);

        services.AddSingleton<IMeetingLinkGenerator>(new JitsiMeetingLinkGenerator(configuration["Meetings:BaseUrl"] ?? "https://meet.jit.si"));
        services.AddSingleton<ICertificateRenderer, QuestPdfCertificateRenderer>();

        if (configuration.GetValue("Jobs:Enabled", true))
        {
            services.AddHostedService<SessionReminderJob>();
        }

        return services;
    }
}

/// <summary>
/// Online-session links (US-037) on Jitsi Meet, which needs no account or API key. Room names
/// are unguessable. To use Zoom or Google Meet, implement <see cref="IMeetingLinkGenerator"/>
/// with their OAuth APIs and register it instead.
/// </summary>
internal sealed class JitsiMeetingLinkGenerator(string baseUrl) : IMeetingLinkGenerator
{
    public string Generate(long academyId, string title, DateTime startsAtUtc) =>
        $"{baseUrl.TrimEnd('/')}/academy{academyId}-{startsAtUtc:yyyyMMddHHmm}-{Guid.NewGuid():N}";
}

/// <summary>
/// Certificate PDF (US-041). Arabic names need a font with Arabic glyphs: the Docker image
/// installs Noto (see the Academic Dockerfile).
/// </summary>
internal sealed class QuestPdfCertificateRenderer : ICertificateRenderer
{
    // QuestPDF refuses the whole list if any family is missing, so pick per OS: Windows ships
    // Arial/Segoe UI (both with Arabic glyphs); the Linux image installs Noto (fonts-noto-core).
    private static readonly string[] Fonts = OperatingSystem.IsWindows() ? ["Arial", "Segoe UI"] : ["Noto Sans", "Noto Naskh Arabic"];

    static QuestPdfCertificateRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        QuestPDF.Settings.UseSystemFonts = true;
    }

    public byte[] Render(CertificateDocument c) =>
        Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(30);
            page.DefaultTextStyle(t => t.FontSize(16).FontFamily(Fonts));
            page.Content().Border(4).BorderColor(Colors.Blue.Darken3).Padding(36).Column(col =>
            {
                col.Spacing(14);
                col.Item().AlignCenter().Text(c.AcademyName).FontSize(18).SemiBold().FontColor(Colors.Grey.Darken2);
                col.Item().AlignCenter().Text("Certificate of Completion").FontSize(36).Bold().FontColor(Colors.Blue.Darken3);
                col.Item().AlignCenter().Text("شهادة إتمام").FontSize(26).FontColor(Colors.Blue.Darken3);
                col.Item().PaddingTop(10).AlignCenter().Text("This is to certify that · نشهد بأن");
                col.Item().AlignCenter().Text(c.StudentName).FontSize(32).Bold();
                col.Item().AlignCenter().Text("has successfully completed · أتمّ بنجاح");
                col.Item().AlignCenter().Text(c.CourseName).FontSize(26).SemiBold();
                col.Item().PaddingTop(30).Row(row =>
                {
                    row.RelativeItem().Text($"Issued · تاريخ الإصدار: {c.IssuedOnUtc:yyyy-MM-dd}");
                    row.RelativeItem().AlignRight().Text($"No. {c.Number}");
                });
            });
        })).GeneratePdf();
}

/// <summary>Creates Student/Teacher/Supervisor/Parent profiles for new or re-roled users (US-020).</summary>
internal sealed class ProfileSyncConsumer(IProfileSync sync) : IConsumer<UserCreated>, IConsumer<UserUpdated>
{
    public Task Consume(ConsumeContext<UserCreated> context) =>
        SyncAsync(context.Message.AcademyId, context.Message.UserId, context.Message.Roles, context.CancellationToken);

    public Task Consume(ConsumeContext<UserUpdated> context) =>
        SyncAsync(context.Message.AcademyId, context.Message.UserId, context.Message.Roles, context.CancellationToken);

    private async Task SyncAsync(long? academyId, long userId, IReadOnlyList<string> roles, CancellationToken ct)
    {
        if (academyId is not { } academy)
        {
            return;
        }

        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(academy));
        await sync.SyncAsync(academy, userId, roles, ct);
    }
}

/// <summary>Every 10 minutes: announce sessions starting within 24 hours (US-035).</summary>
internal sealed class SessionReminderJob(IServiceScopeFactory scopes, ILogger<SessionReminderJob> logger) : RecurringJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(10);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.Platform);
        await services.GetRequiredService<ISessionReminderService>().SendDueRemindersAsync(ct);
    }
}
