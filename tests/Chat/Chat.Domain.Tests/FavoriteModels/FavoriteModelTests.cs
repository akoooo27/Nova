using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Domain.Tests.FavoriteModels;

public sealed class FavoriteModelTests
{
    [Fact]
    public void CreateInitializesFavoriteModel()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        LlmModelId llmModelId = LlmModelId.New();
        DateTimeOffset createdAt = new(2026, 6, 6, 10, 15, 0, TimeSpan.Zero);

        FavoriteModel favoriteModel = FavoriteModel.Create
        (
            userId: userId,
            llmModelId: llmModelId,
            createdAt: createdAt
        );

        Assert.NotEqual(Guid.Empty, favoriteModel.Id.Value);
        Assert.Equal(userId, favoriteModel.UserId);
        Assert.Equal(llmModelId, favoriteModel.LlmModelId);
        Assert.Equal(createdAt, favoriteModel.CreatedAt);
    }
}