using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

using SharedKernel;

namespace Chat.Domain.Chats.Entities;

public sealed class ChatMessage : Entity<ChatMessageId>
{
    public ChatId ChatId { get; private set; } = default!;

    public ChatMessageId? ParentMessageId { get; private set; }

    public MessageRole Role { get; private set; }

    public MessageContent? Content { get; private set; }

    public LlmModelId? LlmModelId { get; private set; }

    public MessageStatus Status { get; private set; }

    public FailureReason? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public SiblingIndex SiblingIndex { get; private set; } = default!;

    private ChatMessage()
    {
        // EF Core materialization only
    }

    private ChatMessage
    (
        ChatMessageId id,
        ChatId chatId,
        ChatMessageId? parentMessageId,
        MessageRole role,
        MessageContent? content,
        LlmModelId? llmModelId,
        MessageStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt,
        SiblingIndex siblingIndex
    ) : base(id)
    {
        ChatId = chatId;
        ParentMessageId = parentMessageId;
        Role = role;
        Content = content;
        LlmModelId = llmModelId;
        Status = status;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
        SiblingIndex = siblingIndex;
    }

    internal static ChatMessage CreateUserMessage
    (
        ChatId chatId,
        ChatMessageId? parentMessageId,
        MessageContent content,
        DateTimeOffset createdAt,
        SiblingIndex siblingIndex
    ) => new
    (
        id: ChatMessageId.New(),
        chatId: chatId,
        parentMessageId: parentMessageId,
        role: MessageRole.User,
        content: content,
        llmModelId: null,
        status: MessageStatus.Completed,
        createdAt: createdAt,
        completedAt: createdAt,
        siblingIndex: siblingIndex
    );

    internal static ChatMessage CreateAssistantMessage
    (
        ChatId chatId,
        ChatMessageId parentMessageId,
        LlmModelId llmModelId,
        DateTimeOffset createdAt,
        SiblingIndex siblingIndex
    ) => new
    (
        id: ChatMessageId.New(),
        chatId: chatId,
        parentMessageId: parentMessageId,
        role: MessageRole.Assistant,
        content: null,
        llmModelId: llmModelId,
        status: MessageStatus.Generating,
        createdAt: createdAt,
        completedAt: null,
        siblingIndex: siblingIndex
    );

    internal ErrorOr<Success> CompleteAssistantMessage(MessageContent content, DateTimeOffset completedAt)
    {
        if (Status != MessageStatus.Generating)
        {
            return ChatErrors.CannotCompleteNonGenerating(Id);
        }

        Content = content;
        Status = MessageStatus.Completed;
        CompletedAt = completedAt;

        return Result.Success;
    }

    internal ErrorOr<Success> Fail(FailureReason reason, DateTimeOffset failedAt)
    {
        if (Status != MessageStatus.Generating)
        {
            return ChatErrors.CannotFailNonGenerating(Id);
        }

        Status = MessageStatus.Failed;
        FailureReason = reason;
        CompletedAt = failedAt;

        return Result.Success;
    }

    internal ChatMessage CopyForBranch
    (
        ChatMessageId id,
        ChatId chatId,
        ChatMessageId? parentMessageId
    )
    {
        ChatMessage copy = new
        (
            id: id,
            chatId: chatId,
            parentMessageId: parentMessageId,
            role: Role,
            content: Content,
            llmModelId: LlmModelId,
            status: Status,
            createdAt: CreatedAt,
            completedAt: CompletedAt,
            siblingIndex: SiblingIndex.First()
        );

        copy.FailureReason = FailureReason;

        return copy;
    }

    internal ErrorOr<Success> Stop(MessageContent? content, DateTimeOffset stoppedAt)
    {
        if (Status != MessageStatus.Generating)
        {
            return ChatErrors.CannotStopNonGenerating(Id);
        }

        Content = content;
        Status = MessageStatus.Stopped;
        CompletedAt = stoppedAt;

        return Result.Success;
    }
}