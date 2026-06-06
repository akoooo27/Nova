using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmModelProfile;

internal sealed class UpdateLlmModelProfileHandler(ILlmProviderRepository providers, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateLlmModelProfileCommand, ErrorOr<LlmModelResult>>
{
    public async ValueTask<ErrorOr<LlmModelResult>> Handle(UpdateLlmModelProfileCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<LlmProviderId> providerIdResult = LlmProviderId.Create(command.ProviderId);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.ModelId);
        ErrorOr<ModelName> nameResult = ModelName.Create(command.Name);
        ErrorOr<ModelDescription> descriptionResult = ModelDescription.Create(command.Description);
        ErrorOr<ContextWindow> contextWindowResult = ContextWindow.Create(command.ContextWindow);
        ErrorOr<ModelCapabilities> capabilitiesResult = ModelCapabilities.Create
        (
            supportsVision: command.SupportsVision,
            supportsReasoning: command.SupportsReasoning,
            supportsToolCalling: command.SupportsToolCalling
        );

        List<Error> errors = [];

        if (providerIdResult.IsError)
        {
            errors.AddRange(providerIdResult.Errors);
        }

        if (modelIdResult.IsError)
        {
            errors.AddRange(modelIdResult.Errors);
        }

        if (nameResult.IsError)
        {
            errors.AddRange(nameResult.Errors);
        }

        if (descriptionResult.IsError)
        {
            errors.AddRange(descriptionResult.Errors);
        }

        if (contextWindowResult.IsError)
        {
            errors.AddRange(contextWindowResult.Errors);
        }

        if (capabilitiesResult.IsError)
        {
            errors.AddRange(capabilitiesResult.Errors);
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

        LlmModelProfile profile = LlmModelProfile.Create
        (
            name: nameResult.Value,
            description: descriptionResult.Value,
            contextWindow: contextWindowResult.Value,
            capabilities: capabilitiesResult.Value
        );

        ErrorOr<LlmModel> modelUpdateResult = provider.UpdateModelProfile(modelIdResult.Value, profile);

        if (modelUpdateResult.IsError)
        {
            return modelUpdateResult.Errors;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        LlmModel model = modelUpdateResult.Value;

        return model.ToResult();
    }
}