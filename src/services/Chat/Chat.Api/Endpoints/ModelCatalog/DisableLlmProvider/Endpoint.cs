using Chat.Api.Endpoints.ModelCatalog.Responses;
using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Commands.DisableLlmProvider;
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.ModelCatalog.DisableLlmProvider;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<LlmProviderResponse>
{
    public const string RouteName = "Chat.ModelCatalog.LlmProviders.Disable";

    public override void Configure()
    {
        Put("/model-catalog/providers/{providerId}/disable");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Disable LLM Provider")
                .WithDescription("Disables an existing LLM provider in the model catalog.")
                .Produces<LlmProviderResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        DisableLlmProviderCommand command = new(Route<Guid>("providerId"));

        ErrorOr<LlmProviderResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ModelCatalogResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}