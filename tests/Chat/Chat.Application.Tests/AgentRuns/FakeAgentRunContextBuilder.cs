using Chat.Application.Abstractions.AgentRuns;
using Chat.Domain.AgentRuns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

using ErrorOr;

namespace Chat.Application.Tests.AgentRuns;

internal sealed class FakeAgentRunContextBuilder : IAgentRunContextBuilder
{
    public Task<ErrorOr<AgentRunContext>> BuildAsync
    (
        ChatThread thread,
        ChatMessage assistantMessage,
        AgentRun run,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult<ErrorOr<AgentRunContext>>(new AgentRunContext
        (
            RunId: run.Id.Value,
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            Kind: run.Kind,
            Task: run.Task.Value,
            ExternalModelId: "gpt-4.1",
            PriorConversation: []
        ));
}