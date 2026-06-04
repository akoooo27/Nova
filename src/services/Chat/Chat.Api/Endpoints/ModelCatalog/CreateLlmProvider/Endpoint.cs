using Chat.Api.Endpoints;
using Chat.Api.Endpoints.ModelCatalog.Responses;
using Chat.Application.ModelCatalog.LlmProviders.Commands.CreateLlmProvider;
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.ModelCatalog.CreateLlmProvider;

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, LlmProviderResponse>
{
    public const string RouteName = "Chat.ModelCatalog.LlmProviders.Create";

    public override void Configure()
    {
        Post("/model-catalog/providers");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Create LLM Provider")
                .WithDescription("Creates a provider entry for the model catalog.")
                .Produces<LlmProviderResponse>(StatusCodes.Status201Created, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        CreateLlmProviderCommand command = new
        (
            Name: req.Name,
            Slug: req.Slug,
            SortOrder: req.SortOrder,
            LogoKey: req.LogoKey
        );

        ErrorOr<LlmProviderResult> result = await sender.Send(command, ct);

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ModelCatalogResponseMapper.ToResponse,
            successStatusCode: StatusCodes.Status201Created,
            cancellationToken: ct
        );
    }
}