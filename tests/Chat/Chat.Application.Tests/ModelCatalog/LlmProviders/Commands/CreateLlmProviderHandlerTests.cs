using Chat.Application.ModelCatalog.LlmProviders.Commands.CreateLlmProvider;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Commands;

public sealed class CreateLlmProviderHandlerTests
{
    [Fact]
    public async Task HandleAddsProviderAndSavesChanges()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        CreateLlmProviderHandler handler = new(providers, unitOfWork);
        CreateLlmProviderCommand command = new
        (
            Name: "OpenAI",
            Slug: "openai",
            SortOrder: 2,
            LogoKey: "llm-providers/openai.svg"
        );

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        LlmProviderResult provider = result.Value;
        Assert.NotEqual(Guid.Empty, provider.Id);
        Assert.Equal(command.Name, provider.Name);
        Assert.Equal(command.Slug, provider.Slug);
        Assert.Equal(command.SortOrder, provider.SortOrder);
        Assert.Equal(command.LogoKey, provider.LogoKey);
        Assert.Empty(provider.Models);
        Assert.Single(providers.AddedProviders);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsConflictWhenSlugAlreadyExists()
    {
        FakeLlmProviderRepository providers = new();
        providers.AddExistingSlug(ProviderSlug.FromDatabase("openai"));
        FakeUnitOfWork unitOfWork = new();
        CreateLlmProviderHandler handler = new(providers, unitOfWork);
        CreateLlmProviderCommand command = new
        (
            Name: "OpenAI",
            Slug: "openai",
            SortOrder: null,
            LogoKey: null
        );

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.Conflict, error.Type);
        Assert.Equal("LlmProvider.SlugAlreadyExists", error.Code);
        Assert.Empty(providers.AddedProviders);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorsWithoutSaving()
    {
        FakeLlmProviderRepository providers = new();
        FakeUnitOfWork unitOfWork = new();
        CreateLlmProviderHandler handler = new(providers, unitOfWork);
        CreateLlmProviderCommand command = new
        (
            Name: "",
            Slug: "",
            SortOrder: 0,
            LogoKey: null
        );

        ErrorOr<LlmProviderResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, x => x.Code == "ProviderName.Required");
        Assert.Contains(result.Errors, x => x.Code == "ProviderSlug.Required");
        Assert.Contains(result.Errors, x => x.Code == "SortOrder.Invalid");
        Assert.Empty(providers.AddedProviders);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}