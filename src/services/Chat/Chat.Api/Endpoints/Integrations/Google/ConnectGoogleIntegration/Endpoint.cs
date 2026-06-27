using Chat.Api.Endpoints;
using Chat.Application.Abstractions.Arcade;
using Chat.Application.Abstractions.Arcade.Google;

using FastEndpoints;

using Shared.Application.Authentication;

namespace Chat.Api.Endpoints.Integrations.Google.ConnectGoogleIntegration;

internal sealed class Endpoint(IUserContext userContext, IGoogleIntegrationClient integrations)
    : EndpointWithoutRequest<Response>
{
    public const string RouteName = "Chat.Integrations.Google.Connect";

    public override void Configure()
    {
        Post("/me/integrations/google/connect");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Connect Google Integration")
                .WithDescription("Begins (or continues) the Google authorization flow for the authenticated user. Returns the consent URL the browser should open, or reports the account is already connected.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Integrations);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        GoogleConnectResult result = await integrations.StartConnectAsync(userContext.UserId, ct);

        await Send.ResponseAsync
        (
            new Response(result.Connected, result.AuthorizationUrl),
            cancellation: ct
        );
    }
}