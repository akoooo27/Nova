using Chat.Api.Endpoints;
using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Queries.GetManagedModelCatalog;

using FastEndpoints;

using Mediator;

namespace Chat.Api.Endpoints.ModelCatalog.GetManagedModelCatalog;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<Response>
{
    public const string RouteName = "Chat.ModelCatalog.GetManaged";

    public override void Configure()
    {
        Get("/model-catalog/managed");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Managed Model Catalog")
                .WithDescription("Gets the full model catalog, including disabled models, for catalog managers.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ManagedModelCatalogReadModel catalog = await sender.Send(new GetManagedModelCatalogQuery(), ct);

        await Send.ResponseAsync(ResponseMapper.ToResponse(catalog), cancellation: ct);
    }
}