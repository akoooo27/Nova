using Chat.Domain.FavoriteModels.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using SharedKernel;

namespace Chat.Domain.FavoriteModels;

public sealed class FavoriteModel : AggregateRoot<FavoriteModelId>
{
    public UserId UserId { get; private set; }

    public LlmModelId LlmModelId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private FavoriteModel
    (
        FavoriteModelId id,
        UserId userId,
        LlmModelId llmModelId,
        DateTimeOffset createdAt
    ) : base(id)
    {
        UserId = userId;
        LlmModelId = llmModelId;
        CreatedAt = createdAt;
    }

    public static FavoriteModel Create
    (
        UserId userId,
        LlmModelId llmModelId,
        DateTimeOffset createdAt
    ) => new
    (
        id: FavoriteModelId.New(),
        userId: userId,
        llmModelId: llmModelId,
        createdAt: createdAt
    );
}