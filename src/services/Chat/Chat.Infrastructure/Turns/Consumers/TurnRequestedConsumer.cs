using Chat.Application.Turns;

using MassTransit;

namespace Chat.Infrastructure.Turns.Consumers;

internal sealed class TurnRequestedConsumer(ChatTurnOrchestrator orchestrator) : IConsumer<TurnRequested>
{
    public async Task Consume(ConsumeContext<TurnRequested> context) =>
        await orchestrator.RunTurnAsync(context.Message, context.CancellationToken);
}