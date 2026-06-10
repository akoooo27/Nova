using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using SharedKernel;

namespace Chat.Domain.Chats;

public sealed class ChatThread : AggregateRoot<ChatId>
{
    private readonly List<ChatMessage> _messages;

    public UserId UserId { get; private set; }

    public ChatTitle Title { get; private set; }

    public ChatMessageId CurrentMessageId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ChatMessage> Messages => _messages;

    private ChatThread
    (
        ChatId id,
        UserId userId,
        ChatTitle title,
        ChatMessage root,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt
    ) : base(id)
    {
        UserId = userId;
        Title = title;
        CurrentMessageId = root.Id;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        _messages = [root];
    }

    public static ChatThread Create
    (
        UserId userId,
        ChatTitle title,
        MessageContent firstUserMessage,
        DateTimeOffset createdAt
    )
    {
        ChatId id = ChatId.New();

        ChatMessage root = ChatMessage.CreateUserMessage
        (
            chatId: id,
            parentMessageId: null,
            content: firstUserMessage,
            createdAt: createdAt,
            siblingIndex: SiblingIndex.First()
        );

        return new ChatThread
        (
            id: id,
            userId: userId,
            title: title,
            root: root,
            createdAt: createdAt,
            updatedAt: createdAt
        );
    }

    /// <summary>
    /// Adds a user message. Strict alternation: the parent must be an assistant message.
    /// Root user messages are created only by <see cref="Create"/> and (as siblings) by
    /// <see cref="EditUserMessage"/>; this method never creates roots.
    /// </summary>
    public ErrorOr<ChatMessage> AddUserMessage
    (
        ChatMessageId parentMessageId,
        MessageContent content,
        DateTimeOffset createdAt
    )
    {
        ChatMessage? parent = FindMessage(parentMessageId);

        if (parent is null)
        {
            return ChatErrors.ParentMessageNotFound(parentMessageId);
        }

        if (parent.Role != MessageRole.Assistant)
        {
            return ChatErrors.UserParentMustBeAssistant(parentMessageId);
        }

        ChatMessage message = ChatMessage.CreateUserMessage
        (
            chatId: Id,
            parentMessageId: parentMessageId,
            content: content,
            createdAt: createdAt,
            siblingIndex: GetNextSiblingIndex(parentMessageId)
        );

        _messages.Add(message);

        SetHead(message.Id, createdAt);

        return message;
    }

    public ErrorOr<ChatMessage> BeginAssistantMessage
    (
        ChatMessageId parentMessageId,
        LlmModelId? llmModelId,
        DateTimeOffset createdAt
    )
    {
        ChatMessage? parent = FindMessage(parentMessageId);

        if (parent is null)
        {
            return ChatErrors.ParentMessageNotFound(parentMessageId);
        }

        if (parent.Role != MessageRole.User)
        {
            return ChatErrors.AssistantParentMustBeUser(parentMessageId);
        }

        ChatMessage message = ChatMessage.CreateAssistantMessage
        (
            chatId: Id,
            parentMessageId: parentMessageId,
            llmModelId: llmModelId,
            createdAt: createdAt,
            siblingIndex: GetNextSiblingIndex(parentMessageId)
        );

        _messages.Add(message);

        SetHead(message.Id, createdAt);

        return message;
    }

    public ErrorOr<ChatMessage> CompleteAssistantMessage
    (
        ChatMessageId messageId,
        MessageContent content,
        DateTimeOffset completedAt
    )
    {
        ChatMessage? message = FindMessage(messageId);

        if (message is null)
        {
            return ChatErrors.MessageNotFound(messageId);
        }

        ErrorOr<Success> completionResult = message.CompleteAssistantMessage(content, completedAt);

        if (completionResult.IsError)
        {
            return completionResult.Errors;
        }

        UpdatedAt = completedAt;

        return message;
    }

