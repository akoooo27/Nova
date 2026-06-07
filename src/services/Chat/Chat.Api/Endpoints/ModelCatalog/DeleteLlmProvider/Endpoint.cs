using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Commands.DeleteLlmProvider;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.ModelCatalog.DeleteLlmProvider;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.ModelCatalog.LlmProviders.Delete";

    public override void Configure()
    {
        Delete("/model-catalog/providers/{providerId}");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Delete LLM Provider")
                .WithDescription("Deletes an LLM provider from the model catalog when it has no models.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        DeleteLlmProviderCommand command = new(Route<Guid>("providerId"));

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}