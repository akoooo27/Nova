using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.EnableLlmProvider;

internal sealed class EnableLlmProviderHandler(ILlmProviderRepository providers, IUnitOfWork unitOfWork)
    : ICommandHandler<EnableLlmProviderCommand, ErrorOr<LlmProviderResult>>
{
    public async ValueTask<ErrorOr<LlmProviderResult>> Handle
    (
        EnableLlmProviderCommand command,
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

        provider.Enable();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return provider.ToResult();
    }
}