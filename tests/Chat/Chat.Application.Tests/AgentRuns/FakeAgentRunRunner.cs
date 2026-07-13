using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Turns;

namespace Chat.Application.Tests.AgentRuns;

internal sealed class FakeAgentRunRunner(
    Func<AgentRunContext, CancellationToken, IAsyncEnumerable<TurnEvent>> script) : IAgentRunRunner
{
    public WorkflowCheckpoint? LastCheckpoint { get; private set; }

    public IAsyncEnumerable<TurnEvent> RunAsync
    (
        AgentRunContext context,
        WorkflowCheckpoint? checkpoint,
        CancellationToken cancellationToken
    )
    {
        LastCheckpoint = checkpoint;

        return script(context, cancellationToken);
    }

    public static async IAsyncEnumerable<TurnEvent> Script
    (
        TurnEvent[] events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (TurnEvent turnEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return turnEvent;
        }
    }

    public static async IAsyncEnumerable<TurnEvent> EventsThenThrow
    (
        TurnEvent[] events,
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (TurnEvent turnEvent in Script(events, cancellationToken))
        {
            yield return turnEvent;
        }

        throw exception;
    }

    public static async IAsyncEnumerable<TurnEvent> Hang
    (
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }
}