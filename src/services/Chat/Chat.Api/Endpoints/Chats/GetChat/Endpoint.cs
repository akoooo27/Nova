using Chat.Api.Endpoints;
using Chat.Application.Chats.Queries.GetChat;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class Request
{
    public Guid ChatId { get; init; }
}

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, Response>
{
    public const string RouteName = "Chat.Chats.Get";

    public override void Configure()
    {
        Get("/chats/{chatId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Chat")
                .WithDescription("Gets a chat with its full message tree for the authenticated user.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        ErrorOr<ChatDetailReadModel> result = await sender.Send(new GetChatQuery(request.ChatId), ct);

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ResponseMapper.ToResponse,
            cancellationToken: ct
        );
    }
}