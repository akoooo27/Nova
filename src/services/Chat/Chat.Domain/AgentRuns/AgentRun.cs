using Chat.Domain.AgentRuns.Entities;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using SharedKernel;

namespace Chat.Domain.AgentRuns;

public sealed class AgentRun : AggregateRoot<AgentRunId>
{
    private readonly List<AgentRunActivity> _activities = [];

    public ChatId ChatId { get; private set; } = default!;

    public ChatMessageId AssistantMessageId { get; private set; } = default!;

    public UserId UserId { get; private set; } = default!;

    public AgentRunKind Kind { get; private set; }

    public AgentTask Task { get; private set; } = default!;

    public LlmModelId LlmModelId { get; private set; } = default!;

    public TokenUsage Usage { get; private set; } = default!;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public IReadOnlyCollection<AgentRunActivity> Activities => _activities;

    public ActivityTitle? CurrentPhase =>
        _activities
            .Where(activity => activity.Kind == ActivityKind.Phase)
            .MaxBy(activity => activity.Sequence.Value)?
            .Title;

    private AgentRun()
    {
        // EF Core materialization only
    }

    private AgentRun
    (
        AgentRunId id,
        ChatId chatId,
        ChatMessageId assistantMessageId,
        UserId userId,
        AgentRunKind kind,
        AgentTask task,
        LlmModelId llmModelId,
        DateTimeOffset startedAt
    ) : base(id)
    {
        ChatId = chatId;
        AssistantMessageId = assistantMessageId;
        UserId = userId;
        Kind = kind;
        Task = task;
        LlmModelId = llmModelId;
        Usage = TokenUsage.Zero;
        StartedAt = startedAt;
    }

    public static AgentRun Start
    (
        AgentRunKind kind,
        ChatId chatId,
        ChatMessageId assistantMessageId,
        UserId userId,
        AgentTask task,
        LlmModelId llmModelId,
        DateTimeOffset startedAt
    ) => new
    (
        id: AgentRunId.New(),
        chatId: chatId,
        assistantMessageId: assistantMessageId,
        userId: userId,
        kind: kind,
        task: task,
        llmModelId: llmModelId,
        startedAt: startedAt
    );

    public ErrorOr<AgentRunActivity> AppendActivity
    (
        ActivitySequence sequence,
        ActivityKind kind,
        ActivityType type,
        ActivityTitle title,
        ActivityDetail? detail,
        DateTimeOffset occurredAt
    )
    {
        if (FinishedAt is not null)
        {
            return AgentRunErrors.AlreadyFinished(Id);
        }

        int highestSequence = _activities.Count == 0
            ? 0
            : _activities.Max(activity => activity.Sequence.ToInt());

        if (sequence.ToInt() <= highestSequence)
        {
            return AgentRunErrors.StaleActivitySequence(Id, sequence);
        }

        AgentRunActivity activity = AgentRunActivity.Create
        (
            runId: Id,
            sequence: sequence,
            kind: kind,
            type: type,
            title: title,
            detail: detail,
            occurredAt: occurredAt
        );

        _activities.Add(activity);

        return activity;
    }

    public ErrorOr<Success> RecordUsage(TokenUsage delta)
    {
        if (FinishedAt is not null)
        {
            return AgentRunErrors.AlreadyFinished(Id);
        }

        Usage = Usage.Add(delta);

        return Result.Success;
    }

    public ErrorOr<Success> Finish(DateTimeOffset finishedAt)
    {
        if (FinishedAt is not null)
        {
            return AgentRunErrors.AlreadyFinished(Id);
        }

        if (finishedAt < StartedAt)
        {
            return AgentRunErrors.FinishedBeforeStarted(Id);
        }

        FinishedAt = finishedAt;

        return Result.Success;
    }
}