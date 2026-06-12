using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns.Errors;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;

using ErrorOr;

namespace Chat.Application.Turns;

public sealed class ContextBuilder(ILlmProviderRepository providers) : IContextBuilder
{
    private const string DefaultSystemPrompt = "You are Nova, a helpful AI assistant.";

    public async Task<ErrorOr<TurnContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        RetrievedMemories memories,
        CancellationToken cancellationToken
    )
    {
        _ = memories; // Reserved until the memory design lands (spec Rule 8).

        if (assistantMessage.LlmModelId is null)
        {
            return TurnOperationErrors.ModelNotConfigured(assistantMessage.Id);
        }

        LlmProvider? provider = await providers.GetByModelIdAsync(assistantMessage.LlmModelId, cancellationToken);
        LlmModel? model = provider?.FindModel(assistantMessage.LlmModelId);

        if (provider is null || model is null)
        {
            return TurnOperationErrors.ModelNotFound(assistantMessage.LlmModelId);
        }

        List<TurnMessage> history = [];
        ChatMessageId? cursor = assistantMessage.ParentMessageId;

        while (cursor is not null)
        {
            ChatMessage? message = thread.FindMessage(cursor);

            if (message is null)
            {
                break;
            }

            if (message.Content is not null && message.Status == MessageStatus.Completed)
            {
                TurnMessage turnMessage = new
                (
                    Role: message.Role == MessageRole.User
                        ? TurnRole.User
                        : TurnRole.Assistant,
                    Text: message.Content.Value
                );

                history.Add(turnMessage);
            }

            cursor = message.ParentMessageId;
        }

        history.Reverse();

        return new TurnContext
        (
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            ExternalModelId: model.ExternalModelId.Value,
            SystemPrompt: DefaultSystemPrompt,
            Messages: history
        );
    }
}