using Chat.Api.Endpoints;
using Chat.Application.Chats.Commands.DeleteAllChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.DeleteAllChats;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.DeleteAll";

    public override void Configure()
    {
        Delete("/me/chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Delete All Chats")
                .WithDescription("Permanently deletes every chat owned by the authenticated user, including archived chats and chats inside projects.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<Deleted> result = await sender.Send(new DeleteAllChatsCommand(), ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
