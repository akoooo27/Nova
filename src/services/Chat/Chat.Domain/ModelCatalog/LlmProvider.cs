using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using SharedKernel;

namespace Chat.Domain.ModelCatalog;

public sealed class LlmProvider : AggregateRoot<LlmProviderId>
{
    private readonly List<LlmModel> _models = [];

    public ProviderName Name { get; private set; }

    public ProviderSlug Slug { get; private set; }

    public SortOrder SortOrder { get; private set; }

    public IReadOnlyCollection<LlmModel> Models => _models;

    private LlmProvider
    (
        LlmProviderId id,
        ProviderName name,
        ProviderSlug slug,
        SortOrder sortOrder
    ) : base(id)
    {
        Name = name;
        Slug = slug;
        SortOrder = sortOrder;
    }

    public static LlmProvider Create
    (
        ProviderName name,
        ProviderSlug slug,
        SortOrder sortOrder
    ) => new
    (
        id: LlmProviderId.New(),
        name: name,
        slug: slug,
        sortOrder: sortOrder
    );

    public ErrorOr<LlmModel> AddModel
    (
        ExternalModelId externalModelId,
        LlmModelProfile profile,
        SortOrder sortOrder
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
            profile: profile,
            sortOrder: sortOrder
        );

        _models.Add(model);

        return model;
    }

    public ErrorOr<Success> RefreshModelProfile(LlmModelId modelId, LlmModelProfile profile)
    {
        LlmModel? model = FindModel(modelId);

        if (model is null)
        {
            return LlmProviderErrors.ModelNotFound(modelId);
        }

        model.UpdateProfile(profile);

        return Result.Success;
    }

    public ErrorOr<Success> UpdateModelSortOrder(LlmModelId modelId, SortOrder sortOrder)
    {
        LlmModel? model = FindModel(modelId);

        if (model is null)
        {
            return LlmProviderErrors.ModelNotFound(modelId);
        }

        model.UpdateSortOrder(sortOrder);

        return Result.Success;
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

    public void UpdateSortOrder(SortOrder sortOrder) => SortOrder = sortOrder;

    private LlmModel? FindModel(LlmModelId modelId) =>
        _models.FirstOrDefault(model => model.Id == modelId);
}