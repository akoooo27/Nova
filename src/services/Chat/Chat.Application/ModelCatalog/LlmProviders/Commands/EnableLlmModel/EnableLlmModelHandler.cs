using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.EnableLlmModel;

internal sealed class EnableLlmModelHandler(ILlmProviderRepository providers, IUnitOfWork unitOfWork)
    : ICommandHandler<EnableLlmModelCommand, ErrorOr<LlmModelResult>>
{
    public async ValueTask<ErrorOr<LlmModelResult>> Handle
    (
        EnableLlmModelCommand command,
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

        ErrorOr<Success> enableResult = provider.EnableModel(modelIdResult.Value);

        if (enableResult.IsError)
        {
            return enableResult.Errors;
        }

        LlmModel model = provider.FindModel(modelIdResult.Value)!;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return model.ToResult();
    }
}