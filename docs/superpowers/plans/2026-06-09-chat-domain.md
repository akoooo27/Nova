# Chat Domain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the `ChatThread` aggregate (a ChatGPT-style branching message tree) as a self-contained domain model in the Chat service, with explicit invariants and a repository boundary.

**Scope:** **Domain layer only.** Application (commands/queries/results), infrastructure (EF mappings, Dapper readers), and the streaming pipeline (Redis Streams, SSE, generation queue/worker, stale-generation sweeper, LLM provider client) live in separate plans and are deliberately excluded here. The only outward-facing artifacts in this plan are the `IChatRepository` interface and prose "EF persistence boundary notes" — no EF code.

**Tech Stack:** .NET 10, ErrorOr, `SharedKernel` (`Entity<TId>`, `AggregateRoot<TId>`), `DomainException`. xUnit for the (deferred) domain tests.

---

## Ground Rules

- The aggregate C# type is **`ChatThread`** (a bare `Chat` type collides with the `Chat.*` root namespaces). The DB table is `chats`; messages map to `chat_messages`.
- A tree node **is** a `ChatMessage`; each message has a nullable `ParentMessageId`. There is no separate "node" type.
- Assistant message content is **null while `Generating`** and set on `Complete`. User messages always carry content and are created `Completed`.
- `ChatThread` is the only aggregate root. `ChatMessage` is created and mutated **only** through `ChatThread` methods (its factories and transition methods are `internal`).
- **No test code is authored in this pass** — the "Domain tests" task lists the cases to write later but does not implement them.
- Any `dotnet` command requires elevated permission first.

## Design Decisions (locked)

These three decisions are baked into the methods below:

1. **Strict alternation.** A user message is either a root (`ParentMessageId == null`) or the child of an **assistant** message. Assistant messages are always children of a **user** message. ⇒ every root-to-leaf path strictly alternates user/assistant. Enforced by a guard in `AddUserMessage` (which never creates roots — `Create` does).
2. **`SelectMessage` is status-agnostic.** The branch head legitimately sits on a `Generating` assistant during a live turn (`BeginAssistantMessage` moves it there), so selection is **not** gated on status. The only rule is "the message exists in this chat." This is intentional and documented on the method.
3. **`RegenerateAssistant` requires a terminal target.** The target must be a `Completed` or `Failed` assistant; regenerating a `Generating` assistant is rejected to avoid racing two generations under one parent.

**Out of domain scope (intentional omission):** whether a user may add a message under a still-`Generating` assistant is a *concurrency policy* (queue vs. reject vs. branch) and is left to the application layer. The domain stays structurally permissive there — `AddUserMessage` does not inspect the parent's status.

## Working Order

1. Value objects.
2. `ChatErrors`.
3. `ChatMessage` entity.
4. `ChatThread` aggregate.
5. `IChatRepository`.
6. Invariants reference (prose).
7. EF persistence boundary notes (prose).
8. Domain tests (deferred — case list only).

Each task compiles before the next. Commit after each task.

---

## Task 1: Value Objects

**Files**
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatId.cs`
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatMessageId.cs`
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatTitle.cs`
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/MessageContent.cs`
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/MessageRole.cs`
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/MessageStatus.cs`

- [ ] **Step 1: Create `ChatId`**

```csharp
using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatId
{
    public Guid Value { get; }

    private ChatId(Guid value)
    {
        Value = value;
    }

    public static ChatId New() => new(Guid.CreateVersion7());

    public static ErrorOr<ChatId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "ChatId.Empty",
                description: "Chat id cannot be empty."
            );
        }

        return new ChatId(value);
    }

    public static ChatId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty chat id.");

        return new ChatId(value);
    }

    public override string ToString() => Value.ToString();
}
```

- [ ] **Step 2: Create `ChatMessageId`**

```csharp
using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatMessageId
{
    public Guid Value { get; }

    private ChatMessageId(Guid value)
    {
        Value = value;
    }

    public static ChatMessageId New() => new(Guid.CreateVersion7());

    public static ErrorOr<ChatMessageId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "ChatMessageId.Empty",
                description: "Chat message id cannot be empty."
            );
        }

        return new ChatMessageId(value);
    }

    public static ChatMessageId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty chat message id.");

        return new ChatMessageId(value);
    }

    public override string ToString() => Value.ToString();
}
```

- [ ] **Step 3: Create `ChatTitle`**

