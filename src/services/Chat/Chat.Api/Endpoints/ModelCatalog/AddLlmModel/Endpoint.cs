using Chat.Api.Endpoints;
using Chat.Api.Endpoints.ModelCatalog.Responses;
using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Commands.AddLlmModel;
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.ModelCatalog.AddLlmModel;

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, LlmModelResponse>
{
    public const string RouteName = "Chat.ModelCatalog.LlmModels.Add";

    public override void Configure()
    {
        Post("/model-catalog/providers/{providerId}/models");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Add LLM Model")
                .WithDescription("Adds an LLM model entry to an existing provider in the model catalog.")
                .Produces<LlmModelResponse>(StatusCodes.Status201Created, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        AddLlmModelCommand command = new
        (
            ProviderId: Route<Guid>("providerId"),
            ExternalModelId: req.ExternalModelId,
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
            successStatusCode: StatusCodes.Status201Created,
            cancellationToken: ct
        );
    }
}