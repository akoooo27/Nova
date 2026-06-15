using Chat.Application;
using Chat.CleanupWorker;
using Chat.Infrastructure;
using Chat.Infrastructure.Database;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDataSource("chat-db");

builder.Services.AddDbContext<ChatDbContext>((sp, options) =>
{
    NpgsqlDataSource dataSource = sp.GetRequiredService<NpgsqlDataSource>();

    options
        .UseNpgsql(dataSource)
        .UseSnakeCaseNamingConvention();
});

builder.EnrichNpgsqlDbContext<ChatDbContext>();

builder.Services
    .AddApplication()
    .AddCleanupWorkerInfrastructure();

builder.AddTemporaryChatCleanup();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapTemporaryChatCleanupDashboard();
app.UseTemporaryChatCleanupRecurringJob();

await app.RunAsync();