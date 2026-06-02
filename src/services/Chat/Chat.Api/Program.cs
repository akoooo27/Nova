WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

app.UseHttpsRedirection();

await app.RunAsync();