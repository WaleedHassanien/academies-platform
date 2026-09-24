using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.Subscription.Application;
using Academies.Subscription.Infrastructure;
using Academies.Subscription.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(SubscriptionServiceInfo.Name);
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddSubscriptionApplication();
builder.Services.AddSubscriptionInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseServiceDefaults();
await app.MigrateDatabaseAsync<SubscriptionDbContext>(args);
await app.SeedPlansAsync();
await app.RunAsync();

public partial class Program;
