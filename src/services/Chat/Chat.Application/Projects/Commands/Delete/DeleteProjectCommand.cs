using ErrorOr;

using Mediator;

namespace Chat.Application.Projects.Commands.Delete;

public sealed record DeleteProjectCommand(Guid ProjectId) : ICommand<ErrorOr<Success>>;