using Academies.BuildingBlocks.Domain;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Academies.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Keeps a service's <c>{service}_People</c> table in step with Identity. Only academy users
/// are copied: platform users (SuperAdmin) have no academy and never appear in academy data.
/// </summary>
public sealed class PeopleDirectoryConsumer<TContext>(TContext db) : IConsumer<UserCreated>, IConsumer<UserUpdated>
    where TContext : ServiceDbContext
{
    public Task Consume(ConsumeContext<UserCreated> context)
    {
        var m = context.Message;
        return UpsertAsync(m.UserId, m.AcademyId, m.Email, m.FullName, m.Roles, isActive: true, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<UserUpdated> context)
    {
        var m = context.Message;
        return UpsertAsync(m.UserId, m.AcademyId, m.Email, m.FullName, m.Roles, m.IsActive, context.CancellationToken);
    }

    private async Task UpsertAsync(
        long userId, long? academyId, string email, string fullName, IReadOnlyList<string> roles, bool isActive, CancellationToken ct)
    {
        if (academyId is not { } academy)
        {
            return;
        }

        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.Platform);

        var person = await db.Set<Person>().IgnoreQueryFilters().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (person is null)
        {
            person = new Person { UserId = userId, AcademyId = academy };
            db.Set<Person>().Add(person);
        }

        person.Email = email;
        person.FullName = fullName;
        person.Roles = string.Join(',', roles);
        person.IsActive = isActive;
        person.IsDeleted = false;

        await db.SaveChangesAsync(ct);
    }
}

public static class PeopleDirectoryExtensions
{
    /// <summary>Registers the consumer on its own queue, <c>{service}-people-directory</c>.</summary>
    public static void AddPeopleDirectory<TContext>(this IBusRegistrationConfigurator bus, string serviceName)
        where TContext : ServiceDbContext =>
        bus.AddConsumer<PeopleDirectoryConsumer<TContext>>()
            .Endpoint(e => e.Name = $"{serviceName.ToLowerInvariant()}-people-directory");

    /// <summary>Id → display name for the given users (missing ids are simply absent).</summary>
    public static async Task<Dictionary<long, string>> NamesAsync(this DbSet<Person> people, IEnumerable<long> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Distinct().ToList();
        return ids.Count == 0
            ? []
            : await people.Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.FullName, ct);
    }
}
