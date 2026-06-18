using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

namespace Chat.Application.Tests.Turns;

internal sealed class RecordingTokenPublisher : ITokenPublisher
{
    public List<TurnEvent> Events { get; } = [];

    public int ResetCount { get; private set; }

    public Task PublishAsync(TurnEvent turnEvent, CancellationToken cancellationToken)
    {
        Events.Add(turnEvent);

        return Task.CompletedTask;
    }

    public Task ResetAsync(Guid turnId, CancellationToken cancellationToken)
    {
        ResetCount++;

        return Task.CompletedTask;
    }
}