```csharp
using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatTitle
{
    public const int MaxLength = 200;

    public string Value { get; }

    private ChatTitle(string value)
    {
        Value = value;
    }

    public static ErrorOr<ChatTitle> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ChatTitle.Required",
                description: "Chat title is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ChatTitle.TooLong",
                description: $"Chat title cannot exceed {MaxLength} characters."
            );
        }

        return new ChatTitle(trimmed);
    }

    public static ChatTitle FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid chat title.");

        return new ChatTitle(value);
    }

    public override string ToString() => Value;
}
```

- [ ] **Step 4: Create `MessageContent`**

```csharp
using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record MessageContent
{
    public const int MaxLength = 32768;

    public string Value { get; }

    private MessageContent(string value)
    {
        Value = value;
    }

    public static ErrorOr<MessageContent> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "MessageContent.Required",
                description: "Message content is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "MessageContent.TooLong",
                description: $"Message content cannot exceed {MaxLength} characters."
            );
        }

        return new MessageContent(trimmed);
    }

    public static MessageContent FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained invalid message content.");

        return new MessageContent(value);
    }

    public override string ToString() => Value;
}
```

- [ ] **Step 5: Create `MessageRole`**

```csharp
namespace Chat.Domain.Chats.ValueObjects;

public enum MessageRole
{
    User = 1,
    Assistant = 2
}
```

- [ ] **Step 6: Create `MessageStatus`**

```csharp
namespace Chat.Domain.Chats.ValueObjects;

public enum MessageStatus
{
    Generating = 1,
    Completed = 2,
    Failed = 3
}
```

- [ ] **Step 7: Build**

Run: `dotnet build src/services/Chat/Chat.Domain/Chat.Domain.csproj`
Expected: build succeeds. (Request elevated permission first.)

- [ ] **Step 8: Commit**

```bash
git add src/services/Chat/Chat.Domain/Chats/ValueObjects
git commit -m "feat(chat): add chat tree domain value objects"
```

---

## Task 2: ChatErrors

**Files**
- Create: `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs`

Includes the two errors required by the locked decisions: `UserParentMustBeAssistant` (strict alternation) and `CannotRegenerateWhileGenerating` (terminal regenerate target).

- [ ] **Step 1: Create `ChatErrors`**

```csharp
using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Chats;

public static class ChatErrors
{
    public static Error MessageNotFound(ChatMessageId messageId) =>
        Error.NotFound
        (
            code: "Chat.MessageNotFound",
            description: $"No message found with id '{messageId.Value}'."
        );

    public static Error ParentMessageNotFound(ChatMessageId messageId) =>
        Error.NotFound
        (
            code: "Chat.ParentMessageNotFound",
            description: $"No parent message found with id '{messageId.Value}'."
        );

    public static Error AssistantParentMustBeUser(ChatMessageId parentMessageId) =>
        Error.Conflict
        (
            code: "Chat.AssistantParentMustBeUser",
            description: $"Assistant messages must reply to a user message; '{parentMessageId.Value}' is not a user message."
        );

    public static Error UserParentMustBeAssistant(ChatMessageId parentMessageId) =>
        Error.Conflict
        (
            code: "Chat.UserParentMustBeAssistant",
            description: $"User messages may only follow an assistant message; '{parentMessageId.Value}' is not an assistant message."
        );

    public static Error EditTargetMustBeUser(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.EditTargetMustBeUser",
            description: $"Only user messages can be edited; '{messageId.Value}' is not a user message."
        );

    public static Error RegenerationTargetMustBeAssistant(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.RegenerationTargetMustBeAssistant",
            description: $"Only assistant messages can be regenerated; '{messageId.Value}' is not an assistant message."
        );

    public static Error CannotRegenerateWhileGenerating(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotRegenerateWhileGenerating",
            description: $"Message '{messageId.Value}' is still generating and cannot be regenerated yet."
        );

    public static Error CannotCompleteNonGenerating(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotCompleteNonGenerating",
            description: $"Message '{messageId.Value}' is not generating and cannot be completed."
        );

    public static Error CannotFailNonGenerating(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.CannotFailNonGenerating",
            description: $"Message '{messageId.Value}' is not generating and cannot be failed."
        );
}
```

- [ ] **Step 2: Commit** (after the aggregate compiles in Task 4; `ChatErrors` has no other dependencies and builds immediately)

```bash
git add src/services/Chat/Chat.Domain/Chats/ChatErrors.cs
git commit -m "feat(chat): add chat domain errors"
```

---

## Task 3: ChatMessage Entity

**Files**
- Create: `src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs`

