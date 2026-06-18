using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

using ErrorOr;

namespace Chat.Application.Abstractions.Turns;

public interface IContextBuilder
{
    Task<ErrorOr<TurnContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        RetrievedMemories memories,
        TurnGenerationOptions generationOptions,
        CancellationToken cancellationToken
    );
}