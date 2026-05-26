namespace SharedKernel;

public abstract class Entity<TId>
    where TId : notnull
{
    public required TId Id { get; init; }
}