using Chat.Application.FavoriteModels.Commands.AddFavoriteModel;
using Chat.Application.FavoriteModels.Results;
using Chat.Application.Tests.ModelCatalog;
using Chat.Application.Tests.ModelCatalog.LlmProviders;
using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.FavoriteModels.Commands;

public sealed class AddFavoriteModelHandlerTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 6, 6, 10, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAddsFavoriteModelAndSavesChanges()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);
        FakeFavoriteModelRepository favoriteModels = new();
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        AddFavoriteModelHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            favoriteModels: favoriteModels,
            llmProviders: providers,
            unitOfWork: unitOfWork,
            dateTimeProvider: new FakeDateTimeProvider(UtcNow)
        );

        ErrorOr<FavoriteModelResult> result = await handler.Handle
        (
            new AddFavoriteModelCommand(model.Id.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        FavoriteModel favoriteModel = Assert.Single(favoriteModels.FavoriteModels);
        Assert.Equal(favoriteModel.Id.Value, result.Value.Id);
        Assert.Equal(userId.Value, result.Value.UserId);
        Assert.Equal(model.Id.Value, result.Value.LlmModelId);
        Assert.Equal(UtcNow, result.Value.CreatedAt);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsExistingFavoriteModelWithoutSavingChanges()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        LlmModelId modelId = LlmModelId.New();
        FavoriteModel existingFavoriteModel = FavoriteModel.Create
        (
            userId: userId,
            llmModelId: modelId,
            createdAt: UtcNow.AddHours(-1)
        );
        FakeFavoriteModelRepository favoriteModels = new();
        favoriteModels.AddExistingFavoriteModel(existingFavoriteModel);
        FakeUnitOfWork unitOfWork = new();
        AddFavoriteModelHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            favoriteModels: favoriteModels,
            llmProviders: new FakeLlmProviderRepository(),
            unitOfWork: unitOfWork,
            dateTimeProvider: new FakeDateTimeProvider(UtcNow)
        );

        ErrorOr<FavoriteModelResult> result = await handler.Handle
        (
            new AddFavoriteModelCommand(modelId.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal(existingFavoriteModel.Id.Value, result.Value.Id);
        Assert.Single(favoriteModels.FavoriteModels);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenModelDoesNotExist()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        FakeUnitOfWork unitOfWork = new();
        AddFavoriteModelHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            favoriteModels: new FakeFavoriteModelRepository(),
            llmProviders: new FakeLlmProviderRepository(),
            unitOfWork: unitOfWork,
            dateTimeProvider: new FakeDateTimeProvider(UtcNow)
        );
        Guid modelId = Guid.CreateVersion7();

        ErrorOr<FavoriteModelResult> result = await handler.Handle
        (
            new AddFavoriteModelCommand(modelId),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("FavoriteModel.LlmModelNotFound", error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsConflictWhenModelIsDisabled()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);
        ErrorOr<Success> disableResult = provider.DisableModel(model.Id);
        Assert.False(disableResult.IsError);
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        AddFavoriteModelHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            favoriteModels: new FakeFavoriteModelRepository(),
            llmProviders: providers,
            unitOfWork: unitOfWork,
            dateTimeProvider: new FakeDateTimeProvider(UtcNow)
        );

        ErrorOr<FavoriteModelResult> result = await handler.Handle
        (
            new AddFavoriteModelCommand(model.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("FavoriteModel.LlmModelDisabled", error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static LlmModel AddModel(LlmProvider provider)
    {
        ErrorOr<LlmModel> result = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile()
        );

        Assert.False(result.IsError);

        return result.Value;
    }
}