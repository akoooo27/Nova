using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DeleteLlmModel;

internal sealed class DeleteLlmModelHandler(ILlmProviderRepository providers, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteLlmModelCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(DeleteLlmModelCommand command, CancellationToken cancellationToken)
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

        ErrorOr<Success> removeModelResult = provider.RemoveModel(modelIdResult.Value);

        if (removeModelResult.IsError)
        {
            return removeModelResult.Errors;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}