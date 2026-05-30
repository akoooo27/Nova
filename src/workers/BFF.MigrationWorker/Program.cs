using BFF.Database;
using BFF.MigrationWorker;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<BffSessionDbContext>("bff-db");

builder.Services.AddScoped<MigrationRunner>();

await using WebApplication app = builder.Build();
await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

MigrationRunner runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
await runner.RunAsync();