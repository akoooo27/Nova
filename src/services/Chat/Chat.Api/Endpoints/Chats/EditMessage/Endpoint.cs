using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Commands.EditMessage;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.EditMessage;

internal sealed record Request
(
    string Message,
    Guid ModelId,
    bool ForceUseSearch = false
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.EditMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages/{messageId}/edit");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Edit Message")
                .WithDescription("Creates an edited sibling of an active-path user message and starts generating a new assistant reply asynchronously.")
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
        EditMessageCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
            MessageId: Route<Guid>("messageId"),
            Message: request.Message,
            LlmModelId: request.ModelId,
            GenerationOptions: new TurnGenerationOptions(ForceUseSearch: request.ForceUseSearch)
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