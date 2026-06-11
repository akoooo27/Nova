using Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmProvider;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Events;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Commands;

public sealed class DisableLlmProviderHandlerTests
{
    [Fact]
    public async Task HandleDisablesProviderAndSavesChanges()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        FakeLlmProviderRepository providers = new();
        providers.AddExistingProvider(provider);
        FakeUnitOfWork unitOfWork = new();
        DisableLlmProviderHandler handler = new(providers, unitOfWork);
        DisableLlmProviderCommand command = new(provider.Id.Value);

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(provider.IsEnabled);
        Assert.False(result.Value.IsEnabled);
        LlmProviderUpdated domainEvent = Assert.IsType<LlmProviderUpdated>(Assert.Single(provider.DomainEvents));
        Assert.Equal(provider.Id, domainEvent.ProviderId);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenProviderDoesNotExist()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        DisableLlmProviderHandler handler = new(providers, unitOfWork);
        DisableLlmProviderCommand command = new(Guid.CreateVersion7());

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
        DisableLlmProviderHandler handler = new(providers, unitOfWork);
        DisableLlmProviderCommand command = new(Guid.Empty);

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Validation, error.Type);
        Assert.Equal("LlmProviderId.Empty", error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}