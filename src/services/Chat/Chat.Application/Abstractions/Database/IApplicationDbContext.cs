using Chat.Domain.ModelCatalog;

using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Abstractions.Database;

public interface IApplicationDbContext
{
    DbSet<LlmProvider> LlmProviders { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}