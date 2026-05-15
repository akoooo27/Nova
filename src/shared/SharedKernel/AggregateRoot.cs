namespace SharedKernel;

public abstract class AggregateRoot<TId>
    where TId : notnull
{
    public required TId Id { get; init; }

    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => [.. _domainEvents];

    protected void AddDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() =>
        _domainEvents.Clear();
}