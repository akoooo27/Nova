using Chat.Api.Endpoints;
using Chat.Application.SharedChats.Commands.Delete;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.SharedChats.DeleteSharedChat;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.SharedChats.Delete";

    public override void Configure()
    {
        Delete("/me/shared-chats/{shareId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Delete Shared Chat")
                .WithDescription("Revokes a single shared link owned by the authenticated user.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.SharedChats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        DeleteSharedChatCommand command = new(Route<Guid>("shareId"));

        ErrorOr<Deleted> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}