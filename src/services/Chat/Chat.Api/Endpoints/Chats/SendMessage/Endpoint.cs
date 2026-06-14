using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Commands.SendMessage;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.SendMessage;

internal sealed record Request
(
    string Message,
    Guid ModelId,
    bool ForceUseSearch = false
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.SendMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Send Message")
                .WithDescription("Appends a user message to the active branch and starts generating the assistant reply asynchronously.")
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
        SendMessageCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
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