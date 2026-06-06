using Chat.Api.Endpoints;
using Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<Response>
{
    public const string RouteName = "Chat.FavoriteModels.Get";

    public override void Configure()
    {
        Get("/me/favorite-models");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Favorite Models")
                .WithDescription("Gets the authenticated user's favorite LLM models.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.FavoriteModels);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<FavoriteModelsReadModel> result = await sender.Send(new GetFavoriteModelsQuery(), ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}