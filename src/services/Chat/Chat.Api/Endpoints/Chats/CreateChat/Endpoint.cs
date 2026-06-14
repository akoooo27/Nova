using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Abstractions.Turns;
using Chat.Application.Chats.Commands.CreateChat;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.CreateChat;

internal sealed record Request
(
    string Message,
    Guid ModelId,
    bool ForceUseSearch = false,
    [property: QueryParam, BindFrom("temporary-chat")]
    bool IsTemporary = false
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.Create";

    public override void Configure()
    {
        Post("/chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Create Chat")
                .WithDescription("Creates a chat with the first user message and starts generating the assistant reply asynchronously.")
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
        CreateChatCommand command = new
        (
            Message: request.Message,
            LlmModelId: request.ModelId,
            GenerationOptions: new TurnGenerationOptions(ForceUseSearch: request.ForceUseSearch),
            IsTemporary: request.IsTemporary
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