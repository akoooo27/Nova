using Chat.Application.Abstractions.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

using ErrorOr;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeContextBuilder : IContextBuilder
{
    public Task<ErrorOr<TurnContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        RetrievedMemories memories,
        CancellationToken cancellationToken
    )
    {
        TurnContext context = new
        (
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            ExternalModelId: "gpt-4.1",
            SystemPrompt: "test",
            Messages: [new TurnMessage(TurnRole.User, "Hello")]
        );

        return Task.FromResult<ErrorOr<TurnContext>>(context);
    }
}