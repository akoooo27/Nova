using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeTurnStopSignal : ITurnStopSignal
{
    private readonly HashSet<Guid> _requestedStops = [];
    private readonly Queue<bool> _scriptedResponses = [];

    public int CheckCount { get; private set; }

    public void Request(Guid assistantMessageId) =>
        _requestedStops.Add(assistantMessageId);

    public void EnqueueResponse(bool isStopRequested) =>
        _scriptedResponses.Enqueue(isStopRequested);

    public Task RequestStopAsync(Guid assistantMessageId, CancellationToken cancellationToken)
    {
        _requestedStops.Add(assistantMessageId);

        return Task.CompletedTask;
    }

    public Task<bool> IsStopRequestedAsync(Guid assistantMessageId, CancellationToken cancellationToken)
    {
        CheckCount++;

        if (_scriptedResponses.TryDequeue(out bool scripted))
        {
            return Task.FromResult(scripted);
        }

        return Task.FromResult(_requestedStops.Contains(assistantMessageId));
    }
}