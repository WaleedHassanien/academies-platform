using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.Finance.Application;
using Academies.Finance.Infrastructure;
using Academies.Finance.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(FinanceServiceInfo.Name);
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddFinanceApplication();
builder.Services.AddFinanceInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseServiceDefaults();
await app.MigrateDatabaseAsync<FinanceDbContext>(args);
await app.RunAsync();

public partial class Program;
