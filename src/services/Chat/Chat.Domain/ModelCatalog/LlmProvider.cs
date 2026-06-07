using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.Events;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using SharedKernel;

namespace Chat.Domain.ModelCatalog;

public sealed class LlmProvider : AggregateRoot<LlmProviderId>
{
    private readonly List<LlmModel> _models = [];

    public ProviderName Name { get; private set; }

    public ProviderSlug Slug { get; private set; }

    public bool IsFeatured { get; private set; }

    public AssetKey? LogoKey { get; private set; }

    public IReadOnlyCollection<LlmModel> Models => _models;

    private LlmProvider
    (
        LlmProviderId id,
        ProviderName name,
        ProviderSlug slug,
        bool isFeatured
    ) : base(id)
    {
        Name = name;
        Slug = slug;
        IsFeatured = isFeatured;
    }

    public static LlmProvider Create
    (
        ProviderName name,
        ProviderSlug slug,
        bool isFeatured
    ) => new
    (
        id: LlmProviderId.New(),
        name: name,
        slug: slug,
        isFeatured: isFeatured
    );

    public ErrorOr<LlmModel> AddModel
    (
        ExternalModelId externalModelId,
        LlmModelProfile profile
    )
    {
        if (_models.Any(x => x.ExternalModelId == externalModelId))
        {
            return LlmProviderErrors.ModelAlreadyExists(externalModelId);
        }

        LlmModel model = LlmModel.Create
        (
            providerId: Id,
            externalModelId: externalModelId,
            profile: profile
        );

        _models.Add(model);

        return model;
    }

    public ErrorOr<LlmModel> UpdateModelProfile(LlmModelId modelId, LlmModelProfile profile)
    {
        LlmModel? model = FindModel(modelId);

        if (model is null)
        {
            return LlmProviderErrors.ModelNotFound(modelId);
        }

        model.UpdateProfile(profile);
        AddDomainEvent(new LlmModelProfileUpdated(Id, model.Id));

        return model;
    }

    public ErrorOr<Success> EnableModel(LlmModelId modelId)
    {
        LlmModel? model = FindModel(modelId);

        if (model is null)
        {
            return LlmProviderErrors.ModelNotFound(modelId);
        }

        model.Enable();

        return Result.Success;
    }

    public ErrorOr<Success> DisableModel(LlmModelId modelId)
    {
        LlmModel? model = FindModel(modelId);

        if (model is null)
        {
            return LlmProviderErrors.ModelNotFound(modelId);
        }

        model.Disable();

        return Result.Success;
    }

    public void UpdateDetails
    (
        ProviderName name,
        ProviderSlug slug,
        AssetKey? logoKey,
        bool isFeatured
    )
    {
        if (Name == name && Slug == slug && LogoKey == logoKey && IsFeatured == isFeatured)
        {
            return;
        }

        Name = name;
        Slug = slug;
        LogoKey = logoKey;
        IsFeatured = isFeatured;

        AddDomainEvent(new LlmProviderUpdated(Id));
    }

    public ErrorOr<Success> EnsureCanBeDeleted()
    {
        return _models.Count > 0
            ? LlmProviderErrors.CannotDeleteProviderWithModels(Id)
            : Result.Success;
    }

    public void UpdateLogoKey(AssetKey logoKey) => LogoKey = logoKey;

    public void RemoveLogoKey() => LogoKey = null;

    public LlmModel? FindModel(LlmModelId modelId) =>
        _models.FirstOrDefault(model => model.Id == modelId);
}