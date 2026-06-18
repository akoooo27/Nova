using Chat.Api.Security;
using Chat.Application.Abstractions.ProviderLogos.Results;
using Chat.Application.ModelCatalog.ProviderLogos.Queries.ListProviderLogos;

using FastEndpoints;

using Mediator;

namespace Chat.Api.Endpoints.ModelCatalog.ListProviderLogos;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<Response>
{
    public const string RouteName = "Chat.ModelCatalog.ProviderLogos.List";

    public override void Configure()
    {
        Get("/model-catalog/provider-logos");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("List Provider Logos")
                .WithDescription("Lists provider logo objects stored under the configured S3 provider-logo prefix.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        IReadOnlyCollection<ProviderLogoObject> logos = await sender.Send(new ListProviderLogosQuery(), ct);

        await Send.ResponseAsync
        (
            new Response
            {
                Logos = logos.Select(ToResponse).ToArray()
            },
            cancellation: ct
        );
    }

    private static ProviderLogoResponse ToResponse(ProviderLogoObject logo) => new()
    {
        Key = logo.Key,
        FileName = logo.FileName,
        ContentType = logo.ContentType,
        Size = logo.Size,
        LastModified = logo.LastModified
    };
}