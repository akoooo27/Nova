using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.RegenerateMessage;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.RegenerateMessage;

internal sealed record Request
(
    Guid? ModelId = null,
    bool ForceUseSearch = false
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.RegenerateMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages/{messageId}/regenerate");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Regenerate Message")
                .WithDescription("Regenerates an assistant message as a new sibling under the same user message, optionally with a different model, and starts generating the reply asynchronously.")
                .Produces<TurnStartedResponse>(StatusCodes.Status202Accepted)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        RegenerateMessageCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
            MessageId: Route<Guid>("messageId"),
            ModelId: request.ModelId,
            ForceUseSearch: request.ForceUseSearch
        );

        ErrorOr<TurnStartedResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResultAsync(TypedResults.Accepted((string?)null, TurnStartedResponse.From(result.Value)));
    }
}