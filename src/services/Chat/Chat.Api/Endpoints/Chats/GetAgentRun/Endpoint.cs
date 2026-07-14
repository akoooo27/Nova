using Chat.Application.AgentRuns.Queries.GetAgentRun;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.GetAgentRun;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.GetAgentRun";

    public override void Configure()
    {
        Get("/chats/{chatId}/messages/{messageId}/agent-run");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Agent Run")
                .WithDescription("Returns the agent run behind an assistant card message: summary, usage, and the full ordered activity log. Owner-only.")
                .Produces<AgentRunDetailResult>(StatusCodes.Status200OK)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        GetAgentRunQuery query = new
        (
            ChatId: Route<Guid>("chatId"),
            MessageId: Route<Guid>("messageId")
        );

        ErrorOr<AgentRunDetailResult> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
