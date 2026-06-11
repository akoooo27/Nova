using Chat.Domain.ModelCatalog.ValueObjects;

using SharedKernel;

namespace Chat.Domain.ModelCatalog.Entities;

public sealed class LlmModel : Entity<LlmModelId>
{
    public LlmProviderId ProviderId { get; private set; } = default!;

    public ExternalModelId ExternalModelId { get; private set; } = default!;

    public LlmModelProfile Profile { get; private set; } = default!;

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
        bool isEnabled
    ) : base(id)
    {
        ProviderId = providerId;
        ExternalModelId = externalModelId;
        Profile = profile;
        IsEnabled = isEnabled;
    }

    internal static LlmModel Create
    (
        LlmProviderId providerId,
        ExternalModelId externalModelId,
        LlmModelProfile profile
    ) => new
    (
        id: LlmModelId.New(),
        providerId: providerId,
        externalModelId: externalModelId,
        profile: profile,
        isEnabled: true
    );

    internal bool Enable()
    {
        if (IsEnabled)
        {
            return false;
        }

        IsEnabled = true;

        return true;
    }

    internal bool Disable()
    {
        if (!IsEnabled)
        {
            return false;
        }

        IsEnabled = false;

        return true;
    }

    internal void UpdateProfile(LlmModelProfile profile) => Profile = profile;
}