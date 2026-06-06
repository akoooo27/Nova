using Chat.Api.Endpoints;
using Chat.Application.FavoriteModels.Commands.AddFavoriteModel;
using Chat.Application.FavoriteModels.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.FavoriteModels.AddFavoriteModel;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.FavoriteModels.Add";

    public override void Configure()
    {
        Put("/me/favorite-models/{modelId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Add Favorite Model")
                .WithDescription("Adds an enabled LLM model to the authenticated user's favorites.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.FavoriteModels);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        AddFavoriteModelCommand command = new(Route<Guid>("modelId"));

        ErrorOr<FavoriteModelResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}