IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Gateway_Api>("gateway-api");

await builder.Build().RunAsync();