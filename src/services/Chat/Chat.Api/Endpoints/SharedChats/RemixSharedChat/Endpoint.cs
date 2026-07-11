using Chat.Application.SharedChats.Commands.Remix;
using Chat.Application.SharedChats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.SharedChats.RemixSharedChat;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<Response>
{
    public const string RouteName = "Chat.SharedChats.Remix";

    public override void Configure()
    {
        Post("/shared-chats/{shareId}/remix");
        Version(1);

        Throttle(hitLimit: 20, durationSeconds: 60);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Remix Shared Chat")
                .WithDescription("Copies a remix-enabled shared chat's path into a new independent chat owned by the authenticated caller.")
                .Produces<Response>(StatusCodes.Status201Created, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status403Forbidden, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.SharedChats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        RemixSharedChatCommand command = new(Route<Guid>("shareId"));

        ErrorOr<RemixSharedChatResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        Response response = new()
        {
            ChatId = result.Value.ChatId,
            Title = result.Value.Title,
            CreatedAt = result.Value.CreatedAt
        };

        await Send.ResultAsync(TypedResults.Created($"/v1/chats/{response.ChatId}", response));
    }
}