using Chat.Application.AgentRuns;

using MassTransit;

namespace Chat.Infrastructure.AgentRuns.Consumers;

public class AgentRunRequestedConsumer(AgentRunOrchestrator orchestrator) : IConsumer<AgentRunRequested>
{
    public async Task Consume(ConsumeContext<AgentRunRequested> context) =>
        await orchestrator.RunAsync(context.Message, context.CancellationToken);
}