using Chat.Api.Endpoints;
using Chat.Api.SharedChats;
using Chat.Application.SharedChats;
using Chat.Application.SharedChats.Queries.GetSharedChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.SharedChats.ListSharedChats;

internal sealed record Request
(
    [property: QueryParam] int? Limit,
    [property: QueryParam] int? Offset
);

internal sealed class Endpoint(ISender sender, SharedLinkUrlBuilder urlBuilder) : Endpoint<Request, Response>
{
    public const string RouteName = "Chat.SharedChats.List";

    public override void Configure()
    {
        Get("/me/shared-chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("List Shared Chats")
                .WithDescription("Lists shared links owned by the authenticated user, newest first.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.SharedChats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        GetSharedChatsQuery query = new
        (
            Limit: request.Limit ?? SharedChatLimits.DefaultLimit,
            Offset: request.Offset ?? SharedChatLimits.DefaultOffset
        );

        ErrorOr<SharedChatListReadModel> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value, urlBuilder), cancellation: ct);
    }
}