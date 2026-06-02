using Chat.Infrastructure.Database;
using Chat.MigrationWorker;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Shared.Infrastructure.DomainEvents;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<ChatDbContext>(
    "chat-db",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

builder.Services.AddScoped<IDomainEventsDispatcher, NoOpDomainEventsDispatcher>();
builder.Services.AddScoped<MigrationRunner>();

await using WebApplication app = builder.Build();
await using AsyncServiceScope scope = app.Services.CreateAsyncScope();

MigrationRunner runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
await runner.RunAsync();