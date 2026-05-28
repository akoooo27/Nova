using FastEndpoints;

using IdentityIngress.Database;

using MassTransit;

using Microsoft.EntityFrameworkCore;

using Shared.Infrastructure.Messaging;

using SharedKernel.Application.Messaging;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IdentityIngressDbContext>(
    "identity-ingress-db",
    configureDbContextOptions: options => options.UseSnakeCaseNamingConvention());

builder.Services.AddFastEndpoints();
builder.Services.AddScoped<IMessageBus, MessageBus>();

builder.Services.AddMassTransit(configurator =>
{
    configurator.SetKebabCaseEndpointNameFormatter();

    configurator.AddEntityFrameworkOutbox<IdentityIngressDbContext>(outbox =>
    {
        outbox.UsePostgres();
        outbox.UseBusOutbox();
    });

    configurator.AddConfigureEndpointsCallback((context, _, endpointConfigurator) =>
    {
        endpointConfigurator.UseEntityFrameworkOutbox<IdentityIngressDbContext>(context);
    });

    configurator.UsingRabbitMq((context, rabbitMqConfigurator) =>
    {
        string rabbitMqConnectionString = context.GetRequiredService<IConfiguration>()
            .GetConnectionString("rabbitmq")
            ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required.");

        rabbitMqConfigurator.Host(new Uri(rabbitMqConnectionString));
        rabbitMqConfigurator.ConfigureEndpoints(context);
    });
});

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.UseHttpsRedirection();
app.UseFastEndpoints();

await app.RunAsync();