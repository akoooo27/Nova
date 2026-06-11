using Chat.Api.Endpoints.ModelCatalog.Responses;
using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmModel;
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.ModelCatalog.DisableLlmModel;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<LlmModelResponse>
{
    public const string RouteName = "Chat.ModelCatalog.LlmModels.Disable";

    public override void Configure()
    {
        Put("/model-catalog/providers/{providerId}/models/{modelId}/disable");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Disable LLM Model")
                .WithDescription("Disables an existing LLM model in the model catalog.")
                .Produces<LlmModelResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        DisableLlmModelCommand command = new
        (
            ProviderId: Route<Guid>("providerId"),
            ModelId: Route<Guid>("modelId")
        );

        ErrorOr<LlmModelResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ModelCatalogResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}