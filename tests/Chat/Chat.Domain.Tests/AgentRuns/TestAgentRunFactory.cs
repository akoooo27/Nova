using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.Entities;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns;

internal static class TestAgentRunFactory
{
    public static readonly DateTimeOffset StartedAt = new(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);

    public static AgentRun StartRun(AgentRunKind kind = AgentRunKind.Research) =>
        AgentRun.Start
        (
            kind: kind,
            chatId: ChatId.New(),
            assistantMessageId: ChatMessageId.New(),
            userId: UserId.FromDatabase("auth0|user-1"),
            task: AgentTask.FromDatabase("Research the topic"),
            llmModelId: LlmModelId.New(),
            startedAt: StartedAt
        );

    public static ActivitySequence Sequence(int value) => ActivitySequence.FromDatabase(value);

    public static ActivityTitle Title(string value = "Thinking") => ActivityTitle.FromDatabase(value);

    public static ActivityType Type(string value = "reasoning") => ActivityType.FromDatabase(value);

    public static ActivityDetail Detail(string json = "{\"step\":1}") => ActivityDetail.FromDatabase(json);

    public static TokenUsage Usage(int inputTokens = 10, int outputTokens = 20) =>
        TokenUsage.FromDatabase(inputTokens, outputTokens);

    public static ErrorOr<AgentRunActivity> AppendActivity
    (
        AgentRun run,
        int sequence,
        ActivityKind kind = ActivityKind.Thought,
        string? title = null,
        DateTimeOffset? occurredAt = null
    ) =>
        run.AppendActivity
        (
            sequence: Sequence(sequence),
            kind: kind,
            type: Type(),
            title: Title(title ?? "Thinking"),
            detail: null,
            occurredAt: occurredAt ?? StartedAt.AddSeconds(sequence)
        );
}