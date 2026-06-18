namespace Chat.Application.Abstractions.Turns;

public sealed record TurnGenerationOptions
(
    bool ForceUseSearch = false
)
{
    public static TurnGenerationOptions Default { get; } = new();
}