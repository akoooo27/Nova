using Chat.Api.Security;
using Chat.Application.Abstractions.ProviderLogos.Results;
using Chat.Application.ModelCatalog.ProviderLogos.Commands.RequestProviderLogoUploadUrl;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.ModelCatalog.RequestProviderLogoUploadUrl;

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, Response>
{
    public const string RouteName = "Chat.ModelCatalog.ProviderLogos.RequestUploadUrl";

    public override void Configure()
    {
        Post("/model-catalog/providers/{providerId}/logo-upload-url");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Request Provider Logo Upload URL")
                .WithDescription("Creates a presigned S3 PUT URL for uploading an LLM provider logo.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        RequestProviderLogoUploadUrlCommand command = new
        (
            ProviderId: Route<Guid>("providerId"),
            ContentType: req.ContentType
        );

        ErrorOr<ProviderLogoUploadUrl> result = await sender.Send(command, ct);

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ToResponse,
            cancellationToken: ct
        );
    }

    private static Response ToResponse(ProviderLogoUploadUrl uploadUrl) => new()
    {
        UploadUrl = uploadUrl.UploadUrl,
        LogoKey = uploadUrl.LogoKey,
        ExpiresAt = uploadUrl.ExpiresAt,
        Headers = uploadUrl.Headers
    };
}