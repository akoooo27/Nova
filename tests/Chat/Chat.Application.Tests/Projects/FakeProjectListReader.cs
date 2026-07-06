using Chat.Application.Projects.Queries.ListProjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Projects;

internal sealed class FakeProjectListReader(ProjectListReadModel readModel) : IProjectListReader
{
    public UserId? RequestedUserId { get; private set; }

    public int? RequestedLimit { get; private set; }

    public int? RequestedOffset { get; private set; }

    public int GetCallCount { get; private set; }

    public Task<ProjectListReadModel> GetAsync
    (
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        RequestedUserId = userId;
        RequestedLimit = limit;
        RequestedOffset = offset;
        GetCallCount++;

        return Task.FromResult(readModel);
    }
}