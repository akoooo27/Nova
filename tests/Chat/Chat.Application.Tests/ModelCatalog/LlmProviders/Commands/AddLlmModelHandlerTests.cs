using Chat.Application.ModelCatalog.LlmProviders.Commands.AddLlmModel;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Commands;

public sealed class AddLlmModelHandlerTests
{
    [Fact]
    public async Task HandleAddsModelToProviderAndSavesChanges()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        AddLlmModelHandler handler = new(providers, unitOfWork);
        AddLlmModelCommand command = new
        (
            ProviderId: provider.Id.Value,
            ExternalModelId: "gpt-4.1",
            Name: "GPT-4.1",
            Description: "General purpose model",
            ContextWindow: 128000,
            SupportsVision: true,
            SupportsReasoning: false,
            SupportsToolCalling: true
        );

        ErrorOr<LlmModelResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        LlmModelResult model = result.Value;
        Assert.Equal(provider.Id.Value, model.ProviderId);
        Assert.Equal(command.ExternalModelId, model.ExternalModelId);
        Assert.Equal(command.Name, model.Name);
        Assert.Equal(command.Description, model.Description);
        Assert.Equal(command.ContextWindow, model.ContextWindow);
        Assert.Equal(command.SupportsVision, model.SupportsVision);
        Assert.Equal(command.SupportsReasoning, model.SupportsReasoning);
        Assert.Equal(command.SupportsToolCalling, model.SupportsToolCalling);
        Assert.True(model.IsEnabled);
        Assert.Single(provider.Models);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProviderDoesNotExist()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        AddLlmModelHandler handler = new(providers, unitOfWork);
        Guid providerId = Guid.CreateVersion7();
        AddLlmModelCommand command = new
        (
            ProviderId: providerId,
            ExternalModelId: "gpt-4.1",
            Name: "GPT-4.1",
            Description: "General purpose model",
            ContextWindow: 128000,
            SupportsVision: true,
            SupportsReasoning: false,
            SupportsToolCalling: true
        );

        ErrorOr<LlmModelResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.NotFound", error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsConflictWhenExternalModelIdAlreadyExists()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        ExternalModelId externalModelId = TestCatalogFactory.CreateExternalModelId();
        ErrorOr<LlmModel> existingModelResult = provider.AddModel
        (
            externalModelId: externalModelId,
            profile: TestCatalogFactory.CreateProfile()
        );
        Assert.False(existingModelResult.IsError);

        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        AddLlmModelHandler handler = new(providers, unitOfWork);
        AddLlmModelCommand command = new
        (
            ProviderId: provider.Id.Value,
            ExternalModelId: externalModelId.Value,
            Name: "GPT-4.1 mini",
            Description: "Small general purpose model",
            ContextWindow: 128000,
            SupportsVision: true,
            SupportsReasoning: false,
            SupportsToolCalling: true
        );

        ErrorOr<LlmModelResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("LlmProvider.ModelAlreadyExists", error.Code);
        Assert.Single(provider.Models);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}