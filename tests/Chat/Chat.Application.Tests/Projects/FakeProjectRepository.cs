using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Projects;

internal sealed class FakeProjectRepository : IProjectRepository
{
    private readonly List<Project> _projects = [];

    public IReadOnlyList<Project> Projects => _projects;

    public void AddExisting(Project project) => _projects.Add(project);

    public Task<Project?> GetByIdAsync
    (
        ProjectId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        Project? project = _projects.FirstOrDefault(x => x.Id == id && x.UserId == userId);

        return Task.FromResult(project);
    }

    public void Add(Project project) => _projects.Add(project);

    public void Remove(Project project) => _projects.Remove(project);
}