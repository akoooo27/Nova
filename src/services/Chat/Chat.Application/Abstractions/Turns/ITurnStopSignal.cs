namespace Chat.Application.Abstractions.Turns;

public interface ITurnStopSignal
{
    Task RequestStopAsync(Guid assistantMessageId, CancellationToken cancellationToken);

    Task<bool> IsStopRequestedAsync(Guid assistantMessageId, CancellationToken cancellationToken);
}