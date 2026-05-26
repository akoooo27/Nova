using Mediator;

using SharedKernel;

namespace Shared.Infrastructure.DomainEvents;

public sealed record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent) : INotification
    where TDomainEvent : IDomainEvent;