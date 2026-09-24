using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.Engagement.Application;
using Academies.Engagement.Infrastructure;
using Academies.Engagement.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(EngagementServiceInfo.Name);
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddEngagementApplication();
builder.Services.AddEngagementInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseServiceDefaults();
await app.MigrateDatabaseAsync<EngagementDbContext>(args);
await app.RunAsync();

public partial class Program;
