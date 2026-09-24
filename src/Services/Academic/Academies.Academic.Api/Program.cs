using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.Academic.Application;
using Academies.Academic.Infrastructure;
using Academies.Academic.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(AcademicServiceInfo.Name);
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddAcademicApplication();
builder.Services.AddAcademicInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseServiceDefaults();
await app.MigrateDatabaseAsync<AcademicDbContext>(args);
await app.RunAsync();

public partial class Program;
