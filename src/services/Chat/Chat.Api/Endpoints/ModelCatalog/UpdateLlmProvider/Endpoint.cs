using Chat.Api.Endpoints.ModelCatalog.Responses;
using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmProvider;
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.ModelCatalog.UpdateLlmProvider;

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, LlmProviderResponse>
{
    public const string RouteName = "Chat.ModelCatalog.LlmProviders.Update";

    public override void Configure()
    {
        Put("/model-catalog/providers/{providerId}");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Update LLM Provider")
                .WithDescription("Replaces the editable profile of an LLM provider in the model catalog.")
                .Produces<LlmProviderResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        UpdateLlmProviderCommand command = new
        (
            ProviderId: Route<Guid>("providerId"),
            Name: req.Name,
            Slug: req.Slug,
            LogoKey: req.LogoKey,
            IsFeatured: req.IsFeatured
        );

        ErrorOr<LlmProviderResult> result = await sender.Send(command, ct);

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ModelCatalogResponseMapper.ToResponse,
            cancellationToken: ct
        );
    }
}