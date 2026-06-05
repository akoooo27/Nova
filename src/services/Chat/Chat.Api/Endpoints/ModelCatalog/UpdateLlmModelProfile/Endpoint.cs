using Chat.Api.Endpoints.ModelCatalog.Responses;
using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmModelProfile;
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.ModelCatalog.UpdateLlmModelProfile;

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, LlmModelResponse>
{
    public const string RouteName = "Chat.ModelCatalog.LlmModels.UpdateProfile";

    public override void Configure()
    {
        Put("/model-catalog/providers/{providerId}/models/{modelId}/profile");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Update LLM Model Profile")
                .WithDescription("Replaces the editable profile of an existing LLM model.")
                .Produces<LlmModelResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        UpdateLlmModelProfileCommand command = new
        (
            ProviderId: Route<Guid>("providerId"),
            ModelId: Route<Guid>("modelId"),
            Name: req.Name,
            Description: req.Description,
            ContextWindow: req.ContextWindow,
            SupportsVision: req.SupportsVision,
            SupportsReasoning: req.SupportsReasoning,
            SupportsToolCalling: req.SupportsToolCalling
        );

        ErrorOr<LlmModelResult> result = await sender.Send(command, ct);

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ModelCatalogResponseMapper.ToResponse,
            cancellationToken: ct
        );
    }
}