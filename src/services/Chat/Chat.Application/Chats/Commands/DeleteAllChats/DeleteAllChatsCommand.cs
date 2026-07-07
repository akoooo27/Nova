using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.DeleteAllChats;

public sealed record DeleteAllChatsCommand : ICommand<ErrorOr<Deleted>>;
