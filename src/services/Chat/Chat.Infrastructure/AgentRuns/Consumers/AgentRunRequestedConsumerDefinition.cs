using MassTransit;

namespace Chat.Infrastructure.AgentRuns.Consumers;

internal sealed class AgentRunRequestedConsumerDefinition : ConsumerDefinition<AgentRunRequestedConsumer>
{
    private static readonly TimeSpan ConsumerAckTimeout = TimeSpan.FromMinutes(60);

    private const int QueueConcurrency = 1;

    public AgentRunRequestedConsumerDefinition()
    {
        ConcurrentMessageLimit = QueueConcurrency;
    }

    protected override void ConfigureConsumer
    (
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<AgentRunRequestedConsumer> consumerConfigurator,
        IRegistrationContext context
    )
    {
        endpointConfigurator.UseMessageRetry(retry =>
            retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));

        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMq)
        {
            rabbitMq.SetQueueArgument("x-consumer-timeout", (long)ConsumerAckTimeout.TotalMilliseconds);
        }
    }
}