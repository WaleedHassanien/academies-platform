using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.Identity.Application;
using Academies.Identity.Infrastructure;
using Academies.Identity.Infrastructure.Persistence;
using Academies.Identity.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

var signingKeys = SigningKeyStore.Load(
    builder.Configuration, builder.Environment.ContentRootPath, allowGenerate: builder.Environment.IsDevelopment());

builder.AddServiceDefaults(IdentityServiceInfo.Name);
builder.Services.AddPlatformAuthentication(builder.Configuration, signingKeys.SigningKey);
builder.Services.AddIdentityApplication();
builder.Services.AddIdentityInfrastructure(builder.Configuration, signingKeys);

var app = builder.Build();

app.UseServiceDefaults();
await app.MigrateDatabaseAsync<IdentityDbContext>(args);
await app.SeedIdentityAsync();
await app.RunAsync();

public partial class Program;
