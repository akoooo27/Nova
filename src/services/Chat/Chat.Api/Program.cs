using Chat.Api;
using Chat.Infrastructure.Database;

using FastEndpoints;
using FastEndpoints.Swagger;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddRedisDistributedCache("redis");

builder.AddNpgsqlDataSource("chat-db");

builder.Services.AddDbContext<ChatDbContext>((sp, options) =>
{
    NpgsqlDataSource dataSource = sp.GetRequiredService<NpgsqlDataSource>();

    options
        .UseNpgsql(dataSource)
        .UseSnakeCaseNamingConvention();
});

builder.EnrichNpgsqlDbContext<ChatDbContext>();

builder.Services.AddApi(builder.Configuration);

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerGen();

    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("Chat API")
            .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json")
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(c =>
{
    c.Versioning.Prefix = "v";
    c.Versioning.DefaultVersion = 1;
    c.Versioning.PrependToRoute = true;
});

await app.RunAsync();