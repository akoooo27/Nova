using Chat.Application.Chats.Errors;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.Chats;

internal static class ModelUsability
{
    public static async Task<ErrorOr<Success>> EnsureUsableAsync
    (
        ILlmProviderRepository providers,
        LlmModelId modelId,
        CancellationToken cancellationToken,
        bool requiresToolCalling = false
    )
    {
        LlmProvider? provider = await providers.GetByModelIdAsync(modelId, cancellationToken);

        if (provider is null)
        {
            return ChatOperationErrors.LlmModelNotFound(modelId);
        }

        LlmModel? model = provider.FindModel(modelId);

        if (model is null)
        {
            return ChatOperationErrors.LlmModelNotFound(modelId);
        }

        if (!provider.IsEnabled || !model.IsEnabled)
        {
            return ChatOperationErrors.LlmModelDisabled(modelId);
        }

        if (requiresToolCalling && !model.Profile.Capabilities.SupportsToolCalling)
        {
            return ChatOperationErrors.LlmModelDoesNotSupportToolCalling(modelId);
        }

        return Result.Success;
    }
}