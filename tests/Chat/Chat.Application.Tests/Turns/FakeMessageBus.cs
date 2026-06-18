using Shared.Application.Messaging;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];

    public Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : class
    {
        Published.Add(integrationEvent);

        return Task.CompletedTask;
    }

    public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default)
    {
        Published.Add(integrationEvent);

        return Task.CompletedTask;
    }
}