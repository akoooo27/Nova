using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Projects.Queries.GetProjectChats;

public interface IProjectChatListReader
{
    Task<ProjectChatListReadModel?> GetAsync
    (
        ProjectId projectId,
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    );
}