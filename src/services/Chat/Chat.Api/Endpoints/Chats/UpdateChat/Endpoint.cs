using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.UpdateChat;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.Chats.UpdateChat;

internal sealed class Request
{
    public required string Title { get; init; }

    public required bool IsPinned { get; init; }

    public required bool IsArchived { get; init; }
}

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, ChatThreadResponse>
{
    public const string RouteName = "Chat.Chats.Update";

    public override void Configure()
    {
        Patch("/chats/{chatId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Update Chat")
                .WithDescription("Replaces the editable metadata state of a chat thread.")
                .Produces<ChatThreadResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        UpdateChatCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
            Title: request.Title,
            IsPinned: request.IsPinned,
            IsArchived: request.IsArchived
        );

        ErrorOr<ChatThreadResult> result = await sender.Send(command, ct);

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ChatThreadResponse.From,
            cancellationToken: ct
        );
    }
}