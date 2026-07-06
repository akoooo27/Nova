using Chat.Application.Projects.Queries.GetProjectChats;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Projects;

internal sealed class FakeProjectChatListReader(ProjectChatListReadModel? readModel) : IProjectChatListReader
{
    public ProjectId? RequestedProjectId { get; private set; }

    public UserId? RequestedUserId { get; private set; }

    public int? RequestedLimit { get; private set; }

    public int? RequestedOffset { get; private set; }

    public int GetCallCount { get; private set; }

    public Task<ProjectChatListReadModel?> GetAsync
    (
        ProjectId projectId,
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        RequestedProjectId = projectId;
        RequestedUserId = userId;
        RequestedLimit = limit;
        RequestedOffset = offset;
        GetCallCount++;

        return Task.FromResult(readModel);
    }
}