using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeAgentRunner(Func<TurnContext, IAsyncEnumerable<TurnEvent>> script) : IAgentRunner
{
    public IAsyncEnumerable<TurnEvent> RunAsync(TurnContext context, CancellationToken cancellationToken) =>
        script(context);

    public static async IAsyncEnumerable<TurnEvent> Tokens
    (
        Guid turnId,
        string[] tokens,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (string token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new TokenEvent(turnId, token);
        }
    }

    public static async IAsyncEnumerable<TurnEvent> TokenThenThrow
    (
        Guid turnId,
        string token,
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new TokenEvent(turnId, token);
        throw exception;
    }
}