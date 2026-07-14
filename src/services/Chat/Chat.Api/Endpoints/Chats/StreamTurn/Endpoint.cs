using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using FastEndpoints;

using Shared.Application.Authentication;

namespace Chat.Api.Endpoints.Chats.StreamTurn;

internal sealed class Endpoint(IUserContext userContext, IChatRepository chats, ITurnStreamReader streamReader)
    : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.StreamTurn";

    public override void Configure()
    {
        Get("/chats/{chatId}/turns/{turnId}/stream");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Stream Turn Events")
                .WithDescription("Streams turn events (tokens, agent activities, usage, done/failed/stopped) for an assistant message as Server-Sent Events. Supports resume via the Last-Event-ID header.")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(Route<Guid>("chatId"));
        ErrorOr<ChatMessageId> turnIdResult = ChatMessageId.Create(Route<Guid>("turnId"));

        if (userIdResult.IsError || chatIdResult.IsError || turnIdResult.IsError)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        UserId userId = userIdResult.Value;
        ChatId chatId = chatIdResult.Value;
        ChatMessageId turnId = turnIdResult.Value;

        ChatThread? thread = await chats.GetByIdAsync
        (
            id: chatId,
            userId: userId,
            cancellationToken: ct
        );

        if (thread is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        ChatMessage? message = thread.FindMessage(turnId);

        if (message is null || message.Role != MessageRole.Assistant)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        HttpContext.Response.ContentType = "text/event-stream";
        HttpContext.Response.Headers.CacheControl = "no-cache";

        // Terminal turn: the stream may already have expired, so emit the terminal event directly.
        // The client refetches content through the read endpoints.
        if (message.Status != MessageStatus.Generating)
        {
            TurnEvent terminal = message.Status switch
            {
                MessageStatus.Completed => new DoneEvent(message.Id.Value),
                MessageStatus.Stopped => new StoppedEvent(message.Id.Value),
                _ => new FailedEvent(message.Id.Value, message.FailureReason?.Value ?? "The turn failed.")
            };

            await WriteEventAsync("terminal", terminal, ct);
            return;
        }

        string? lastEventId = HttpContext.Request.Headers["Last-Event-ID"].FirstOrDefault();

        try
        {
            await foreach (TurnStreamEntry entry in streamReader.ReadAsync(message.Id.Value, lastEventId, ct))
            {
                await WriteEventAsync
                (
                    entryId: entry.EntryId,
                    turnEvent: entry.Event,
                    ct
                );
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — nothing to clean up; the worker owns the turn.
        }
    }

    private async Task WriteEventAsync
    (
        string entryId,
        TurnEvent turnEvent,
        CancellationToken ct
    )
    {
        string payload =
            $"id: {entryId}\n" +
            $"event: {TurnEventSerializer.EventName(turnEvent)}\n" +
            $"data: {TurnEventSerializer.Serialize(turnEvent)}\n\n";

        await HttpContext.Response.WriteAsync(payload, ct);
        await HttpContext.Response.Body.FlushAsync(ct);
    }
}