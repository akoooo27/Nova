using Chat.Api.Endpoints;
using Chat.Application.Chats.Queries.GetChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.GetChats;

internal sealed record Request
(
    [property: QueryParam] int? Limit,
    [property: QueryParam] int? Offset
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request, Response>
{
    public const string RouteName = "Chat.Chats.List";

    private const int DefaultLimit = 20;
    private const int DefaultOffset = 0;

    public override void Configure()
    {
        Get("/me/chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("List Chats")
                .WithDescription("Lists the authenticated user's chats, pinned first and then by most recent activity.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        GetChatsQuery query = new
        (
            Limit: request.Limit ?? DefaultLimit,
            Offset: request.Offset ?? DefaultOffset
        );

        ErrorOr<ChatListReadModel> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}