using Chat.Domain.SharedChats;

namespace Chat.Application.SharedChats.Results;

public static class SharedChatResultMapper
{
    public static SharedChatResult ToResult(SharedChat sharedChat, bool alreadyExists) =>
        new
        (
            Id: sharedChat.Id.Value,
            Title: sharedChat.Title.Value,
            ChatId: sharedChat.ChatId.Value,
            CurrentMessageId: sharedChat.CurrentMessageId.Value,
            CreatedAt: sharedChat.CreatedAt,
            AlreadyExists: alreadyExists
        );
}