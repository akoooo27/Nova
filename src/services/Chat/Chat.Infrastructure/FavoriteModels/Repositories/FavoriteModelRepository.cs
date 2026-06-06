using Chat.Domain.FavoriteModels;
using Chat.Domain.FavoriteModels.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.FavoriteModels.Repositories;

internal sealed class FavoriteModelRepository(ChatDbContext db) : IFavoriteModelRepository
{
    public async Task<FavoriteModel?> GetByIdAsync(FavoriteModelId id, CancellationToken cancellationToken = default)
    {
        return await db.FavoriteModels
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<FavoriteModel?> GetAsync
    (
        UserId userId,
        LlmModelId llmModelId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.FavoriteModels
            .FirstOrDefaultAsync(x => x.UserId == userId && x.LlmModelId == llmModelId, cancellationToken);
    }

    public void Add(FavoriteModel favoriteModel)
    {
        db.FavoriteModels.Add(favoriteModel);
    }

    public void Remove(FavoriteModel favoriteModel)
    {
        db.FavoriteModels.Remove(favoriteModel);
    }
}