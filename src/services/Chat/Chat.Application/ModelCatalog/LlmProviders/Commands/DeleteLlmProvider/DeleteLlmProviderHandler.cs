using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.DeleteLlmProvider;

internal sealed class DeleteLlmProviderHandler(ILlmProviderRepository providers, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteLlmProviderCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(DeleteLlmProviderCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<LlmProviderId> providerIdResult = LlmProviderId.Create(command.Id);

        if (providerIdResult.IsError)
        {
            return providerIdResult.Errors;
        }

        LlmProviderId providerId = providerIdResult.Value;

        LlmProvider? provider = await providers.GetByIdAsync(providerId, cancellationToken);

        if (provider is null)
        {
            return LlmProviderOperationErrors.ProviderNotFound(providerId);
        }

        ErrorOr<Success> deleteResult = provider.EnsureCanBeDeleted();

        if (deleteResult.IsError)
        {
            return deleteResult.Errors;
        }

        providers.Remove(provider);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}