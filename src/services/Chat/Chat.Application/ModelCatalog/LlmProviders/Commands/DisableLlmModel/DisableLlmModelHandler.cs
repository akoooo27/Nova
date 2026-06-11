using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmModel;

internal sealed class DisableLlmModelHandler(ILlmProviderRepository providers, IUnitOfWork unitOfWork)
    : ICommandHandler<DisableLlmModelCommand, ErrorOr<LlmModelResult>>
{
    public async ValueTask<ErrorOr<LlmModelResult>> Handle
    (
        DisableLlmModelCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<LlmProviderId> providerIdResult = LlmProviderId.Create(command.ProviderId);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.ModelId);
        List<Error> errors = [];

        if (providerIdResult.IsError)
        {
            errors.AddRange(providerIdResult.Errors);
        }

        if (modelIdResult.IsError)
        {
            errors.AddRange(modelIdResult.Errors);
        }

        if (errors.Count != 0)
        {
            return errors;
        }

        LlmProvider? provider = await providers.GetByIdAsync(providerIdResult.Value, cancellationToken);

        if (provider is null)
        {
            return LlmProviderOperationErrors.ProviderNotFound(providerIdResult.Value);
        }

        ErrorOr<Success> disableResult = provider.DisableModel(modelIdResult.Value);

        if (disableResult.IsError)
        {
            return disableResult.Errors;
        }

        LlmModel model = provider.FindModel(modelIdResult.Value)!;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return model.ToResult();
    }
}