namespace SharedKernel;

public abstract class AggregateRoot<TId> : IAggregateRoot
    where TId : notnull
{
    protected AggregateRoot() => Id = default!;

    protected AggregateRoot(TId id) => Id = id;

    public TId Id { get; }

    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void AddDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() =>
        _domainEvents.Clear();
}