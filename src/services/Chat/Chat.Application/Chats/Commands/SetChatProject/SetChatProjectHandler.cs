using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Application.Projects.Errors;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Chats.Commands.SetChatProject;

internal sealed class SetChatProjectHandler(
    IUserContext userContext,
    IChatRepository chats,
    IProjectRepository projects,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<SetChatProjectCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(SetChatProjectCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        ProjectId? targetProjectId = null;

        if (command.ProjectId.HasValue)
        {
            ErrorOr<ProjectId> projectIdResult = ProjectId.Create(command.ProjectId.Value);

            if (projectIdResult.IsError)
            {
                errors.AddRange(projectIdResult.Errors);
            }
            else
            {
                targetProjectId = projectIdResult.Value;
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ChatId chatId = chatIdResult.Value;
        UserId userId = userIdResult.Value;

        ChatThread? thread = await chats.GetByIdAsync
        (
            id: chatId,
            userId: userId,
            cancellationToken: cancellationToken
        );

        if (thread is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        if (targetProjectId is null)
        {
            thread.RemoveFromProject(now);
        }
        else
        {
            Project? project = await projects.GetByIdAsync
            (
                id: targetProjectId,
                userId: userId,
                cancellationToken: cancellationToken
            );

            if (project is null)
            {
                return ProjectOperationErrors.ProjectNotFound(targetProjectId);
            }

            ErrorOr<Success> moveResult = thread.MoveToProject(targetProjectId, now);

            if (moveResult.IsError)
            {
                return moveResult.Errors;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}