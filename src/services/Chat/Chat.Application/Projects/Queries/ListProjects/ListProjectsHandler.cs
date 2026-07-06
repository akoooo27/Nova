using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Projects.Queries.ListProjects;

internal sealed class ListProjectsHandler(IUserContext userContext, IProjectListReader reader)
    : IQueryHandler<ListProjectsQuery, ErrorOr<ProjectListReadModel>>
{
    public async ValueTask<ErrorOr<ProjectListReadModel>> Handle(ListProjectsQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        return await reader.GetAsync
        (
            userId: userIdResult.Value,
            limit: query.Limit,
            offset: query.Offset,
            cancellationToken: cancellationToken
        );
    }
}