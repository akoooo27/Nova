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
            // x-consumer-timeout is a quorum-queue-only argument; RabbitMQ rejects it on a classic
            // queue ("invalid arg 'x-consumer-timeout' ... of queue type rabbit_classic_queue"),
            // which faults the receive endpoint. A quorum queue also suits this durable,
            // one-in-flight, long-running job queue.
            rabbitMq.SetQuorumQueue();
            rabbitMq.SetQueueArgument("x-consumer-timeout", (long)ConsumerAckTimeout.TotalMilliseconds);
        }
    }
}