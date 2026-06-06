using Chat.Application.FavoriteModels.Queries;
using Chat.Application.FavoriteModels.Queries.GetFavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.FavoriteModels.Queries;

public sealed class GetFavoriteModelsHandlerTests
{
    [Fact]
    public async Task HandleReadsFavoriteModelsForCurrentUser()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        FavoriteModelsReadModel readModel = new
        (
            Models:
            [
                new FavoriteLlmModelReadModel
                (
                    Id: Guid.CreateVersion7(),
                    ExternalModelId: "gpt-4.1",
                    Name: "GPT-4.1",
                    Description: "General purpose model",
                    ContextWindow: 128000,
                    SupportsVision: true,
                    SupportsReasoning: false,
                    SupportsToolCalling: true,
                    IsEnabled: false,
                    Provider: new FavoriteModelProviderReadModel
                    (
                        Id: Guid.CreateVersion7(),
                        Name: "OpenAI",
                        Slug: "openai",
                        LogoKey: "llm-providers/openai.svg"
                    )
                )
            ]
        );
        FakeFavoriteModelsReader reader = new(readModel);
        GetFavoriteModelsHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<FavoriteModelsReadModel> result = await handler.Handle
        (
            new GetFavoriteModelsQuery(),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal(1, reader.GetCallCount);
    }
}