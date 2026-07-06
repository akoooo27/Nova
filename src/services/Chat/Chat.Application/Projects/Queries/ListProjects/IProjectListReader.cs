using Chat.Domain.Shared;

namespace Chat.Application.Projects.Queries.ListProjects;

public interface IProjectListReader
{
    Task<ProjectListReadModel> GetAsync
    (
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    );
}