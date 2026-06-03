using Chat.Application.Abstractions.ModelCatalog;
using Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;

namespace Chat.Application.Tests.ModelCatalog.LlmProviders.Queries;

public sealed class GetPublicModelCatalogHandlerTests
{
    [Fact]
    public async Task HandleReturnsPublicModelCatalogFromReader()
    {
        PublicModelCatalogReadModel catalog = CreateCatalog();
        FakePublicModelCatalogReader reader = new(catalog);
        GetPublicModelCatalogHandler handler = new(reader);

        PublicModelCatalogReadModel result = await handler.Handle(new GetPublicModelCatalogQuery(), CancellationToken.None);

        Assert.Same(catalog, result);
        Assert.Equal(1, reader.GetCallCount);
    }

    [Fact]
    public async Task HandlePassesCancellationTokenToReader()
    {
        PublicModelCatalogReadModel catalog = CreateCatalog();
        FakePublicModelCatalogReader reader = new(catalog);
        GetPublicModelCatalogHandler handler = new(reader);
        using CancellationTokenSource cancellationTokenSource = new();

        _ = await handler.Handle(new GetPublicModelCatalogQuery(), cancellationTokenSource.Token);

        Assert.Equal(cancellationTokenSource.Token, reader.CancellationToken);
    }

    private static PublicModelCatalogReadModel CreateCatalog()
    {
        Guid providerId = Guid.CreateVersion7();

        PublicLlmModelReadModel model = new
        (
            Id: Guid.CreateVersion7(),
            ProviderId: providerId,
            ExternalModelId: "gpt-4.1",
            Name: "GPT-4.1",
            Description: "General purpose model",
            ContextWindow: 128000,
            SupportsVision: true,
            SupportsReasoning: false,
            SupportsToolCalling: true,
            SortOrder: 1
        );

        PublicLlmProviderReadModel provider = new
        (
            Id: providerId,
            Name: "OpenAI",
            Slug: "openai",
            SortOrder: 1,
            LogoKey: "llm-providers/openai.svg",
            Models: [model]
        );

        return new PublicModelCatalogReadModel([provider]);
    }

    private sealed class FakePublicModelCatalogReader(PublicModelCatalogReadModel catalog) : IPublicModelCatalogReader
    {
        public int GetCallCount { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Task<PublicModelCatalogReadModel> GetAsync(CancellationToken cancellationToken)
        {
            GetCallCount++;
            CancellationToken = cancellationToken;

            return Task.FromResult(catalog);
        }
    }
}