    public ErrorOr<ChatMessage> FailAssistantMessage
    (
        ChatMessageId messageId,
        FailureReason reason,
        DateTimeOffset failedAt
    )
    {
        ChatMessage? message = FindMessage(messageId);

        if (message is null)
            return ChatErrors.MessageNotFound(messageId);

        ErrorOr<Success> result = message.Fail(reason, failedAt);

        if (result.IsError)
            return result.Errors;

        UpdatedAt = failedAt;

        return message;
    }

    /// <summary>
    /// Creates an edited sibling of a user message under the same parent (a new branch),
    /// leaving the original untouched. Editing a root user message creates another root sibling.
    /// </summary>
    public ErrorOr<ChatMessage> EditUserMessage
    (
        ChatMessageId messageId,
        MessageContent content,
        DateTimeOffset createdAt
    )
    {
        ChatMessage? target = FindMessage(messageId);

        if (target is null)
        {
            return ChatErrors.MessageNotFound(messageId);
        }

        if (target.Role != MessageRole.User)
        {
            return ChatErrors.EditTargetMustBeUser(messageId);
        }

        ChatMessage sibling = ChatMessage.CreateUserMessage
        (
            chatId: Id,
            parentMessageId: target.ParentMessageId,
            content: content,
            createdAt: createdAt,
            siblingIndex: GetNextSiblingIndex(target.ParentMessageId)
        );

        _messages.Add(sibling);

        SetHead(sibling.Id, createdAt);

        return sibling;
    }

    /// <summary>
    /// Creates a new assistant sibling under the same parent as a terminal assistant message.
    /// The target must be <see cref="MessageStatus.Completed"/> or <see cref="MessageStatus.Failed"/>;
    /// a still-generating target is rejected to avoid racing two generations under one parent.
    /// </summary>
    public ErrorOr<ChatMessage> RegenerateAssistant
    (
        ChatMessageId messageId,
        LlmModelId llmModelId,
        DateTimeOffset createdAt
    )
    {
        ChatMessage? target = FindMessage(messageId);

        if (target is null)
        {
            return ChatErrors.MessageNotFound(messageId);
        }

        if (target.Role != MessageRole.Assistant || target.ParentMessageId is null)
        {
            return ChatErrors.RegenerationTargetMustBeAssistant(messageId);
        }

        if (target.Status == MessageStatus.Generating)
        {
            return ChatErrors.CannotRegenerateWhileGenerating(messageId);
        }

        ChatMessage sibling = ChatMessage.CreateAssistantMessage
        (
            chatId: Id,
            parentMessageId: target.ParentMessageId,
            llmModelId: llmModelId,
            createdAt: createdAt,
            siblingIndex: GetNextSiblingIndex(target.ParentMessageId)
        );

        _messages.Add(sibling);

        SetHead(sibling.Id, createdAt);

        return sibling;
    }

    /// <summary>
    /// Moves the active branch head to an existing message. Intentionally status-agnostic:
    /// the head legitimately rests on a <see cref="MessageStatus.Generating"/> assistant during a
    /// live turn, so selection is gated only on the message existing within this chat.
    /// </summary>
    public ErrorOr<Success> SelectMessage(ChatMessageId messageId, DateTimeOffset updatedAt)
    {
        ChatMessage? message = FindMessage(messageId);

        if (message is null)
            return ChatErrors.MessageNotFound(messageId);

        SetHead(messageId, updatedAt);

        return Result.Success;
    }

    public ChatMessage? FindMessage(ChatMessageId messageId) =>
        _messages.SingleOrDefault(message => message.Id == messageId);

    private void SetHead(ChatMessageId messageId, DateTimeOffset updatedAt)
    {
        CurrentMessageId = messageId;
        UpdatedAt = updatedAt;
    }

    private SiblingIndex GetNextSiblingIndex(ChatMessageId? parentMessageId)
    {
        int count = _messages.Count(m => m.ParentMessageId == parentMessageId);

        return SiblingIndex.Next(count);
    }
}