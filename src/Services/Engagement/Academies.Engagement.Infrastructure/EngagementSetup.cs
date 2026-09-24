using System.Globalization;
using System.Text.Json;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Domain;
using Academies.BuildingBlocks.Infrastructure.Caching;
using Academies.BuildingBlocks.Infrastructure.Internal;
using Academies.BuildingBlocks.Infrastructure.Messaging;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.Contracts.Events;
using Academies.Engagement.Application;
using Academies.Engagement.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.Engagement.Infrastructure.Persistence
{
    /// <summary>Maps only the Engagement service's tables in the shared <c>academies</c> database.</summary>
    public sealed class EngagementDbContext(DbContextOptions<EngagementDbContext> options, ICurrentUser currentUser)
        : ServiceDbContext(options, currentUser), IEngagementDbContext
    {
        protected override string ServiceName => EngagementServiceInfo.Name;
        protected override bool HasPeopleDirectory => true;

        public DbSet<Person> People => Set<Person>();

        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<Notification>(e =>
            {
                e.ToTable("Notifications");
                e.Property(x => x.Type).HasMaxLength(40);
                e.Property(x => x.TitleAr).HasMaxLength(200);
                e.Property(x => x.TitleEn).HasMaxLength(200);
                e.Property(x => x.BodyAr).HasMaxLength(1000);
                e.Property(x => x.BodyEn).HasMaxLength(1000);
                e.Property(x => x.Link).HasMaxLength(200);
                e.HasIndex(x => new { x.UserId, x.IsRead, x.Id });
            });

            b.Entity<AuditLog>(e =>
            {
                e.ToTable("AuditLogs");
                e.Property(x => x.Service).HasMaxLength(30);
                e.Property(x => x.Action).HasMaxLength(80);
                e.Property(x => x.EntityName).HasMaxLength(80);
                e.Property(x => x.EntityId).HasMaxLength(80);
                e.Property(x => x.DataJson).HasMaxLength(8000);
                e.HasIndex(x => new { x.AcademyId, x.OccurredOnUtc });
                e.HasIndex(x => x.UserId);
                // Replaces the base soft-delete filter (one per entity in EF Core 9), so it repeats !IsDeleted.
                e.HasQueryFilter(a => !a.IsDeleted && (BypassTenantFilter || a.AcademyId == CurrentAcademyId));
            });
        }
    }

    internal sealed class EngagementDesignTimeFactory() : DesignTimeDbContextFactoryBase<EngagementDbContext>(EngagementServiceInfo.Name)
    {
        protected override EngagementDbContext Create(DbContextOptions<EngagementDbContext> options, ICurrentUser currentUser) => new(options, currentUser);
    }
}

namespace Academies.Engagement.Infrastructure
{
    using Academies.Engagement.Infrastructure.Persistence;

