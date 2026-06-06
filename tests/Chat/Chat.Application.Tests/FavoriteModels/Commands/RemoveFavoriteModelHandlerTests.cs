using Chat.Application.FavoriteModels.Commands.RemoveFavoriteModel;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.FavoriteModels.Commands;

public sealed class RemoveFavoriteModelHandlerTests
{
    [Fact]
    public async Task HandleRemovesFavoriteModelAndSavesChanges()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        LlmModelId modelId = LlmModelId.New();
        FavoriteModel favoriteModel = FavoriteModel.Create
        (
            userId: userId,
            llmModelId: modelId,
            createdAt: new DateTimeOffset(2026, 6, 6, 10, 15, 0, TimeSpan.Zero)
        );
        FakeFavoriteModelRepository favoriteModels = new();
        favoriteModels.AddExistingFavoriteModel(favoriteModel);
        FakeUnitOfWork unitOfWork = new();
        RemoveFavoriteModelHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            favoriteModels: favoriteModels,
            unitOfWork: unitOfWork
        );

        ErrorOr<Success> result = await handler.Handle
        (
            new RemoveFavoriteModelCommand(modelId.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Empty(favoriteModels.FavoriteModels);
        Assert.Same(favoriteModel, Assert.Single(favoriteModels.RemovedFavoriteModels));
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsSuccessWithoutSavingChangesWhenFavoriteModelDoesNotExist()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        FakeFavoriteModelRepository favoriteModels = new();
        FakeUnitOfWork unitOfWork = new();
        RemoveFavoriteModelHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            favoriteModels: favoriteModels,
            unitOfWork: unitOfWork
        );

        ErrorOr<Success> result = await handler.Handle
        (
            new RemoveFavoriteModelCommand(Guid.CreateVersion7()),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Empty(favoriteModels.FavoriteModels);
        Assert.Empty(favoriteModels.RemovedFavoriteModels);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}