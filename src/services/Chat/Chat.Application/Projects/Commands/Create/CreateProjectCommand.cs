using Chat.Application.Projects.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Projects.Commands.Create;

public sealed record CreateProjectCommand
(
    string Name,
    string? Instructions,
    string? Emoji,
    string? Theme
) : ICommand<ErrorOr<ProjectResult>>;