using Chat.Application.Chats.Errors;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.GetChat;

internal sealed class GetChatHandler(IUserContext userContext, IChatDetailReader reader)
    : IQueryHandler<GetChatQuery, ErrorOr<ChatDetailReadModel>>
{
    public async ValueTask<ErrorOr<ChatDetailReadModel>> Handle(GetChatQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(query.ChatId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId userId = userIdResult.Value;
        ChatId chatId = chatIdResult.Value;

        ChatDetailReadModel? chat = await reader.GetAsync
        (
            userId: userId,
            chatId: chatId,
            cancellationToken: cancellationToken
        );

        if (chat is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        return chat;
    }
}