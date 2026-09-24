using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Academies.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build a context without running the API host.
/// Pomelo needs no live server here because the server version is explicit.
/// </summary>
public abstract class DesignTimeDbContextFactoryBase<TContext>(string serviceName) : IDesignTimeDbContextFactory<TContext>
    where TContext : ServiceDbContext
{
    private const string FallbackConnection = "Server=localhost;Port=3306;Database=academies;Uid=academies;Pwd=academies;AllowPublicKeyRetrieval=true;";

    public TContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database") ?? FallbackConnection;
        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseServiceMySql(connectionString, serviceName);
        return Create(builder.Options, SystemCurrentUser.Platform);
    }

    protected abstract TContext Create(DbContextOptions<TContext> options, ICurrentUser currentUser);
}
