using Chat.Api.Endpoints;
using Chat.Application.ModelCatalog.LlmProviders.Queries.GetPublicModelCatalog;

using FastEndpoints;

using Mediator;

namespace Chat.Api.Endpoints.ModelCatalog.GetModelCatalog;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<Response>
{
    public const string RouteName = "Chat.ModelCatalog.Get";

    public override void Configure()
    {
        Get("/model-catalog");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Model Catalog")
                .WithDescription("Gets the model catalog available to chat clients.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        PublicModelCatalogReadModel catalog = await sender.Send(new GetPublicModelCatalogQuery(), ct);

        await Send.ResponseAsync(ResponseMapper.ToResponse(catalog), cancellation: ct);
    }
}