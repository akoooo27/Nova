using Chat.Api.Endpoints;
using Chat.Application.Abstractions.Arcade;
using Chat.Application.Abstractions.Arcade.Google;

using FastEndpoints;

using Shared.Application.Authentication;

namespace Chat.Api.Endpoints.Integrations.Google.DisconnectGoogleIntegration;

internal sealed class Endpoint(IUserContext userContext, IGoogleIntegrationClient integrations)
    : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Integrations.Google.Disconnect";

    public override void Configure()
    {
        Delete("/me/integrations/google");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Disconnect Google Integration")
                .WithDescription("Revokes every Google connection the authenticated user holds for the configured Arcade provider. Idempotent.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Integrations);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await integrations.DisconnectAsync(userContext.UserId, ct);

        await Send.NoContentAsync(ct);
    }
}