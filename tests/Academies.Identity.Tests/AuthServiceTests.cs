using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Infrastructure.Caching;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Tests;
using Academies.Contracts.Security;
using Academies.Identity.Application;
using Academies.Identity.Application.Abstractions;
using Academies.Identity.Application.Auth;
using Academies.Identity.Domain.Academies;
using Academies.Identity.Domain.Roles;
using Academies.Identity.Domain.Users;
using Academies.Identity.Infrastructure.Persistence;
using Academies.Identity.Infrastructure.Security;
using FluentValidation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using BbCurrentUser = Academies.BuildingBlocks.Application.Abstractions.ICurrentUser;
using IEmailSender = Academies.BuildingBlocks.Application.Abstractions.IEmailSender;

namespace Academies.Identity.Tests;

/// <summary>
/// Login, refresh rotation, logout and password reset against a real IdentityDbContext
/// (SQLite in-memory) and the in-process cache in place of Redis (US-010, US-012).
/// </summary>
public sealed class AuthServiceTests : IAsyncLifetime
{
    private const string Password = "Correct!Horse1";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeCurrentUser _caller = new();
    private readonly CapturingEmailSender _email = new();
    private readonly string _keyDir = Path.Combine(Path.GetTempPath(), "academies-tests", Guid.NewGuid().ToString("N"));
    private ServiceProvider _services = null!;
    private long _academyId;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKeyPath"] = Path.Combine(_keyDir, "signing.pem"),
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<BbCurrentUser>(_caller);
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddScoped<AuditableTenantInterceptor>();
        services.AddDbContext<IdentityDbContext>((sp, o) => o
            .UseSqlite(_connection)
            .AddInterceptors(sp.GetRequiredService<AuditableTenantInterceptor>()));
        services.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<IdentityDbContext>());
        services.AddPlatformCaching(configuration, "identity");
        services.AddSingleton(SigningKeyStore.Load(configuration, _keyDir, allowGenerate: true));
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IRefreshTokenStore, RedisRefreshTokenStore>();
        services.AddSingleton<IPasswordResetStore, RedisPasswordResetStore>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<IEmailSender>(_email);
        services.AddIdentityApplication();
        _services = services.BuildServiceProvider();

        await SeedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
        if (Directory.Exists(_keyDir))
        {
            Directory.Delete(_keyDir, recursive: true);
        }
    }

    private async Task SeedAsync()
    {
        _caller.As(0, null, Roles.SuperAdmin);
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Database.EnsureCreatedAsync();

        var permission = new Permission { Code = Permissions.Users.Manage };
        var role = new Role { Name = Roles.Admin, NormalizedName = "ADMIN", RolePermissions = [new RolePermission { Permission = permission }] };
        var academy = new Academy { Name = "Noor" };
        db.Academies.Add(academy);
        await db.SaveChangesAsync();
        _academyId = academy.Id;

        var user = new User { Email = "admin@noor.test", NormalizedEmail = User.Normalize("admin@noor.test"), FullName = "Admin", AcademyId = academy.Id };
        user.PasswordHash = hasher.Hash(user, Password);
        user.UserRoles.Add(new UserRole { Role = role });
        db.Users.Add(user);
        await db.SaveChangesAsync();

        _caller.As(0, null); // back to anonymous
        _caller.UserId = null;
    }

    private async Task<T> WithAuth<T>(Func<IAuthService, Task<T>> action)
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAuthService>());
    }

    private Task WithAuth(Func<IAuthService, Task> action) => WithAuth(async a => { await action(a); return 0; });

    [Fact]
    public async Task Login_issues_a_signed_token_with_academy_and_permissions()
    {
        var result = await WithAuth(a => a.LoginAsync(new LoginRequest(" Admin@Noor.test ", Password)));

        result.User.AcademyId.ShouldBe(_academyId);
        result.User.Roles.ShouldBe([Roles.Admin]);
        result.User.Permissions.ShouldBe([Permissions.Users.Manage]);

        var keys = _services.GetRequiredService<SigningKeyStore>();
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(result.AccessToken, new TokenValidationParameters
        {
            ValidIssuer = "academies-identity",
            ValidAudience = "academies-api",
            IssuerSigningKey = keys.SigningKey,
        });
        validation.IsValid.ShouldBeTrue();
        validation.Claims[AppClaims.AcademyId].ShouldBe(_academyId.ToString());
    }

    [Fact]
    public async Task Wrong_password_is_rejected() =>
        await Should.ThrowAsync<UnauthorizedException>(() => WithAuth(a => a.LoginAsync(new LoginRequest("admin@noor.test", "nope"))));

    [Fact]
    public async Task Suspended_academy_cannot_log_in()
    {
        _caller.As(0, null, Roles.SuperAdmin);
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.Academies.SingleAsync(TestContext.Current.CancellationToken)).Status = AcademyStatus.Suspended;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        _caller.RoleList.Clear();
        await Should.ThrowAsync<ForbiddenAccessException>(() => WithAuth(a => a.LoginAsync(new LoginRequest("admin@noor.test", Password))));
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_logout_revokes_it()
    {
        var login = await WithAuth(a => a.LoginAsync(new LoginRequest("admin@noor.test", Password)));

        var refreshed = await WithAuth(a => a.RefreshAsync(new RefreshRequest(login.RefreshToken)));
        refreshed.RefreshToken.ShouldNotBe(login.RefreshToken);

        // The old token was consumed by the rotation.
        await Should.ThrowAsync<UnauthorizedException>(() => WithAuth(a => a.RefreshAsync(new RefreshRequest(login.RefreshToken))));

        await WithAuth(a => a.LogoutAsync(new LogoutRequest(refreshed.RefreshToken)));
        await Should.ThrowAsync<UnauthorizedException>(() => WithAuth(a => a.RefreshAsync(new RefreshRequest(refreshed.RefreshToken))));
    }

    [Fact]
    public async Task Password_reset_with_emailed_code_changes_the_password()
    {
        await WithAuth(a => a.ForgotPasswordAsync(new ForgotPasswordRequest("admin@noor.test")));
        var code = _email.LastBody!.Split(": ")[1][..6];

        await WithAuth(a => a.ResetPasswordAsync(new ResetPasswordRequest("admin@noor.test", code, "NewPassw0rd!")));

        await Should.ThrowAsync<UnauthorizedException>(() => WithAuth(a => a.LoginAsync(new LoginRequest("admin@noor.test", Password))));
        var login = await WithAuth(a => a.LoginAsync(new LoginRequest("admin@noor.test", "NewPassw0rd!")));
        login.User.Email.ShouldBe("admin@noor.test");

        // Codes are single-use.
        await Should.ThrowAsync<ValidationException>(() =>
            WithAuth(a => a.ResetPasswordAsync(new ResetPasswordRequest("admin@noor.test", code, "Another1!"))));
    }

    [Fact]
    public async Task Forgot_password_for_unknown_email_is_silent()
    {
        await WithAuth(a => a.ForgotPasswordAsync(new ForgotPasswordRequest("nobody@noor.test")));
        _email.LastBody.ShouldBeNull();
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public string? LastBody { get; private set; }

        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        {
            LastBody = body;
            return Task.CompletedTask;
        }
    }
}
