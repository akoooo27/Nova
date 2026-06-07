using Chat.Application.ModelCatalog.LlmProviders.Commands.DeleteLlmProvider;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Commands;

public sealed class DeleteLlmProviderHandlerTests
{
    [Fact]
    public async Task HandleRemovesEmptyProviderAndSavesChanges()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        DeleteLlmProviderHandler handler = new(providers, unitOfWork);
        DeleteLlmProviderCommand command = new(provider.Id.Value);

        ErrorOr<Success> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Same(provider, Assert.Single(providers.RemovedProviders));
        Assert.Empty(providers.AddedProviders);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsConflictWhenProviderHasModels()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        AddModel(provider);
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        DeleteLlmProviderHandler handler = new(providers, unitOfWork);
        DeleteLlmProviderCommand command = new(provider.Id.Value);

        ErrorOr<Success> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("LlmProvider.CannotDeleteProviderWithModels", error.Code);
        Assert.Empty(providers.RemovedProviders);
        Assert.Same(provider, Assert.Single(providers.AddedProviders));
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProviderDoesNotExist()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        DeleteLlmProviderHandler handler = new(providers, unitOfWork);
        DeleteLlmProviderCommand command = new(Guid.CreateVersion7());

        ErrorOr<Success> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.NotFound", error.Code);
        Assert.Empty(providers.RemovedProviders);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorsWithoutSaving()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        DeleteLlmProviderHandler handler = new(providers, unitOfWork);
        DeleteLlmProviderCommand command = new(Guid.Empty);

        ErrorOr<Success> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("LlmProviderId.Empty", error.Code);
        Assert.Empty(providers.RemovedProviders);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static void AddModel(LlmProvider provider)
    {
        ErrorOr<LlmModel> result = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile()
        );

        Assert.False(result.IsError);
    }
}