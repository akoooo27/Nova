using Chat.Application.Projects.Errors;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Projects.Queries.GetProjectChats;

internal sealed class GetProjectChatsHandler(IUserContext userContext, IProjectChatListReader reader)
    : IQueryHandler<GetProjectChatsQuery, ErrorOr<ProjectChatListReadModel>>
{
    public async ValueTask<ErrorOr<ProjectChatListReadModel>> Handle(GetProjectChatsQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ProjectId> projectIdResult = ProjectId.Create(query.ProjectId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (projectIdResult.IsError)
        {
            errors.AddRange(projectIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ProjectChatListReadModel? chats = await reader.GetAsync
        (
            projectId: projectIdResult.Value,
            userId: userIdResult.Value,
            limit: query.Limit,
            offset: query.Offset,
            cancellationToken: cancellationToken
        );

        if (chats is null)
        {
            return ProjectOperationErrors.ProjectNotFound(projectIdResult.Value);
        }

        return chats;
    }
}