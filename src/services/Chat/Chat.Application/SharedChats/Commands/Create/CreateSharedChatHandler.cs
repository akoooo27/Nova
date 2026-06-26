using Chat.Application.Chats.Errors;
using Chat.Application.SharedChats.Results;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.SharedChats.Commands.Create;

internal sealed class CreateSharedChatHandler(
    IUserContext userContext,
    IChatRepository chats,
    ISharedChatRepository sharedChats,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<CreateSharedChatCommand, ErrorOr<SharedChatResult>>
{
    public async ValueTask<ErrorOr<SharedChatResult>> Handle(CreateSharedChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(command.CurrentMessageId);

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

        UserId userId = userIdResult.Value;
        ChatId chatId = chatIdResult.Value;
        ChatMessageId currentMessageId = messageIdResult.Value;

        ChatThread? source = await chats.GetSnapshotByIdAsync(chatId, userId, cancellationToken);

        if (source is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        ErrorOr<Success> eligibility = source.ValidateShareAt(currentMessageId);

        if (eligibility.IsError)
        {
            return eligibility.Errors;
        }

        SharedChat? existing = await sharedChats.GetBySourceAsync
        (
            userId: userId,
            chatId: chatId,
            currentNodeId: currentMessageId,
            cancellationToken: cancellationToken
        );

        if (existing is not null)
        {
            return SharedChatResultMapper.ToResult(existing, alreadyExists: true);
        }

        SharedChat candidate = SharedChat.Create
        (
            userId: userId,
            chatId: chatId,
            currentMessageId: currentMessageId,
            title: source.Title,
            createdAt: dateTimeProvider.UtcNow
        );

        bool inserted = await sharedChats.TryAddAsync(candidate, cancellationToken);

        if (inserted)
        {
            return SharedChatResultMapper.ToResult(candidate, alreadyExists: false);
        }

        SharedChat winner = await sharedChats.GetBySourceAsync
        (
            userId: userId,
            chatId: chatId,
            currentNodeId: currentMessageId,
            cancellationToken: cancellationToken
        ) ?? throw new InvalidOperationException("Conflicting shared chat row conflict.");

        return SharedChatResultMapper.ToResult(winner, alreadyExists: true);
    }
}