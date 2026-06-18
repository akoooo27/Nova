using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Chats.Commands.UpdateChat;

internal sealed class UpdateChatHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<UpdateChatCommand, ErrorOr<ChatThreadResult>>
{
    public async ValueTask<ErrorOr<ChatThreadResult>> Handle(UpdateChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatTitle> titleResult = ChatTitle.Create(command.Title);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        if (titleResult.IsError)
        {
            errors.AddRange(titleResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ChatId chatId = chatIdResult.Value;
        UserId userId = userIdResult.Value;
        ChatTitle title = titleResult.Value;

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

        thread.Rename(title);

        DateTimeOffset now = dateTimeProvider.UtcNow;

        if (command.IsPinned) thread.Pin(now); else thread.Unpin();
        if (command.IsArchived) thread.Archive(); else thread.Unarchive();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return thread.ToResult();
    }
}