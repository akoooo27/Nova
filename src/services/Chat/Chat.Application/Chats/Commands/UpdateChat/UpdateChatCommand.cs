using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.UpdateChat;

public sealed record UpdateChatCommand
(
    Guid ChatId,
    string Title,
    bool IsPinned,
    bool IsArchived
) : ICommand<ErrorOr<ChatThreadResult>>;