`Content` is nullable: assistant messages start `Generating` with null content. `Complete` and `Fail` are the guarded state transitions — a persisted `Status` enum plus methods that enforce valid transitions and return `ErrorOr`. Factories and transitions are `internal` so only `ChatThread` can call them.

- [ ] **Step 1: Create `ChatMessage`**

```csharp
using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

using SharedKernel;

namespace Chat.Domain.Chats.Entities;

public sealed class ChatMessage : Entity<ChatMessageId>
{
    public const int FailureReasonMaxLength = 1024;

    public ChatId ChatId { get; private set; } = default!;
    public ChatMessageId? ParentMessageId { get; private set; }
    public MessageRole Role { get; private set; }
    public MessageContent? Content { get; private set; }
    public Guid? ModelId { get; private set; }
    public MessageStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int SiblingIndex { get; private set; }

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
        Guid? modelId,
        MessageStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt,
        int siblingIndex
    ) : base(id)
    {
        ChatId = chatId;
        ParentMessageId = parentMessageId;
        Role = role;
        Content = content;
        ModelId = modelId;
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
        int siblingIndex
    ) => new
    (
        id: ChatMessageId.New(),
        chatId: chatId,
        parentMessageId: parentMessageId,
        role: MessageRole.User,
        content: content,
        modelId: null,
        status: MessageStatus.Completed,
        createdAt: createdAt,
        completedAt: createdAt,
        siblingIndex: siblingIndex
    );

    internal static ChatMessage CreateAssistantMessage
    (
        ChatId chatId,
        ChatMessageId parentMessageId,
        Guid? modelId,
        DateTimeOffset createdAt,
        int siblingIndex
    ) => new
    (
        id: ChatMessageId.New(),
        chatId: chatId,
        parentMessageId: parentMessageId,
        role: MessageRole.Assistant,
        content: null,
        modelId: modelId,
        status: MessageStatus.Generating,
        createdAt: createdAt,
        completedAt: null,
        siblingIndex: siblingIndex
    );

    internal ErrorOr<Success> Complete(MessageContent content, DateTimeOffset completedAt)
    {
        if (Status != MessageStatus.Generating)
            return ChatErrors.CannotCompleteNonGenerating(Id);

        Content = content;
        Status = MessageStatus.Completed;
        CompletedAt = completedAt;
        FailureReason = null;

        return Result.Success;
    }

    internal ErrorOr<Success> Fail(string reason, DateTimeOffset failedAt)
    {
        if (Status != MessageStatus.Generating)
            return ChatErrors.CannotFailNonGenerating(Id);

        string trimmed = (reason ?? string.Empty).Trim();

        if (trimmed.Length > FailureReasonMaxLength)
            trimmed = trimmed[..FailureReasonMaxLength];

        Status = MessageStatus.Failed;
        FailureReason = string.IsNullOrEmpty(trimmed) ? "Generation failed." : trimmed;
        CompletedAt = failedAt;

        return Result.Success;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/services/Chat/Chat.Domain/Chat.Domain.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs
git commit -m "feat(chat): add chat message entity with state transitions"
```

---

## Task 4: ChatThread Aggregate

