using Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmModel;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.Events;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Commands;

public sealed class DisableLlmModelHandlerTests
{
    [Fact]
    public async Task HandleDisablesModelAndSavesChanges()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        DisableLlmModelHandler handler = new(providers, unitOfWork);
        DisableLlmModelCommand command = new(provider.Id.Value, model.Id.Value);

        ErrorOr<LlmModelResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(model.IsEnabled);
        LlmModelResult disabledModel = result.Value;
        Assert.Equal(model.Id.Value, disabledModel.Id);
        Assert.Equal(provider.Id.Value, disabledModel.ProviderId);
        Assert.False(disabledModel.IsEnabled);
        LlmModelAvailabilityChanged domainEvent =
            Assert.IsType<LlmModelAvailabilityChanged>(Assert.Single(provider.DomainEvents));
        Assert.Equal(provider.Id, domainEvent.ProviderId);
        Assert.Equal(model.Id, domainEvent.ModelId);
        Assert.False(domainEvent.IsEnabled);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProviderDoesNotExist()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        DisableLlmModelHandler handler = new(providers, unitOfWork);
        DisableLlmModelCommand command = new(Guid.CreateVersion7(), Guid.CreateVersion7());

        ErrorOr<LlmModelResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.NotFound", error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenModelDoesNotExist()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        DisableLlmModelHandler handler = new(providers, unitOfWork);
        DisableLlmModelCommand command = new(provider.Id.Value, Guid.CreateVersion7());

        ErrorOr<LlmModelResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.ModelNotFound", error.Code);
        Assert.Empty(provider.DomainEvents);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorsWithoutSaving()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        DisableLlmModelHandler handler = new(providers, unitOfWork);
        DisableLlmModelCommand command = new(Guid.Empty, Guid.Empty);

        ErrorOr<LlmModelResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, error => error.Code == "LlmProviderId.Empty");
        Assert.Contains(result.Errors, error => error.Code == "LlmModelId.Empty");
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