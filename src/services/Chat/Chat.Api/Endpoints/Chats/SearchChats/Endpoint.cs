using Chat.Api.Endpoints;
using Chat.Application.Chats;
using Chat.Application.Chats.Queries.SearchChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed record Request
(
    [property: QueryParam] string? Query,
    [property: QueryParam] bool IsArchived,
    [property: QueryParam] int? Limit,
    [property: QueryParam] int? Offset
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request, Response>
{
    public const string RouteName = "Chat.Chats.Search";

    public override void Configure()
    {
        Get("/me/chats/search");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Search Chats")
                .WithDescription("Searches the authenticated user's chat history and returns chat-level results with matching snippets.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        SearchChatsQuery query = new
        (
            Query: request.Query ?? string.Empty,
            IsArchived: request.IsArchived,
            Limit: request.Limit ?? ChatLimits.DefaultQueryLimit,
            Offset: request.Offset ?? ChatLimits.DefaultQueryOffset
        );

        ErrorOr<ChatSearchReadModel> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}