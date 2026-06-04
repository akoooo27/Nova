using Chat.Api.Endpoints.ModelCatalog.Responses;
using Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmModel;
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.ModelCatalog.UpdateLlmModel;

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, LlmModelResponse>
{
    public const string RouteName = "Chat.ModelCatalog.LlmModels.Update";

    public override void Configure()
    {
        Patch("/model-catalog/providers/{providerId}/models/{modelId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Update LLM Model")
                .WithDescription("Updates an LLM model profile for an existing provider in the model catalog.")
                .Produces<LlmModelResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        UpdateLlmModelCommand command = new
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