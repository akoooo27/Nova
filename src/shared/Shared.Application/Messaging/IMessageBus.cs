namespace SharedKernel.Application.Messaging;

public interface IMessageBus
{
    Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : class;
}