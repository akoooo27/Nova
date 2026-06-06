using Chat.Application.Abstractions.Database;
using Chat.Application.FavoriteModels.Errors;
using Chat.Application.FavoriteModels.Results;
using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.FavoriteModels.Commands.AddFavoriteModel;

internal sealed class AddFavoriteModelHandler(
    IUserContext userContext,
    IFavoriteModelRepository favoriteModels,
    ILlmProviderRepository llmProviders,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<AddFavoriteModelCommand, ErrorOr<FavoriteModelResult>>
{
    public async ValueTask<ErrorOr<FavoriteModelResult>> Handle(AddFavoriteModelCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (modelIdResult.IsError)
        {
            errors.AddRange(modelIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId userId = userIdResult.Value;
        LlmModelId modelId = modelIdResult.Value;

        FavoriteModel? favoriteModel = await favoriteModels.GetAsync
        (
            userId: userId,
            llmModelId: modelId,
            cancellationToken: cancellationToken
        );

        if (favoriteModel is not null)
        {
            return favoriteModel.ToResult();
        }

        LlmProvider? llmProvider = await llmProviders.GetByModelIdAsync(modelId, cancellationToken);

        if (llmProvider is null)
        {
            return FavoriteModelOperationErrors.LlmModelNotFound(modelId);
        }

        LlmModel? llmModel = llmProvider.FindModel(modelId);

        if (llmModel is null)
        {
            return FavoriteModelOperationErrors.LlmModelNotFound(modelId);
        }

        if (!llmModel.IsEnabled)
        {
            return FavoriteModelOperationErrors.LlmModelDisabled(modelId);
        }

        FavoriteModel newFavoriteModel = FavoriteModel.Create
        (
            userId: userId,
            llmModelId: modelId,
            createdAt: dateTimeProvider.UtcNow
        );

        favoriteModels.Add(newFavoriteModel);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return newFavoriteModel.ToResult();
    }
}