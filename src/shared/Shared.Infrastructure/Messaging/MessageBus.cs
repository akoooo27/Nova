using MassTransit;

using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging;

public sealed class MessageBus(IPublishEndpoint publishEndpoint) : IMessageBus
{
    public async Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : class
    {
        await publishEndpoint.Publish(integrationEvent, cancellationToken);
    }

    public async Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default)
    {
        await publishEndpoint.Publish(integrationEvent, cancellationToken);
    }
}