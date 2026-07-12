using Chat.Application.Abstractions.AgentRuns;
using Chat.Application.Abstractions.Turns;
using Chat.Application.AgentRuns.Errors;
using Chat.Domain.AgentRuns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;

using ErrorOr;

namespace Chat.Application.AgentRuns;

internal sealed class AgentRunContextBuilder(ILlmProviderRepository providers) : IAgentRunContextBuilder
{
    public async Task<ErrorOr<AgentRunContext>> BuildContextAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        AgentRun run,
        CancellationToken cancellationToken
    )
    {
        if (assistantMessage.LlmModelId is null)
        {
            return AgentRunOperationErrors.ModelNotConfigured(assistantMessage.Id);
        }

        LlmProvider? provider = await providers.GetByModelIdAsync(run.LlmModelId, cancellationToken);
        LlmModel? model = provider?.FindModel(run.LlmModelId);

        if (provider is null || model is null)
        {
            return AgentRunOperationErrors.ModelNotFound(run.LlmModelId);
        }

        List<TurnMessage> history = [];
        ChatMessage? taskMessage = assistantMessage.ParentMessageId is null
            ? null
            : thread.FindMessage(assistantMessage.ParentMessageId);
        ChatMessageId? cursor = taskMessage?.ParentMessageId;

        while (cursor is not null)
        {
            ChatMessage? message = thread.FindMessage(cursor);

            if (message is null)
            {
                break;
            }

            if (message.Content is not null && message.Status == MessageStatus.Completed)
            {
                history.Add(new TurnMessage
                (
                    Role: message.Role == MessageRole.User ? TurnRole.User : TurnRole.Assistant,
                    Text: message.Content.Value
                ));
            }

            cursor = message.ParentMessageId;
        }

        history.Reverse();

        return new AgentRunContext
        (
            RunId: run.Id.Value,
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            Kind: run.Kind,
            Task: run.Task.Value,
            ExternalModelId: model.ExternalModelId.Value,
            PriorConversation: history
        );
    }
}