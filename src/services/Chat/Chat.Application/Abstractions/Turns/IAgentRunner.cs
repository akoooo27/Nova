using Chat.Application.Turns;

namespace Chat.Application.Abstractions.Turns;

public interface IAgentRunner
{
    IAsyncEnumerable<TurnEvent> RunAsync(TurnContext context, CancellationToken cancellationToken);
}