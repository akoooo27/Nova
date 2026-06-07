using Aspire.Hosting.DevTunnels;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<ParameterResource> redisPassword = builder.AddParameter("redis-password", secret: true);

IResourceBuilder<RedisResource> redis = builder.AddRedis("redis", password: redisPassword)
    .WithHostPort(6379);

IResourceBuilder<ParameterResource> postgresUser = builder.AddParameter("postgres-user", secret: true);
IResourceBuilder<ParameterResource> postgresPassword = builder.AddParameter("postgres-password", secret: true);

IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres", postgresUser, postgresPassword)
    .WithHostPort(5432)
    .WithDataVolume();

IResourceBuilder<PostgresDatabaseResource> bffDb = postgres.AddDatabase("bff-db");
IResourceBuilder<PostgresDatabaseResource> chatDb = postgres.AddDatabase("chat-db");
IResourceBuilder<PostgresDatabaseResource> identityIngressDb = postgres.AddDatabase("identity-ingress-db");

IResourceBuilder<ParameterResource> rabbitMqUser = builder.AddParameter("rabbitmq-user", secret: true);
IResourceBuilder<ParameterResource> rabbitMqPassword = builder.AddParameter("rabbitmq-password", secret: true);

IResourceBuilder<RabbitMQServerResource> rabbitMq = builder.AddRabbitMQ("rabbitmq", rabbitMqUser, rabbitMqPassword)
    .WithManagementPlugin();

IResourceBuilder<ProjectResource> bffMigrations = builder.AddProject<Projects.BFF_MigrationWorker>("bff-migrations")
    .WithReference(bffDb)
    .WaitFor(bffDb);

IResourceBuilder<ProjectResource> chatMigrations = builder.AddProject<Projects.Chat_MigrationWorker>("chat-migrations")
    .WithReference(chatDb)
    .WaitFor(chatDb);

IResourceBuilder<ProjectResource> identityIngressMigrations = builder
    .AddProject<Projects.IdentityIngress_MigrationWorker>("identity-ingress-migrations")
    .WithReference(identityIngressDb)
    .WaitFor(identityIngressDb);

IResourceBuilder<ParameterResource> auth0Domain = builder.AddParameter("auth0-domain", secret: true);
IResourceBuilder<ParameterResource> auth0Audience = builder.AddParameter("auth0-audience", secret: true);
IResourceBuilder<ParameterResource> auth0ClientId = builder.AddParameter("auth0-client-id", secret: true);
IResourceBuilder<ParameterResource> auth0ClientSecret = builder.AddParameter("auth0-client-secret", secret: true);
IResourceBuilder<ParameterResource> auth0EventsWebhookToken =
    builder.AddParameter("auth0-events-webhook-token", secret: true);

IResourceBuilder<ProjectResource> bff = builder.AddProject<Projects.BFF>("bff")
    .WithHttpEndpoint(port: 7000, name: "http")
    .WithHttpsEndpoint(port: 7001, name: "https")
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience)
    .WithEnvironment("Auth0__ClientId", auth0ClientId)
    .WithEnvironment("Auth0__ClientSecret", auth0ClientSecret)
    .WithReference(bffDb)
    .WithReference(redis)
    .WithReference(rabbitMq)
    .WaitFor(bffDb)
    .WaitFor(redis)
    .WaitFor(rabbitMq)
    .WaitForCompletion(bffMigrations);

IResourceBuilder<ProjectResource> identityIngress = builder.AddProject<Projects.IdentityIngress>("identity-ingress")
    .WithHttpEndpoint(port: 7100, name: "http")
    .WithHttpsEndpoint(port: 7101, name: "https")
    .WithEnvironment("Auth0Events__WebhookToken", auth0EventsWebhookToken)
    .WithReference(identityIngressDb)
    .WithReference(rabbitMq)
    .WaitFor(identityIngressDb)
    .WaitFor(rabbitMq)
    .WaitForCompletion(identityIngressMigrations);

builder.AddDevTunnel(
    name: "identity-ingress-dev-tunnel",
    tunnelId: "nova-identity-ingress",
    options: new DevTunnelOptions
    {
        Description = "Nova identity ingress tunnel for Auth0 Event Streams"
    })
    .WithReference(identityIngress.GetEndpoint("https"), allowAnonymous: true);

IResourceBuilder<ProjectResource> chatApi = builder.AddProject<Projects.Chat_Api>("chat-api")
    .WithHttpEndpoint(port: 7200, name: "http")
    .WithHttpsEndpoint(port: 7201, name: "https")
    .WithEnvironment("Auth0__Domain", auth0Domain)
    .WithEnvironment("Auth0__Audience", auth0Audience)
    .WithReference(redis)
    .WithReference(chatDb)
    .WithReference(rabbitMq)
    .WaitFor(redis)
    .WaitFor(chatDb)
    .WaitFor(rabbitMq)
    .WaitForCompletion(chatMigrations);

bff
    .WithEnvironment("ChatApi__Address", chatApi.GetEndpoint("https"))
    .WithReference(chatApi)
    .WaitFor(chatApi);

await builder.Build().RunAsync();