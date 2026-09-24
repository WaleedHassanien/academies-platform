using Academies.Identity.Domain.Academies;
using Academies.Identity.Domain.Roles;
using Academies.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Academies.Identity.Application.Abstractions;

public interface IIdentityDbContext
{
    DbSet<Academy> Academies { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
