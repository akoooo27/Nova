using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.ArchiveAllChats;

public sealed record ArchiveAllChatsCommand : ICommand<ErrorOr<Success>>;