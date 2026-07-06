using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Domain.Projects;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync
    (
        ProjectId id,
        UserId userId,
        CancellationToken cancellationToken = default
    );

    void Add(Project project);

    void Remove(Project project);
}