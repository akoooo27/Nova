using Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmModelProfile;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.Events;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Commands;

public sealed class UpdateLlmModelProfileHandlerTests
{
    [Fact]
    public async Task HandleUpdatesModelProfileAndSavesChanges()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = AddModel(provider);
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        UpdateLlmModelProfileHandler handler = new(providers, unitOfWork);
        UpdateLlmModelProfileCommand command = new
        (
            ProviderId: provider.Id.Value,
            ModelId: model.Id.Value,
            Name: "GPT-4.1 mini",
            Description: "Small general purpose model",
            ContextWindow: 64000,
            SupportsVision: false,
            SupportsReasoning: true,
            SupportsToolCalling: false
        );

        ErrorOr<LlmModelResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        LlmModelResult updatedModel = result.Value;
        Assert.Equal(model.Id.Value, updatedModel.Id);
        Assert.Equal(provider.Id.Value, updatedModel.ProviderId);
        Assert.Equal(command.Name, updatedModel.Name);
        Assert.Equal(command.Description, updatedModel.Description);
        Assert.Equal(command.ContextWindow, updatedModel.ContextWindow);
        Assert.Equal(command.SupportsVision, updatedModel.SupportsVision);
        Assert.Equal(command.SupportsReasoning, updatedModel.SupportsReasoning);
        Assert.Equal(command.SupportsToolCalling, updatedModel.SupportsToolCalling);
        LlmModelProfileUpdated domainEvent = Assert.IsType<LlmModelProfileUpdated>(Assert.Single(provider.DomainEvents));
        Assert.Equal(provider.Id, domainEvent.ProviderId);
        Assert.Equal(model.Id, domainEvent.ModelId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProviderDoesNotExist()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        UpdateLlmModelProfileHandler handler = new(providers, unitOfWork);
        UpdateLlmModelProfileCommand command = new
        (
            ProviderId: Guid.CreateVersion7(),
            ModelId: Guid.CreateVersion7(),
            Name: "GPT-4.1 mini",
            Description: "Small general purpose model",
            ContextWindow: 64000,
            SupportsVision: false,
            SupportsReasoning: true,
            SupportsToolCalling: false
        );

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
        UpdateLlmModelProfileHandler handler = new(providers, unitOfWork);
        UpdateLlmModelProfileCommand command = new
        (
            ProviderId: provider.Id.Value,
            ModelId: Guid.CreateVersion7(),
            Name: "GPT-4.1 mini",
            Description: "Small general purpose model",
            ContextWindow: 64000,
            SupportsVision: false,
            SupportsReasoning: true,
            SupportsToolCalling: false
        );

        ErrorOr<LlmModelResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.ModelNotFound", error.Code);
        Assert.Empty(provider.Models);
        Assert.Empty(provider.DomainEvents);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static LlmModel AddModel(LlmProvider provider)
    {
        ErrorOr<LlmModel> result = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile()
        );

        return result.Value;
    }
}