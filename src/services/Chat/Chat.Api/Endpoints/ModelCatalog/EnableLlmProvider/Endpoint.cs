using Chat.Api.Endpoints.ModelCatalog.Responses;
using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Commands.EnableLlmProvider;
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.ModelCatalog.EnableLlmProvider;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<LlmProviderResponse>
{
    public const string RouteName = "Chat.ModelCatalog.LlmProviders.Enable";

    public override void Configure()
    {
        Put("/model-catalog/providers/{providerId}/enable");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Enable LLM Provider")
                .WithDescription("Re-enables an existing LLM provider in the model catalog.")
                .Produces<LlmProviderResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        EnableLlmProviderCommand command = new(Route<Guid>("providerId"));

        ErrorOr<LlmProviderResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ModelCatalogResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}