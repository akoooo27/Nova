using Chat.Api.Endpoints;
using Chat.Application.SharedChats.Commands.DeleteAll;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.SharedChats.DeleteAllSharedChats;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.SharedChats.DeleteAll";

    public override void Configure()
    {
        Delete("/me/shared-chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Delete All Shared Chats")
                .WithDescription("Revokes every shared link owned by the authenticated user.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.SharedChats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<Deleted> result = await sender.Send(new DeleteAllSharedChatsCommand(), ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}