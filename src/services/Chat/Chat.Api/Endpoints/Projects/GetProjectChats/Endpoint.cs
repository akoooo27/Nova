using Chat.Api.Endpoints;
using Chat.Application.Projects;
using Chat.Application.Projects.Queries.GetProjectChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Projects.GetProjectChats;

internal sealed record Request
(
    Guid ProjectId,
    [property: QueryParam] int? Limit,
    [property: QueryParam] int? Offset
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request, Response>
{
    public const string RouteName = "Chat.Projects.Chats.List";

    public override void Configure()
    {
        Get("/projects/{projectId}/chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("List Project Chats")
                .WithDescription("Lists the chats in a project owned by the authenticated user, pinned first and then by most recent activity.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Projects);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        GetProjectChatsQuery query = new
        (
            ProjectId: request.ProjectId,
            Limit: request.Limit ?? ProjectLimits.DefaultQueryLimit,
            Offset: request.Offset ?? ProjectLimits.DefaultQueryOffset
        );

        ErrorOr<ProjectChatListReadModel> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}