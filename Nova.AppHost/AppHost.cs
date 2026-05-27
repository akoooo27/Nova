IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ParameterResource> auth0Domain = builder.AddParameter("auth0-domain", secret: true);
IResourceBuilder<ParameterResource> auth0Audience = builder.AddParameter("auth0-audience", secret: true);
IResourceBuilder<ParameterResource> auth0ClientId = builder.AddParameter("auth0-client-id", secret: true);
IResourceBuilder<ParameterResource> auth0ClientSecret = builder.AddParameter("auth0-client-secret", secret: true);

builder.AddProject<Projects.BFF>("bff")
    .WithHttpsEndpoint(port: 7001, name: "https")
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience)
    .WithEnvironment("Auth0__ClientId", auth0ClientId)
    .WithEnvironment("Auth0__ClientSecret", auth0ClientSecret);

await builder.Build().RunAsync();