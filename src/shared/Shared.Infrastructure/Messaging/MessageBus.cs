using MassTransit;

using SharedKernel.Application.Messaging;

namespace Shared.Infrastructure.Messaging;

public sealed class MessageBus(IPublishEndpoint publishEndpoint) : IMessageBus
{
    public async Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : class
    {
        await publishEndpoint.Publish(integrationEvent, cancellationToken);
    }
}