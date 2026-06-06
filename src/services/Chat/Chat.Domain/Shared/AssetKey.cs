using ErrorOr;

namespace Chat.Domain.Shared;

public sealed record AssetKey
{
    public string Value { get; }

    private AssetKey(string value)
    {
        Value = value;
    }

    public static ErrorOr<AssetKey> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "AssetKey.Required",
                description: "Asset key is required."
            );
        }

        return new AssetKey(trimmed);
    }

    public override string ToString() => Value;
}