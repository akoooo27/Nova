namespace SharedKernel;

public abstract class Entity<TId>
    where TId : notnull
{
    protected Entity() => Id = default!;

    protected Entity(TId id) => Id = id;

    public TId Id { get; }
}