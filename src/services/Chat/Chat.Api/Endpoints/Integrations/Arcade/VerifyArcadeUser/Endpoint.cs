using Chat.Api.Endpoints;
using Chat.Application.Abstractions.Arcade;

using FastEndpoints;

using Shared.Application.Authentication;

namespace Chat.Api.Endpoints.Integrations.Arcade.VerifyArcadeUser;

internal sealed record Request
(
    [property: QueryParam, BindFrom("flow_id")] string FlowId
);

internal sealed class Endpoint(IUserContext userContext, IArcadeAuthClient arcade) : Endpoint<Request>
{
    public const string RouteName = "Chat.Integrations.Arcade.VerifyUser";

    public override void Configure()
    {
        Get("/me/integrations/arcade/verify");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Verify Arcade User")
                .WithDescription("Custom Arcade verifier route. Confirms the signed-in user owns the in-flight authorization, then redirects the browser back to Arcade to complete the OAuth flow.")
                .Produces(StatusCodes.Status302Found)
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Integrations);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        ArcadeUserConfirmation confirmation = await arcade.ConfirmUserAsync
        (
            flowId: request.FlowId,
            userId: userContext.UserId,
            cancellationToken: ct
        );

        await Send.RedirectAsync
        (
            location: confirmation.NextUri?.ToString() ?? "/",
            isPermanent: false,
            allowRemoteRedirects: true
        );
    }
}