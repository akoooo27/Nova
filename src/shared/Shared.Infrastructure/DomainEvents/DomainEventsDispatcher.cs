using System.Collections.Concurrent;
using System.Reflection;

using Mediator;

using SharedKernel;

namespace Shared.Infrastructure.DomainEvents;

internal sealed class DomainEventsDispatcher(IPublisher publisher) : IDomainEventsDispatcher
{
    private static readonly MethodInfo PublishMethodDefinition = typeof(IPublisher)
        .GetMethods()
        .Single(static m => m is { Name: nameof(IPublisher.Publish), IsGenericMethod: true });

    private static readonly ConcurrentDictionary<Type, Type> WrapperTypeCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> PublishMethodCache = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {

        foreach (IDomainEvent domainEvent in domainEvents)
        {
            Type eventType = domainEvent.GetType();

            Type wrapperType = WrapperTypeCache.GetOrAdd
            (
                eventType,
                static t => typeof(DomainEventNotification<>).MakeGenericType(t)
            );

            MethodInfo publishMethod = PublishMethodCache.GetOrAdd
            (
                wrapperType,
                static t => PublishMethodDefinition.MakeGenericMethod(t)
            );

            object wrapper = Activator.CreateInstance(wrapperType, domainEvent)!;

            ValueTask publishTask = (ValueTask)publishMethod.Invoke
            (
                publisher,
                [wrapper, cancellationToken]
            )!;

            await publishTask;
        }
    }
}