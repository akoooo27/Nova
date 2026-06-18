namespace Chat.Application.Abstractions.Turns;

#pragma warning disable CA1008
public enum TurnRole
#pragma warning restore CA1008
{
    User = 1,
    Assistant = 2
}

public sealed record TurnMessage(TurnRole Role, string Text);

public sealed record TurnContext
(
    Guid TurnId,
    Guid ChatId,
    string UserId,
    string ExternalModelId,
    string SystemPrompt,
    TurnGenerationOptions GenerationOptions,
    IReadOnlyList<TurnMessage> Messages
);