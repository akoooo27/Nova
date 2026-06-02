using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

using Microsoft.EntityFrameworkCore;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.AddLlmModel;

internal sealed class AddLlmModelHandler(IApplicationDbContext db)
    : ICommandHandler<AddLlmModelCommand, ErrorOr<LlmModelResult>>
{
    public async ValueTask<ErrorOr<LlmModelResult>> Handle(AddLlmModelCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<LlmProviderId> providerIdResult = LlmProviderId.Create(command.ProviderId);
        ErrorOr<ExternalModelId> externalModelIdResult = ExternalModelId.Create(command.ExternalModelId);
        ErrorOr<ModelName> nameResult = ModelName.Create(command.Name);
        ErrorOr<ModelDescription> descriptionResult = ModelDescription.Create(command.Description);
        ErrorOr<ContextWindow> contextWindowResult = ContextWindow.Create(command.ContextWindow);
        ErrorOr<ModelCapabilities> capabilitiesResult = ModelCapabilities.Create
        (
            supportsVision: command.SupportsVision,
            supportsReasoning: command.SupportsReasoning,
            supportsToolCalling: command.SupportsToolCalling
        );
        ErrorOr<SortOrder> sortOrderResult = command.SortOrder is not null
            ? SortOrder.Create(command.SortOrder.Value)
            : SortOrder.First;

        List<Error> errors = [];

        if (providerIdResult.IsError)
        {
            errors.AddRange(providerIdResult.Errors);
        }

        if (externalModelIdResult.IsError)
        {
            errors.AddRange(externalModelIdResult.Errors);
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

        if (sortOrderResult.IsError)
        {
            errors.AddRange(sortOrderResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        LlmProvider? provider = await db.LlmProviders
            .Include(x => x.Models)
            .FirstOrDefaultAsync(x => x.Id == providerIdResult.Value, cancellationToken);

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

        ErrorOr<LlmModel> addModelResult = provider.AddModel
        (
            externalModelId: externalModelIdResult.Value,
            profile: profile,
            sortOrder: sortOrderResult.Value
        );

        if (addModelResult.IsError)
        {
            return addModelResult.Errors;
        }

        await db.SaveChangesAsync(cancellationToken);

        LlmModel model = addModelResult.Value;

        return model.ToResult();
    }
}