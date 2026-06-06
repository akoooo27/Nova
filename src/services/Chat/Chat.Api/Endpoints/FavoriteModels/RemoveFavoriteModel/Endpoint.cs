using Chat.Api.Endpoints;
using Chat.Application.FavoriteModels.Commands.RemoveFavoriteModel;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.FavoriteModels.RemoveFavoriteModel;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.FavoriteModels.Remove";

    public override void Configure()
    {
        Delete("/me/favorite-models/{modelId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Remove Favorite Model")
                .WithDescription("Removes an LLM model from the authenticated user's favorites.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.FavoriteModels);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        RemoveFavoriteModelCommand command = new(Route<Guid>("modelId"));

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}