using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Academies.BuildingBlocks.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    /// <summary>MySQL version the Pomelo provider targets (<c>Database:ServerVersion</c>).</summary>
    public const string DefaultServerVersion = "8.0.36";

    /// <summary>
    /// Every service shares the one MySQL database (<c>ConnectionStrings:Database</c>) but keeps
    /// its own migrations history table, so migrations from different services never collide.
    /// </summary>
    public static string MigrationsHistoryTable(string serviceName) => $"__ef_history_{serviceName.ToLowerInvariant()}";

    /// <summary>
    /// Pomelo setup shared by the runtime and design-time factories. The server version is
    /// explicit (not AutoDetect) so migrations can be generated without a live database.
    /// </summary>
    public static DbContextOptionsBuilder UseServiceMySql(
        this DbContextOptionsBuilder options, string connectionString, string serviceName, string? serverVersion = null) =>
        options.UseMySql(
            connectionString,
            new MySqlServerVersion(Version.Parse(serverVersion ?? DefaultServerVersion)),
            // No EnableRetryOnFailure: a retrying strategy replays SaveChanges inside the same DbContext,
            // which breaks the MassTransit outbox. Consumers retry with a fresh scope instead.
            mysql => mysql.MigrationsHistoryTable(MigrationsHistoryTable(serviceName)));

    public static IServiceCollection AddServiceDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
        where TContext : ServiceDbContext
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings:Database is not configured.");
        var serverVersion = configuration["Database:ServerVersion"];

        services.AddScoped<AuditableTenantInterceptor>();
        services.AddDbContext<TContext>((sp, options) => options
            .UseServiceMySql(connectionString, serviceName, serverVersion)
            .AddInterceptors(sp.GetRequiredService<AuditableTenantInterceptor>()));

        services.AddHealthChecks().AddDbContextCheck<TContext>("database", tags: ["ready"]);
        return services;
    }

    /// <summary>
    /// Applies pending migrations when <c>Database:MigrateOnStartup</c> is true (the default in
    /// Development) or the process was started with <c>--migrate</c>.
    /// </summary>
    public static async Task MigrateDatabaseAsync<TContext>(this IHost host, string[] args)
        where TContext : DbContext
    {
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        var shouldMigrate = args.Contains("--migrate") || configuration.GetValue("Database:MigrateOnStartup", false);
        if (!shouldMigrate)
        {
            return;
        }

        await using var scope = host.Services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        logger.LogInformation("Applying migrations for {Context}", typeof(TContext).Name);
        await db.Database.MigrateAsync();
    }
}
