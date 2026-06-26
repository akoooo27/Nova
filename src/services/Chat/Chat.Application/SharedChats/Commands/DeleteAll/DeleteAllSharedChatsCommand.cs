using ErrorOr;

using Mediator;

namespace Chat.Application.SharedChats.Commands.DeleteAll;

public class DeleteAllSharedChatsCommand : ICommand<ErrorOr<Deleted>>;