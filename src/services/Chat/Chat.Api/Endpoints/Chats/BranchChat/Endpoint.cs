using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Commands.BranchChat;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.BranchChat;

internal sealed record Request
(
    string Message,
    Guid ModelId,
    bool ForceUseSearch = false
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.Branch";

    public override void Configure()
    {
        Post("/chats/{sourceChatId}/messages/{sourceMessageId}/branches");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Branch Chat")
                .WithDescription("Snapshots the source path up to the selected assistant message into a new independent chat, appends the first user message, and starts generating the assistant reply asynchronously.")
                .Produces<TurnStartedResponse>(StatusCodes.Status201Created)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        BranchChatCommand command = new
        (
            SourceChatId: Route<Guid>("sourceChatId"),
            SourceMessageId: Route<Guid>("sourceMessageId"),
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

        TurnStartedResponse response = TurnStartedResponse.From(result.Value);

        await Send.ResultAsync(TypedResults.Created($"/v1/chats/{response.ChatId}", response));
    }
}