using Chat.Application.Chats.Commands.StopGeneration;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.StopGeneration;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.StopGeneration";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages/{assistantMessageId}/stop");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Stop Generation")
                .WithDescription("Requests that a generating assistant message stop and keep any partial content already produced.")
                .Produces(StatusCodes.Status202Accepted)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        StopGenerationCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
            AssistantMessageId: Route<Guid>("assistantMessageId")
        );

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResultAsync(TypedResults.StatusCode(StatusCodes.Status202Accepted));
    }
}