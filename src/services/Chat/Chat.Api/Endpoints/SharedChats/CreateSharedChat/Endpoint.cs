using Chat.Api.Endpoints.SharedChats.Responses;
using Chat.Api.SharedChats;
using Chat.Application.SharedChats.Commands.Create;
using Chat.Application.SharedChats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.SharedChats.CreateSharedChat;

internal sealed record Request(
    Guid ChatId,
    Guid CurrentMessageId,
    bool AllowRemix = false
);

internal sealed class Endpoint(ISender sender, SharedLinkUrlBuilder urlBuilder) : Endpoint<Request>
{
    public const string RouteName = "Chat.SharedChats.Create";

    public override void Configure()
    {
        Post("/me/shared-chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Create Shared Chat")
                .WithDescription("Creates an anonymous shared link for the selected chat message, or returns the existing link for that node.")
                .Produces<SharedChatResponse>(StatusCodes.Status201Created, "application/json")
                .Produces<SharedChatResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.SharedChats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        CreateSharedChatCommand command = new
        (
            ChatId: request.ChatId,
            CurrentMessageId: request.CurrentMessageId,
            AllowRemix: request.AllowRemix
        );

        ErrorOr<SharedChatResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        SharedChatResponse response = SharedChatResponse.From(result.Value, urlBuilder.Build(result.Value.Id));

        if (response.AlreadyExists)
        {
            await Send.ResultAsync(TypedResults.Ok(response));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(response.ShareUrl, response));
    }
}