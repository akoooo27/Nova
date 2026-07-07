using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.DeleteChat;

public sealed record DeleteChatCommand(Guid ChatId) : ICommand<ErrorOr<Deleted>>;
