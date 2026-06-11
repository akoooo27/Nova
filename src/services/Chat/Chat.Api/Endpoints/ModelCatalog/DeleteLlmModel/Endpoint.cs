using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Commands.DeleteLlmModel;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.ModelCatalog.DeleteLlmModel;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.ModelCatalog.LlmModels.Delete";

    public override void Configure()
    {
        Delete("/model-catalog/providers/{providerId}/models/{modelId}");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Delete LLM Model")
                .WithDescription("Deletes an LLM model from an existing provider in the model catalog.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        DeleteLlmModelCommand command = new
        (
            ProviderId: Route<Guid>("providerId"),
            ModelId: Route<Guid>("modelId")
        );

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}