using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns.Errors;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.Personalizations;
using Chat.Domain.Projects;

using ErrorOr;

namespace Chat.Application.Turns;

public sealed class ContextBuilder(
    ILlmProviderRepository providers,
    IPersonalizationRepository personalizations,
    IProjectRepository projects)
    : IContextBuilder
{
    private const string DefaultSystemPrompt = "You are Nova, a helpful AI assistant.";

    public async Task<ErrorOr<TurnContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        RetrievedMemories memories,
        TurnGenerationOptions generationOptions,
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

            if (HasContextContent(message) && message.Content is { } content)
            {
                TurnMessage turnMessage = new
                (
                    Role: message.Role == MessageRole.User
                        ? TurnRole.User
                        : TurnRole.Assistant,
                    Text: content.Value
                );

                history.Add(turnMessage);
            }

            cursor = message.ParentMessageId;
        }

        history.Reverse();

        Personalization? personalization = await personalizations.GetByUserIdAsync(thread.UserId, cancellationToken);

        Project? project = thread.ProjectId is null
            ? null
            : await projects.GetByIdAsync
            (
                id: thread.ProjectId,
                userId: thread.UserId,
                cancellationToken: cancellationToken
            );

        string systemPrompt = PersonalizationSystemPrompt.Compose
        (
            basePrompt: DefaultSystemPrompt,
            project: project,
            personalization: personalization
        );

        return new TurnContext
        (
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            ExternalModelId: model.ExternalModelId.Value,
            SystemPrompt: systemPrompt,
            GenerationOptions: generationOptions,
            Messages: history
        );
    }

    private static bool HasContextContent(ChatMessage message) =>
        message.Content is not null
        && message.Status is MessageStatus.Completed or MessageStatus.Stopped;
}