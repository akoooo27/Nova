using Chat.Application.ModelCatalog.LlmProviders.Commands.EnableLlmProvider;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Events;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Commands;

public sealed class EnableLlmProviderHandlerTests
{
    [Fact]
    public async Task HandleEnablesProviderAndSavesChanges()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        provider.Disable();
        provider.ClearDomainEvents();
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        EnableLlmProviderHandler handler = new(providers, unitOfWork);
        EnableLlmProviderCommand command = new(provider.Id.Value);

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(provider.IsEnabled);
        Assert.True(result.Value.IsEnabled);
        LlmProviderUpdated domainEvent = Assert.IsType<LlmProviderUpdated>(Assert.Single(provider.DomainEvents));
        Assert.Equal(provider.Id, domainEvent.ProviderId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProviderDoesNotExist()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        EnableLlmProviderHandler handler = new(providers, unitOfWork);
        EnableLlmProviderCommand command = new(Guid.CreateVersion7());

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("LlmProvider.NotFound", error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorsWithoutSaving()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        EnableLlmProviderHandler handler = new(providers, unitOfWork);
        EnableLlmProviderCommand command = new(Guid.Empty);

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("LlmProviderId.Empty", error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}