using Chat.Domain.Projects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Projects.Repositories;

internal sealed class ProjectRepository(ChatDbContext db) : IProjectRepository
{
    public async Task<Project?> GetByIdAsync
    (
        ProjectId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.Projects
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
    }

    public void Add(Project project)
    {
        db.Projects.Add(project);
    }

    public void Remove(Project project)
    {
        db.Projects.Remove(project);
    }
}