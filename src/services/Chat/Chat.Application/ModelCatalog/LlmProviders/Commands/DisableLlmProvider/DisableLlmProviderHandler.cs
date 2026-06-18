using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmProvider;

internal sealed class DisableLlmProviderHandler(ILlmProviderRepository providers, IUnitOfWork unitOfWork)
    : ICommandHandler<DisableLlmProviderCommand, ErrorOr<LlmProviderResult>>
{
    public async ValueTask<ErrorOr<LlmProviderResult>> Handle
    (
        DisableLlmProviderCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<LlmProviderId> providerIdResult = LlmProviderId.Create(command.ProviderId);

        if (providerIdResult.IsError)
        {
            return providerIdResult.Errors;
        }

        LlmProvider? provider = await providers.GetByIdAsync(providerIdResult.Value, cancellationToken);

        if (provider is null)
        {
            return LlmProviderOperationErrors.ProviderNotFound(providerIdResult.Value);
        }

        provider.Disable();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return provider.ToResult();
    }
}