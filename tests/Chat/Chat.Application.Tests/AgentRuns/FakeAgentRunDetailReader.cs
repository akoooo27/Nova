using Chat.Application.AgentRuns.Queries.GetAgentRun;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.AgentRuns;

/// <summary>
/// Mirrors the Dapper reader's SQL scoping (chat + assistant message + owner) so the handler's
/// owner-isolation behaviour stays under test without touching infrastructure.
/// </summary>
internal sealed class FakeAgentRunDetailReader : IAgentRunDetailReader
{
    private readonly List<(Guid ChatId, Guid MessageId, string UserId, AgentRunDetailResult Result)> _entries = [];

    public void Seed(Guid chatId, Guid messageId, string userId, AgentRunDetailResult result) =>
        _entries.Add((chatId, messageId, userId, result));

    public Task<AgentRunDetailResult?> GetAsync
    (
        ChatId chatId,
        ChatMessageId messageId,
        UserId userId,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(_entries
            .Where(entry => entry.ChatId == chatId.Value
                && entry.MessageId == messageId.Value
                && entry.UserId == userId.Value)
            .Select(entry => entry.Result)
            .FirstOrDefault());
}