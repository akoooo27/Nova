using Chat.Application.Abstractions.Database;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;
using Shared.Application.Messaging;

using SharedKernel;

namespace Chat.Application.Chats.Commands.RegenerateMessage;

internal sealed class RegenerateMessageHandler(
    IUserContext userContext,
    IChatRepository chats,
    ILlmProviderRepository providers,
    IMessageBus bus,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<RegenerateMessageCommand, ErrorOr<TurnStartedResult>>
{
    public async ValueTask<ErrorOr<TurnStartedResult>> Handle(RegenerateMessageCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(command.MessageId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        if (messageIdResult.IsError)
        {
            errors.AddRange(messageIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ChatId chatId = chatIdResult.Value;
        UserId userId = userIdResult.Value;
        ChatMessageId messageId = messageIdResult.Value;
        TurnGenerationOptions generationOptions = new(ForceUseSearch: command.ForceUseSearch);

        ChatThread? thread = await chats.GetByIdAsync
        (
            id: chatId,
            userId: userId,
            cancellationToken: cancellationToken
        );

        if (thread is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        ChatMessage? target = thread.FindMessage(messageId);

        if (target is null)
        {
            return ChatErrors.MessageNotFound(messageId);
        }

        ErrorOr<LlmModelId> modelIdResult = command.ModelId is { } overrideModelId
            ? LlmModelId.Create(overrideModelId)
            : ResolveTargetModel(target);

        if (modelIdResult.IsError)
        {
            return modelIdResult.Errors;
        }

        LlmModelId modelId = modelIdResult.Value;

        ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
        (
            providers: providers,
            modelId: modelId,
            cancellationToken: cancellationToken,
            requiresToolCalling: generationOptions.ForceUseSearch
        );

        if (usabilityResult.IsError)
        {
            return usabilityResult.Errors;
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ErrorOr<ChatMessage> siblingResult = thread.RegenerateAssistant
        (
            messageId: messageId,
            llmModelId: modelId,
            createdAt: now
        );

        if (siblingResult.IsError)
        {
            return siblingResult.Errors;
        }

        ChatMessage sibling = siblingResult.Value;

        TurnRequested turnRequested = new
        (
            ChatId: thread.Id.Value,
            UserId: userId.Value,
            AssistantMessageId: sibling.Id.Value,
            Options: generationOptions
        );

        // Published BEFORE SaveChangesAsync on purpose: the MassTransit bus outbox buffers
        // this and writes it to the outbox table inside the same transaction (spec: no dual-write).
        await bus.PublishAsync(turnRequested, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new TurnStartedResult
        (
            ChatId: thread.Id.Value,
            UserMessageId: sibling.ParentMessageId!.Value,
            AssistantMessageId: sibling.Id.Value
        );
    }

    private static ErrorOr<LlmModelId> ResolveTargetModel(ChatMessage target) =>
        target.LlmModelId is { } existing
            ? existing
            : ChatErrors.RegenerationTargetMustBeAssistant(target.Id);
}