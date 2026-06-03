using Chat.Api;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddRedisDistributedCache("redis");

builder.AddNpgsqlDataSource("chat-db");

builder.AddNpgsqlDbContext<ChatDbContext>
(
    "chat-db",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention()
);

builder.Services.AddApi(builder.Configuration);

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseHttpsRedirection();

await app.RunAsync();