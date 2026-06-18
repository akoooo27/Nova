using MassTransit;

namespace Chat.Infrastructure.Turns.Consumers;

internal sealed class TurnRequestedConsumerDefinition : ConsumerDefinition<TurnRequestedConsumer>
{
    public TurnRequestedConsumerDefinition()
    {
        // In-flight LLM calls per worker replica. Scale replicas, then revisit this knob.
        ConcurrentMessageLimit = 4;
    }

    protected override void ConfigureConsumer
    (
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TurnRequestedConsumer> consumerConfigurator,
        IRegistrationContext context
    )
    {
        endpointConfigurator.UseMessageRetry(retry =>
            retry.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}