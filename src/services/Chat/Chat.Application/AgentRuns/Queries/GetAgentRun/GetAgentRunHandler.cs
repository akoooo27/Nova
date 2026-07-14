using Chat.Application.AgentRuns.Errors;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.AgentRuns.Queries.GetAgentRun;

internal sealed class GetAgentRunHandler(IUserContext userContext, IAgentRunDetailReader reader)
    : IQueryHandler<GetAgentRunQuery, ErrorOr<AgentRunDetailResult>>
{
    public async ValueTask<ErrorOr<AgentRunDetailResult>> Handle(GetAgentRunQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(query.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(query.MessageId);

        if (userIdResult.IsError || chatIdResult.IsError || messageIdResult.IsError)
        {
            // Malformed ids cannot correspond to a run; same NotFound as absence (no leak).
            return Error.NotFound(code: "AgentRun.NotFound", description: "No agent run found.");
        }

        // Owner + chat scoping happen in the reader's SQL; a mismatch returns null,
        // indistinguishable from absence (no information leak).
        AgentRunDetailResult? detail = await reader.GetAsync
        (
            chatId: chatIdResult.Value,
            messageId: messageIdResult.Value,
            userId: userIdResult.Value,
            cancellationToken: cancellationToken
        );

        return detail is null
            ? AgentRunOperationErrors.NotFound(messageIdResult.Value)
            : detail;
    }
}