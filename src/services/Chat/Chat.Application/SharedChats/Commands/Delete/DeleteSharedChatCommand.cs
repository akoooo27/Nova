using ErrorOr;

using Mediator;

namespace Chat.Application.SharedChats.Commands.Delete;

public sealed record DeleteSharedChatCommand(Guid SharedChatId) : ICommand<ErrorOr<Deleted>>;