**Files**
- Create: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs`

Carries the two locked guards (`AddUserMessage` strict-alternation, `RegenerateAssistant` terminal target) and the documented status-agnostic `SelectMessage`.

- [ ] **Step 1: Create `ChatThread`**

```csharp
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
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

    public IReadOnlyCollection<ChatMessage> Messages => _messages;

    private ChatThread()
        : base(default!)
    {
        // EF Core materialization only
    }

    private ChatThread
    (
        ChatId id,
        UserId userId,
        ChatTitle title,
        DateTimeOffset createdAt
    ) : base(id)
    {
        UserId = userId;
        Title = title;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static ChatThread Create
    (
        UserId userId,
        ChatTitle title,
        MessageContent firstUserMessage,
        DateTimeOffset createdAt
    )
    {
        ChatThread chat = new(ChatId.New(), userId, title, createdAt);

        ChatMessage root = ChatMessage.CreateUserMessage
        (
            chatId: chat.Id,
            parentMessageId: null,
            content: firstUserMessage,
            createdAt: createdAt,
            siblingIndex: 0
        );

        chat._messages.Add(root);
        chat.CurrentMessageId = root.Id;

        return chat;
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
            return ChatErrors.ParentMessageNotFound(parentMessageId);

        if (parent.Role != MessageRole.Assistant)
            return ChatErrors.UserParentMustBeAssistant(parentMessageId);

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
        Guid? modelId,
        DateTimeOffset createdAt
    )
    {
        ChatMessage? parent = FindMessage(parentMessageId);

        if (parent is null)
            return ChatErrors.ParentMessageNotFound(parentMessageId);

        if (parent.Role != MessageRole.User)
            return ChatErrors.AssistantParentMustBeUser(parentMessageId);

        ChatMessage message = ChatMessage.CreateAssistantMessage
        (
            chatId: Id,
            parentMessageId: parentMessageId,
            modelId: modelId,
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
            return ChatErrors.MessageNotFound(messageId);

        ErrorOr<Success> result = message.Complete(content, completedAt);

        if (result.IsError)
            return result.Errors;

        UpdatedAt = completedAt;

        return message;
    }

    public ErrorOr<ChatMessage> FailAssistantMessage
    (
        ChatMessageId messageId,
        string reason,
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
            return ChatErrors.MessageNotFound(messageId);

        if (target.Role != MessageRole.User)
            return ChatErrors.EditTargetMustBeUser(messageId);

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
        Guid? modelId,
        DateTimeOffset createdAt
    )
    {
        ChatMessage? target = FindMessage(messageId);

        if (target is null)
            return ChatErrors.MessageNotFound(messageId);

        if (target.Role != MessageRole.Assistant || target.ParentMessageId is null)
            return ChatErrors.RegenerationTargetMustBeAssistant(messageId);

        if (target.Status == MessageStatus.Generating)
            return ChatErrors.CannotRegenerateWhileGenerating(messageId);

        ChatMessage sibling = ChatMessage.CreateAssistantMessage
        (
            chatId: Id,
            parentMessageId: target.ParentMessageId,
            modelId: modelId,
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

    private int GetNextSiblingIndex(ChatMessageId? parentMessageId) =>
        _messages.Count(message => message.ParentMessageId == parentMessageId);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/services/Chat/Chat.Domain/Chat.Domain.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/services/Chat/Chat.Domain/Chats/ChatThread.cs
git commit -m "feat(chat): add chat thread aggregate with alternation and regenerate guards"
```

---

## Task 5: Repository Interface

**Files**
- Create: `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs`

The aggregate's only persistence boundary. Loads are always owner-scoped (by `UserId`), and the implementation (in the infrastructure plan) must load the full `Messages` collection so aggregate invariants hold in memory.

- [ ] **Step 1: Create `IChatRepository`**

```csharp
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Domain.Chats;

public interface IChatRepository
{
    Task<ChatThread?> GetByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    );

    void Add(ChatThread chat);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/services/Chat/Chat.Domain/Chat.Domain.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/services/Chat/Chat.Domain/Chats/IChatRepository.cs
git commit -m "feat(chat): add chat repository interface"
```

---

## Task 6: Invariants Reference (prose)

This task adds no code — it is the authoritative statement of the rules the aggregate enforces, for reviewers and for the test list in Task 8.

- [ ] **Step 1: Record the invariants** (in the PR description and/or a `### Invariants` note alongside the aggregate)

1. **Single aggregate root.** `ChatThread` is the only root; `ChatMessage` is created and mutated solely through `ChatThread`. Its factories and transitions are `internal`.
2. **Resolvable head.** `CurrentMessageId` always references a message contained in `Messages`. `Create` sets it to the root; every mutator sets it via `SetHead`; `SelectMessage` validates existence.
3. **Strict alternation.** On every root-to-leaf path, roles alternate. Formally: a user message has `ParentMessageId == null` (root) or a parent whose role is `Assistant`; an assistant message has a parent whose role is `User`.
4. **Status ↔ data consistency.**
   - `Generating` ⇒ `Content == null` and `CompletedAt == null`.
   - `Completed` ⇒ `Content != null`, `CompletedAt != null`, `FailureReason == null`.
   - `Failed` ⇒ `FailureReason != null`, `CompletedAt != null`, `Content == null`.
   - User messages are created `Completed` with content and `CompletedAt == CreatedAt`.
5. **One-way transitions.** The only transitions are `Generating → Completed` and `Generating → Failed`. `Complete`/`Fail` on a non-`Generating` message return a conflict error.
6. **Sibling indexing.** A new message's `SiblingIndex` equals the count of existing messages sharing its parent at creation time (root siblings share the `null` parent group).
7. **Regenerate target is terminal.** `RegenerateAssistant` requires a `Completed` or `Failed` assistant; `Generating` is rejected.
8. **Owner scoping is a load concern.** The aggregate does not store access rules beyond `UserId`; ownership is enforced at the repository boundary.

---

## Task 7: EF Persistence Boundary Notes (prose)

This task adds no code. It records what the persistence layer (implemented in the infrastructure plan) must honor so the domain model round-trips correctly. **No EF configuration code lives in this plan.**

- [ ] **Step 1: Record the boundary expectations**

- **Aggregate boundary.** `chats` (for `ChatThread`) owns `chat_messages` (for `ChatMessage`) one-to-many via `ChatId`. The `Messages` collection is mapped through the `_messages` backing field (field access); external code never adds to it directly.
- **Identity.** All ids are application-generated `Guid` (v7) values mapped `ValueGeneratedNever`, via value-object converters (`HasConversion` to/from `Guid`).
- **Enums as text.** `Role` and `Status` persist as strings (stable, debuggable), not ints.
- **Nullable content.** `chat_messages.content` is **nullable** — `Generating` and `Failed` assistant messages have no content. `failure_reason` and `completed_at` are nullable; `model_id` is a nullable raw `Guid`.
- **Referential rules.** `chat_messages.chat_id → chats.id` cascades on delete. `chat_messages.parent_message_id` is a self-reference with **restrict** on delete. There is **no** FK from `chats.current_message_id` and **no** FK from `chat_messages.model_id` in this pass.
- **Required head.** `chats.current_message_id` is `not null`.
- **Load completeness.** `IChatRepository.GetByIdAsync` must `Include` the full `Messages` collection and filter by `UserId`, so in-memory invariants (alternation, sibling indexing, head resolution) are evaluated against the complete tree.

---

## Task 8: Domain Tests (deferred — case list only)

**No test code is authored in this pass.** This task records the cases the eventual `Chat.Domain.Tests` suite must cover, so the deferral is explicit and the coverage intent is captured.

- [ ] **Step 1: Record the intended test cases**

Value objects:
- `ChatId` / `ChatMessageId`: `New` non-empty; `Create` succeeds for non-empty Guid; `Create` returns `*.Empty` validation for `Guid.Empty`; `FromDatabase` throws for empty.
- `ChatTitle` / `MessageContent`: `Create` returns `Required` when blank, `TooLong` past max length, trims and succeeds otherwise; `FromDatabase` throws on invalid persisted values.

Aggregate (`ChatThread`):
- `Create` seeds a single root user message and points the head at it.
- `BeginAssistantMessage` adds a `Generating` assistant under a user parent and moves the head.
- `BeginAssistantMessage` returns `AssistantParentMustBeUser` when the parent is an assistant.
- `AddUserMessage` adds a user message under an assistant parent and moves the head.
- `AddUserMessage` returns `UserParentMustBeAssistant` when the parent is a user message (strict alternation).
- `AddUserMessage` returns `ParentMessageNotFound` when the parent is missing.
- `AddUserMessage` / `BeginAssistantMessage` assign the next sibling index within the parent group.
- `CompleteAssistantMessage` sets content + `Completed` + `CompletedAt` and bumps `UpdatedAt`.
- `CompleteAssistantMessage` returns `CannotCompleteNonGenerating` when the target is already terminal.
- `FailAssistantMessage` sets `Failed` + `FailureReason`; returns `CannotFailNonGenerating` when not generating.
- `EditUserMessage` creates a user sibling without mutating the original; editing a root creates a root-level sibling; returns `EditTargetMustBeUser` for an assistant target.
- `RegenerateAssistant` creates an assistant sibling for a `Completed` target and for a `Failed` target.
- `RegenerateAssistant` returns `CannotRegenerateWhileGenerating` for a `Generating` target.
- `RegenerateAssistant` returns `RegenerationTargetMustBeAssistant` for a user target.
- `SelectMessage` moves the head for any existing message **including a `Generating` assistant**; returns `MessageNotFound` for an unknown id.

---

## Manual Checklist Summary

- [ ] Value objects implemented.
- [ ] `ChatErrors` implemented (incl. `UserParentMustBeAssistant`, `CannotRegenerateWhileGenerating`).
- [ ] `ChatMessage` entity implemented (nullable content, `Complete`/`Fail` guards).
- [ ] `ChatThread` aggregate implemented (strict-alternation guard, terminal regenerate guard, status-agnostic `SelectMessage`).
- [ ] `IChatRepository` implemented.
- [ ] Invariants recorded.
- [ ] EF persistence boundary notes recorded.
- [ ] Domain test cases listed (authoring deferred).
- [ ] Chat domain project builds.
