namespace Shared.Application.Messaging;

public interface IMessageBus
{
    Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : class;

    Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default);
}