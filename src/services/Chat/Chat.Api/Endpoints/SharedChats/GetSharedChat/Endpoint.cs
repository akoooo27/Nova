using Chat.Api.Endpoints;
using Chat.Application.SharedChats.Queries.GetPublicSharedChat;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.SharedChats.GetSharedChat;

internal sealed class Request
{
    public Guid ShareId { get; init; }
}

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, Response>
{
    public const string RouteName = "Chat.SharedChats.Get";

    public override void Configure()
    {
        Get("/shared-chats/{shareId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Shared Chat")
                .WithDescription("Gets a shared chat for an authenticated user who possesses its link.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.SharedChats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        HttpContext.Response.Headers.CacheControl = "no-store";
        HttpContext.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";

        ErrorOr<PublicSharedChatReadModel> result = await sender.Send
        (
            new GetPublicSharedChatQuery(request.ShareId),
            ct
        );

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ResponseMapper.ToResponse,
            cancellationToken: ct
        );
    }
}