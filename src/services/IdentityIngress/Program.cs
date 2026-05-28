using FastEndpoints;
using FastEndpoints.Swagger;

using IdentityIngress;
using IdentityIngress.Database;

using Microsoft.EntityFrameworkCore;

using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IdentityIngressDbContext>(
    "identity-ingress-db",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

builder.Services.AddApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseHttpsRedirection();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerGen();

    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle("Identity Ingress API")
            .WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json")
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

app.UseFastEndpoints();

await app.RunAsync();