using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using SharedKernel;

namespace Chat.Domain.Chats;

public sealed class ChatThread : AggregateRoot<ChatId>
{
    private readonly List<ChatMessage> _messages = [];

    public UserId UserId { get; private set; } = default!;

    public ChatTitle Title { get; private set; } = default!;

    public ChatMessageId CurrentMessageId { get; private set; } = default!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? PinnedAt { get; private set; }

    public bool IsTemporary { get; private set; }

    public bool IsArchived { get; private set; }

    public bool IsPinned => PinnedAt is not null;

    public ChatBranchOrigin? BranchOrigin { get; private set; }

    public ProjectId? ProjectId { get; private set; }

    public IReadOnlyCollection<ChatMessage> Messages => _messages;

    private ChatThread()
    {
        // EF Core materialization only
    }

    private ChatThread
    (
        ChatId id,
        UserId userId,
        ChatTitle title,
        ChatMessage root,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool isTemporary,
        ProjectId? projectId = null
    ) : base(id)
    {
        UserId = userId;
        Title = title;
        CurrentMessageId = root.Id;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        IsTemporary = isTemporary;
        ProjectId = projectId;
        _messages = [root];
    }

    public static ChatThread Create
    (
        UserId userId,
        ChatTitle title,
        MessageContent firstUserMessage,
        DateTimeOffset createdAt,
        bool isTemporary = false,
        ProjectId? projectId = null
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
            updatedAt: createdAt,
            isTemporary: isTemporary,
            projectId: projectId
        );
    }

    public static ErrorOr<ChatThread> BranchFrom
    (
        ChatThread source,
        ChatMessageId branchPointId,
        DateTimeOffset createdAt
    )
    {
        if (source.IsTemporary)
        {
            return ChatErrors.CannotBranchTemporaryChat(source.Id);
        }

        ChatMessage? branchPoint = source.FindMessage(branchPointId);

        if (branchPoint is null)
        {
            return ChatErrors.MessageNotFound(branchPointId);
        }

        if (branchPoint.Role != MessageRole.Assistant)
        {
            return ChatErrors.BranchPointMustBeAssistant(branchPointId);
        }

        if (branchPoint.Status == MessageStatus.Generating)
        {
            return ChatErrors.CannotBranchWhileGenerating(branchPointId);
        }

        List<ChatMessage> sourcePath = [];
        HashSet<ChatMessageId> visited = [];
        ChatMessage cursor = branchPoint;

        while (true)
        {
            if (!visited.Add(cursor.Id))
            {
                return ChatErrors.InvalidBranchPath(branchPointId);
            }

            sourcePath.Add(cursor);

            if (cursor.ParentMessageId is null)
            {
                break;
            }

            ChatMessage? parent = source.FindMessage(cursor.ParentMessageId);

            if (parent is null)
            {
                return ChatErrors.InvalidBranchPath(branchPointId);
            }

            cursor = parent;
        }

        ChatMessage root = sourcePath[^1];

        if (root.Role != MessageRole.User || root.ParentMessageId is not null)
        {
            return ChatErrors.InvalidBranchPath(branchPointId);
        }

        sourcePath.Reverse();

        ChatId branchId = ChatId.New();
        Dictionary<ChatMessageId, ChatMessageId> copiedIds = sourcePath.ToDictionary
        (
            message => message.Id,
            _ => ChatMessageId.New()
        );

        List<ChatMessage> copiedMessages = sourcePath
            .Select(message => message.CopyForBranch
            (
                id: copiedIds[message.Id],
                chatId: branchId,
                parentMessageId: message.ParentMessageId is null
                    ? null
                    : copiedIds[message.ParentMessageId]
            ))
            .ToList();

        ChatThread branch = new
        (
            id: branchId,
            userId: source.UserId,
            title: ChatTitle.CreateBranch(source.Title),
            root: copiedMessages[0],
            createdAt: createdAt,
            updatedAt: createdAt,
            isTemporary: source.IsTemporary
        );

        branch.BranchOrigin = ChatBranchOrigin.Create
        (
            sourceChatId: source.Id,
            sourceMessageId: branchPointId
        );
        branch._messages.AddRange(copiedMessages.Skip(1));

        branch.SetHead(copiedIds[branchPointId], createdAt);

        return branch;
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

        if (parent.Status == MessageStatus.Generating)
        {
            return ChatErrors.ParentStillGenerating(parentMessageId);
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
        LlmModelId llmModelId,
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

    public ErrorOr<ChatMessage> StopAssistantMessage
    (
        ChatMessageId messageId,
        MessageContent? content,
        DateTimeOffset stoppedAt
    )
    {
        ChatMessage? message = FindMessage(messageId);

        if (message is null)
        {
            return ChatErrors.MessageNotFound(messageId);
        }

        if (message.Role != MessageRole.Assistant)
        {
            return ChatErrors.StopTargetMustBeAssistant(messageId);
        }

        ErrorOr<Success> result = message.Stop(content, stoppedAt);

        if (result.IsError)
        {
            return result.Errors;
        }

        UpdatedAt = stoppedAt;

        return message;
    }

    /// <summary>
    /// Creates an edited sibling of an active-path user message under the same parent (a new branch),
    /// leaving the original untouched. Editing a root user message creates another root sibling.
    /// Editing is rejected while an assistant on the active path is still generating.
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

        List<ChatMessage> activePath = GetActivePath();

        if (activePath.All(x => x.Id != messageId))
        {
            return ChatErrors.EditTargetNotOnActivePath(messageId);
        }

        ChatMessage? generatingAssistant = activePath
            .FirstOrDefault(x => x is { Role: MessageRole.Assistant, Status: MessageStatus.Generating });

        if (generatingAssistant is not null)
        {
            return ChatErrors.CannotEditWhileGenerating(generatingAssistant.Id);
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

    /// <summary>
    /// Validates that the selected message and its ancestor path can be shared.
    /// The selected message may be historical and does not need to be the chat's current head.
    /// This operation does not modify the chat.
    /// </summary>
    public ErrorOr<Success> ValidateShareAt(ChatMessageId messageId)
    {
        if (IsTemporary)
            return ChatErrors.CannotShareTemporaryChat(Id);

        ChatMessage? cursor = FindMessage(messageId);

        if (cursor is null)
            return ChatErrors.MessageNotFound(messageId);

        if (cursor.Status == MessageStatus.Generating)
            return ChatErrors.CannotShareGeneratingMessage(messageId);

        HashSet<ChatMessageId> visited = [];

        while (cursor.ParentMessageId is not null)
        {
            if (!visited.Add(cursor.Id))
                return ChatErrors.InvalidSharePath(messageId);

            cursor = FindMessage(cursor.ParentMessageId);

            if (cursor is null)
                return ChatErrors.InvalidSharePath(messageId);
        }

        if (!visited.Add(cursor.Id) || cursor.Role != MessageRole.User || cursor.Status != MessageStatus.Completed)
            return ChatErrors.InvalidSharePath(messageId);

        return Result.Success;
    }

    public void Pin(DateTimeOffset pinnedAt) =>
        PinnedAt ??= pinnedAt;

    public void Unpin() =>
        PinnedAt = null;

    public void Archive() =>
        IsArchived = true;

    public void Unarchive() =>
        IsArchived = false;

    public void Rename(ChatTitle title) =>
        Title = title;

    public ErrorOr<Success> MoveToProject(ProjectId projectId, DateTimeOffset updatedAt)
    {
        if (IsTemporary)
            return ChatErrors.CannotAddTemporaryChatToProject(Id);

        ProjectId = projectId;
        UpdatedAt = updatedAt;
        return Result.Success;
    }

    public void RemoveFromProject(DateTimeOffset updatedAt)
    {
        ProjectId = null;
        UpdatedAt = updatedAt;
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

    private List<ChatMessage> GetActivePath()
    {
        List<ChatMessage> activePath = [];

        ChatMessage? cursor = FindMessage(CurrentMessageId);

        while (cursor is not null)
        {
            activePath.Add(cursor);

            if (cursor.ParentMessageId is null)
            {
                break;
            }

            cursor = FindMessage(cursor.ParentMessageId);
        }

        return activePath;
    }
}