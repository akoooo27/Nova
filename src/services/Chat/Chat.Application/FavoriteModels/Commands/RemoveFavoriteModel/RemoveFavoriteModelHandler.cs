using Chat.Application.Abstractions.Database;
using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.FavoriteModels.Commands.RemoveFavoriteModel;

internal sealed class RemoveFavoriteModelHandler(
    IUserContext userContext,
    IFavoriteModelRepository favoriteModels,
    IUnitOfWork unitOfWork) : ICommandHandler<RemoveFavoriteModelCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(RemoveFavoriteModelCommand command, CancellationToken cancellationToken)
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

        if (favoriteModel is null)
        {
            return Result.Success;
        }

        favoriteModels.Remove(favoriteModel);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}