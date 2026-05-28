using FastEndpoints;

using IdentityIngress;
using IdentityIngress.Database;

using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IdentityIngressDbContext>(
    "identity-ingress-db",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

builder.Services.AddApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.UseHttpsRedirection();
app.UseFastEndpoints();

await app.RunAsync();