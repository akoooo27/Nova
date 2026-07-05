using Chat.Application.Projects.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Projects.Commands.Update;

public sealed record UpdateProjectCommand
(
    Guid ProjectId,
    string Name,
    string? Instructions,
    string? Emoji,
    string? Theme
) : ICommand<ErrorOr<ProjectResult>>;