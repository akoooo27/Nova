using Chat.Domain.ModelCatalog.ValueObjects;

using SharedKernel;

namespace Chat.Domain.ModelCatalog.Entities;

public sealed class LlmModel : Entity<LlmModelId>
{
    public LlmProviderId ProviderId { get; private set; } = default!;

    public ExternalModelId ExternalModelId { get; private set; } = default!;

    public LlmModelProfile Profile { get; private set; } = default!;

    public SortOrder SortOrder { get; private set; } = default!;

    public bool IsEnabled { get; private set; }

    private LlmModel()
    {
        // EF Core materialization only
    }

    private LlmModel
    (
        LlmModelId id,
        LlmProviderId providerId,
        ExternalModelId externalModelId,
        LlmModelProfile profile,
        SortOrder sortOrder,
        bool isEnabled
    ) : base(id)
    {
        ProviderId = providerId;
        ExternalModelId = externalModelId;
        Profile = profile;
        SortOrder = sortOrder;
        IsEnabled = isEnabled;
    }

    internal static LlmModel Create
    (
        LlmProviderId providerId,
        ExternalModelId externalModelId,
        LlmModelProfile profile,
        SortOrder sortOrder
    ) => new
    (
        id: LlmModelId.New(),
        providerId: providerId,
        externalModelId: externalModelId,
        profile: profile,
        sortOrder: sortOrder,
        isEnabled: true
    );

    internal void Enable() => IsEnabled = true;

    internal void Disable() => IsEnabled = false;

    internal void UpdateProfile(LlmModelProfile profile) => Profile = profile;

    internal void UpdateSortOrder(SortOrder sortOrder) => SortOrder = sortOrder;
}