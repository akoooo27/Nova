using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.CreateResearchChat;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.CreateResearchChat;

internal sealed record Request(string Task, Guid LlmModelId, Guid? ProjectId = null);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.CreateResearchChat";

    public override void Configure()
    {
        Post("/chats/research");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Create Research Chat")
                .WithDescription("Creates a chat whose first turn is a deep-research agent run; progress streams on the returned stream path and the report arrives as the assistant message.")
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
        CreateResearchChatCommand command = new(request.Task, request.LlmModelId, request.ProjectId);

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