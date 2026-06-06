using ErrorOr;

namespace Chat.Domain.FavoriteModels.ValueObjects;

public sealed record FavoriteModelId
{
    public Guid Value { get; }

    private FavoriteModelId(Guid value)
    {
        Value = value;
    }

    public static FavoriteModelId New() => new(Guid.CreateVersion7());

    public static ErrorOr<FavoriteModelId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "FavoriteModelId.Empty",
                description: "Favorite model id cannot be empty."
            );
        }

        return new FavoriteModelId(value);
    }

    public static FavoriteModelId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty favorite model id.");

        return new FavoriteModelId(value);
    }

    public override string ToString() => Value.ToString();
}