using Chat.Api.Endpoints;
using Chat.Application.Abstractions.Arcade;
using Chat.Application.Abstractions.Arcade.Google;

using FastEndpoints;

using Shared.Application.Authentication;

namespace Chat.Api.Endpoints.Integrations.Google.GetGoogleIntegration;

internal sealed class Endpoint(IUserContext userContext, IGoogleIntegrationClient integrations)
    : EndpointWithoutRequest<Response>
{
    public const string RouteName = "Chat.Integrations.Google.Get";

    public override void Configure()
    {
        Get("/me/integrations/google");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Google Integration")
                .WithDescription("Returns whether the authenticated user has connected their Google account for Arcade tools, the connected account, and granted scopes.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Integrations);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        GoogleIntegrationStatus status = await integrations.GetStatusAsync(userContext.UserId, ct);

        await Send.ResponseAsync
        (
            new Response(status.Connected, status.AccountEmail, status.Scopes),
            cancellation: ct
        );
    }
}