    public static class DependencyInjection
    {
        public static IServiceCollection AddEngagementInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddServiceDbContext<EngagementDbContext>(configuration, EngagementServiceInfo.Name);
            services.AddScoped<IEngagementDbContext>(sp => sp.GetRequiredService<EngagementDbContext>());
            services.AddPlatformCaching(configuration, EngagementServiceInfo.Name);
            services.AddPlatformMessaging<EngagementDbContext>(configuration, EngagementServiceInfo.Name, bus =>
            {
                bus.AddPeopleDirectory<EngagementDbContext>(EngagementServiceInfo.Name);
                bus.AddConsumer<NotificationConsumer>();
                bus.AddConsumer<AuditConsumer>();
            });
            services.AddPlatformEmail(configuration);
            services.AddEntitlements(configuration);
            services.AddInternalServiceClient<IAcademicStatsHttp, AcademicStatsHttp>(configuration, "Academic");
            services.AddInternalServiceClient<IFinanceStatsHttp, FinanceStatsHttp>(configuration, "Finance");
            services.AddScoped<IStatsClient, StatsClient>();
            return services;
        }
    }

    public interface IAcademicStatsHttp
    {
        Task<JsonElement> GetAsync(long academyId, DateTime fromUtc, DateTime toUtc, long? supervisorUserId, CancellationToken ct);
    }

    public interface IFinanceStatsHttp
    {
        Task<JsonElement> GetAsync(long academyId, DateOnly from, DateOnly to, CancellationToken ct);
    }

    internal sealed class AcademicStatsHttp(HttpClient http) : IAcademicStatsHttp
    {
        public Task<JsonElement> GetAsync(long academyId, DateTime fromUtc, DateTime toUtc, long? supervisorUserId, CancellationToken ct) =>
            http.GetDataAsync<JsonElement>(
                $"internal/stats?academyId={academyId}&fromUtc={fromUtc.ToString("o", CultureInfo.InvariantCulture)}&toUtc={toUtc.ToString("o", CultureInfo.InvariantCulture)}"
                + (supervisorUserId is { } s ? $"&supervisorUserId={s}" : string.Empty),
                ct);
    }

    internal sealed class FinanceStatsHttp(HttpClient http) : IFinanceStatsHttp
    {
        public Task<JsonElement> GetAsync(long academyId, DateOnly from, DateOnly to, CancellationToken ct) =>
            http.GetDataAsync<JsonElement>($"internal/stats?academyId={academyId}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", ct);
    }

    internal sealed class StatsClient(IAcademicStatsHttp academic, IFinanceStatsHttp finance) : IStatsClient
    {
        public Task<JsonElement> AcademicAsync(long academyId, DateTime fromUtc, DateTime toUtc, long? supervisorUserId, CancellationToken ct = default) =>
            academic.GetAsync(academyId, fromUtc, toUtc, supervisorUserId, ct);

        public Task<JsonElement> FinanceAsync(long academyId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
            finance.GetAsync(academyId, from, to, ct);
    }

    /// <summary>Notifications for sessions, attendance, payments and salaries (US-035).</summary>
    internal sealed class NotificationConsumer(IEventNotifier notifier) :
        IConsumer<SessionReminderDue>, IConsumer<AttendanceRecorded>, IConsumer<PaymentDue>, IConsumer<PaymentRecorded>, IConsumer<SalaryPaid>
    {
        public Task Consume(ConsumeContext<SessionReminderDue> c) => Run(c.Message.AcademyId, ct => notifier.OnSessionReminderAsync(c.Message, ct), c.CancellationToken);

        public Task Consume(ConsumeContext<AttendanceRecorded> c) => Run(c.Message.AcademyId, ct => notifier.OnAttendanceAsync(c.Message, ct), c.CancellationToken);

        public Task Consume(ConsumeContext<PaymentDue> c) => Run(c.Message.AcademyId, ct => notifier.OnPaymentDueAsync(c.Message, ct), c.CancellationToken);

        public Task Consume(ConsumeContext<PaymentRecorded> c) => Run(c.Message.AcademyId, ct => notifier.OnPaymentRecordedAsync(c.Message, ct), c.CancellationToken);

        public Task Consume(ConsumeContext<SalaryPaid> c) => Run(c.Message.AcademyId, ct => notifier.OnSalaryPaidAsync(c.Message, ct), c.CancellationToken);

        private static async Task Run(long academyId, Func<CancellationToken, Task> work, CancellationToken ct)
        {
            using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(academyId));
            await work(ct);
        }
    }

    /// <summary>Stores every service's audit events (US-043).</summary>
    internal sealed class AuditConsumer(IAuditLogService audit) : IConsumer<AuditEvent>
    {
        public async Task Consume(ConsumeContext<AuditEvent> context)
        {
            using var _ = CurrentUserOverride.Begin(SystemCurrentUser.Platform);
            await audit.RecordAsync(context.Message, context.CancellationToken);
        }
    }
}
