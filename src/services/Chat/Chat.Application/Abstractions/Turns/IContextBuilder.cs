using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

using ErrorOr;

namespace Chat.Application.Abstractions.Turns;

public interface IContextBuilder
{
    Task<ErrorOr<TurnContext>> BuildContext
    (
        ChatThread chat,
        ChatMessage assistantMessage,
        RetrievedMemories memories,
        CancellationToken cancellationToken
    );
}