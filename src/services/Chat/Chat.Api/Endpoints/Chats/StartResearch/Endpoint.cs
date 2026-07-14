using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.StartResearch;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.StartResearch;

internal sealed record Request(string Task, Guid LlmModelId);

internal sealed class Endpoint(ISender sender) : Endpoint<Request>
{
    public const string RouteName = "Chat.Chats.StartResearch";

    public override void Configure()
    {
        Post("/chats/{chatId}/research");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Start Research")
                .WithDescription("Starts a deep-research agent run on the active branch of an existing chat. Rejected on temporary chats and while a turn is still generating.")
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
        StartResearchCommand command = new(Route<Guid>("chatId"), request.Task, request.LlmModelId);

        ErrorOr<TurnStartedResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResultAsync(TypedResults.Accepted((string?)null, TurnStartedResponse.From(result.Value)));
    }
}