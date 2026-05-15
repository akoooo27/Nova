using Shared.Api;
using Shared.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services
    .AddSharedInfra()
    .AddSharedApi();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseHttpsRedirection();

await app.RunAsync();