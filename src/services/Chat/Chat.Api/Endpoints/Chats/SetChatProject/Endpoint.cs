using Chat.Application.Chats.Commands.SetChatProject;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.SetChatProject;

internal sealed class Request
{
    /// <summary>
    /// Target project for the chat. A value moves the chat into that project; <c>null</c> moves it out
    /// of any project. This endpoint's only job is to set the association, so an omitted field is
    /// treated as <c>null</c> (move out).
    /// </summary>
    public Guid? ProjectId { get; init; }
}

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.SetProject";

    public override void Configure()
    {
        Patch("/chats/{chatId}/project");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Set Chat Project")
                .WithDescription("Moves a chat into a project (non-null projectId) or out of any project (null).")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        SetChatProjectCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
            ProjectId: request.ProjectId
        );

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}