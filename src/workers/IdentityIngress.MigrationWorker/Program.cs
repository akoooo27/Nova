using IdentityIngress.Database;
using IdentityIngress.MigrationWorker;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IdentityIngressDbContext>(
    "identity-ingress-db",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

builder.Services.AddScoped<MigrationRunner>();

await using WebApplication app = builder.Build();
await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

MigrationRunner runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
await runner.RunAsync();