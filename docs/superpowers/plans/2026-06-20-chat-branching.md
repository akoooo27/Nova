# Chat Branching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add lazy, ChatGPT-style “Branch in new chat” creation that copies a selected source path into an independent `ChatThread` when the first new message is submitted.

**Architecture:** Extend the existing `POST /v1/chats` command with an optional source chat/message pair. The application loads the owner-scoped source without tracking, while `ChatThread.BranchFrom` validates and clones the root-to-branch-point path with new IDs; the handler then appends the new turn and commits it with the existing MassTransit outbox. Persist immediate provenance as an optional `ChatBranchOrigin` complex value object without foreign keys.

**Tech Stack:** .NET 10, C# 14, `Mediator.SourceGenerator` / `Mediator.Abstractions`, FastEndpoints 8, EF Core 10 with Npgsql, MassTransit EF outbox, FluentValidation 12, xUnit 2.

---

## Execution constraints

- Read the approved design first: `docs/superpowers/specs/2026-06-20-chat-branching-design.md`.
- Follow repository `AGENTS.md`: keep `Mediator`, FastEndpoints, and the pinned MassTransit version.
- Test work is explicitly approved for domain and application tests only. Do not add endpoint or infrastructure integration tests.
- Before every `dotnet build`, `dotnet test`, `dotnet restore`, or `dotnet ef` command, request elevated permission and explain that the command is needed to verify or generate this .NET change.
- Do not implement frontend code. Only add the documented frontend contract in Task 7.
- Preserve unrelated worktree changes. Stage only files named by the current task.

## File structure

### New files

- `src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatBranchOrigin.cs` — atomic immediate-source provenance value object.
- `tests/Chat/Chat.Domain.Tests/Chats/ValueObjects/ChatBranchOriginTests.cs` — value-object construction/equality coverage.
- `tests/Chat/Chat.Application.Tests/Turns/CreateChatCommandValidatorTests.cs` — branching field-pair validation.
- `src/services/Chat/Chat.Infrastructure/Database/Migrations/*_ChatBranchOrigin.cs` — EF-generated schema migration; EF supplies the timestamp prefix.
- `src/services/Chat/Chat.Infrastructure/Database/Migrations/*_ChatBranchOrigin.Designer.cs` — EF-generated migration metadata; EF supplies the timestamp prefix.
- `docs/diagrams/chat-branching-flow.md` — frontend route and API handoff contract.

### Modified files

- `src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatTitle.cs` — safe `Branch: ` title construction.
- `src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs` — internal immutable branch-copy factory.
- `src/services/Chat/Chat.Domain/Chats/ChatThread.cs` — nullable origin and domain-owned snapshot factory.
- `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs` — branch-point and corrupted-path errors.
- `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs` — owner-scoped no-tracking snapshot lookup.
- `tests/Chat/Chat.Domain.Tests/Chats/ValueObjects/ChatStringValueObjectTests.cs` — branch title behavior.
- `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs` — copy semantics and domain guards.
- `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommand.cs` — optional branch source IDs.
- `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommandValidator.cs` — both-or-neither rules.
- `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs` — lazy branch creation orchestration.
- `src/services/Chat/Chat.Application/Chats/Errors/ChatOperationErrors.cs` — incomplete-origin validation error for direct handler calls.
- `tests/Chat/Chat.Application.Tests/Turns/CreateChatHandlerTests.cs` — branch success and side-effect failure tests.
- `tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs` — snapshot lookup and added-thread recording.
- `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs` — `AsNoTracking` snapshot load.
- `src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Endpoint.cs` — FastEndpoints request fields and mapping.
- `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatThreadConfiguration.cs` — optional complex mapping and check constraint.
- `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs` — EF-generated model snapshot update.

---

### Task 1: Add branch-origin and branch-title value behavior

**Files:**
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatBranchOrigin.cs`
- Create: `tests/Chat/Chat.Domain.Tests/Chats/ValueObjects/ChatBranchOriginTests.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatTitle.cs`
- Modify: `tests/Chat/Chat.Domain.Tests/Chats/ValueObjects/ChatStringValueObjectTests.cs`

- [ ] **Step 1: Write failing value-object tests**

Create `ChatBranchOriginTests.cs`:

```csharp
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Domain.Tests.Chats.ValueObjects;

public sealed class ChatBranchOriginTests
{
    [Fact]
    public void CreateStoresSourceIdsAsOneValue()
    {
        ChatId sourceChatId = ChatId.New();
        ChatMessageId sourceMessageId = ChatMessageId.New();

        ChatBranchOrigin origin = ChatBranchOrigin.Create(sourceChatId, sourceMessageId);

        Assert.Equal(sourceChatId, origin.SourceChatId);
        Assert.Equal(sourceMessageId, origin.SourceMessageId);
        Assert.Equal(ChatBranchOrigin.Create(sourceChatId, sourceMessageId), origin);
    }
}
```

Append these tests to `ChatStringValueObjectTests.cs`:

```csharp
[Fact]
public void ChatTitleCreateBranchPrefixesSourceTitle()
{
    ChatTitle source = ChatTitle.FromDatabase("Planning chat");

    ChatTitle branch = ChatTitle.CreateBranch(source);

    Assert.Equal("Branch: Planning chat", branch.Value);
}

[Fact]
public void ChatTitleCreateBranchTruncatesSourceToDomainLimit()
{
    ChatTitle source = ChatTitle.FromDatabase(new string('a', ChatTitle.MaxLength));

    ChatTitle branch = ChatTitle.CreateBranch(source);

    Assert.Equal(ChatTitle.MaxLength, branch.Value.Length);
    Assert.StartsWith("Branch: ", branch.Value, StringComparison.Ordinal);
    Assert.Equal(new string('a', ChatTitle.MaxLength - "Branch: ".Length), branch.Value["Branch: ".Length..]);
}
```

- [ ] **Step 2: Run the focused domain tests and verify they fail**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~ChatBranchOriginTests|FullyQualifiedName~ChatStringValueObjectTests"
```

Expected: FAIL to compile because `ChatBranchOrigin` and `ChatTitle.CreateBranch` do not exist.

- [ ] **Step 3: Implement the value objects**

Create `ChatBranchOrigin.cs`:

```csharp
namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatBranchOrigin
{
    public ChatId SourceChatId { get; private init; } = default!;

    public ChatMessageId SourceMessageId { get; private init; } = default!;

    private ChatBranchOrigin()
    {
        // EF Core materialization only
    }

    private ChatBranchOrigin(ChatId sourceChatId, ChatMessageId sourceMessageId)
    {
        SourceChatId = sourceChatId;
        SourceMessageId = sourceMessageId;
    }

    internal static ChatBranchOrigin Create(ChatId sourceChatId, ChatMessageId sourceMessageId) =>
        new(sourceChatId, sourceMessageId);
}
```

Add this member to `ChatTitle` immediately after `Create`:

```csharp
public static ChatTitle CreateBranch(ChatTitle source)
{
    const string prefix = "Branch: ";
    int sourceLength = Math.Min(source.Value.Length, MaxLength - prefix.Length);

    return new ChatTitle($"{prefix}{source.Value[..sourceLength]}");
}
```

The factory cannot fail: `source` is already valid and the source portion is bounded before constructing the value.

- [ ] **Step 4: Run the focused domain tests and verify they pass**

Request elevated permission and rerun the Step 2 command.

Expected: PASS.

- [ ] **Step 5: Commit the value-object slice**

```bash
git add src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatBranchOrigin.cs src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatTitle.cs tests/Chat/Chat.Domain.Tests/Chats/ValueObjects/ChatBranchOriginTests.cs tests/Chat/Chat.Domain.Tests/Chats/ValueObjects/ChatStringValueObjectTests.cs
git commit -m "feat(chat): add branch origin value objects"
```

---

### Task 2: Implement domain-owned path snapshotting

**Files:**
- Modify: `src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs`
- Modify: `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs`

- [ ] **Step 1: Write failing snapshot-copy tests**

Add the following tests to `ChatThreadTests.cs` before its private helper methods:

```csharp
[Fact]
public void BranchFromCopiesOnlySelectedPathWithNewIdsAndIndependentMetadata()
{
    ChatThread source = TestChatFactory.CreateThread(isTemporary: true);
    source.Pin(TestChatFactory.CreatedAt.AddMinutes(1));
    source.Archive();
    ChatMessage root = TestChatFactory.RootMessage(source);
    ChatMessage firstAssistant = CompleteAssistant(source, root.Id, TestChatFactory.CreatedAt.AddMinutes(2));
    ChatMessage followUp = AddUser(source, firstAssistant.Id, TestChatFactory.CreatedAt.AddMinutes(4), "Follow up");
    ChatMessage branchPoint = CompleteAssistant(source, followUp.Id, TestChatFactory.CreatedAt.AddMinutes(5));
    _ = AddUser(source, branchPoint.Id, TestChatFactory.CreatedAt.AddMinutes(7), "Excluded descendant");
    _ = CompleteAssistant(source, followUp.Id, TestChatFactory.CreatedAt.AddMinutes(8));
    ChatMessage[] sourcePath = [root, firstAssistant, followUp, branchPoint];
    DateTimeOffset branchedAt = TestChatFactory.CreatedAt.AddHours(1);

    ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, branchPoint.Id, branchedAt);

    Assert.False(result.IsError);
    ChatThread branch = result.Value;
    ChatMessage[] copiedPath = GetActivePath(branch);
    Assert.Equal(sourcePath.Length, copiedPath.Length);
    Assert.DoesNotContain(copiedPath, copied => source.Messages.Any(original => original.Id == copied.Id));

    for (int index = 0; index < sourcePath.Length; index++)
    {
        Assert.Equal(sourcePath[index].Role, copiedPath[index].Role);
        Assert.Equal(sourcePath[index].Content, copiedPath[index].Content);
        Assert.Equal(sourcePath[index].LlmModelId, copiedPath[index].LlmModelId);
        Assert.Equal(sourcePath[index].Status, copiedPath[index].Status);
        Assert.Equal(sourcePath[index].FailureReason, copiedPath[index].FailureReason);
        Assert.Equal(sourcePath[index].CreatedAt, copiedPath[index].CreatedAt);
        Assert.Equal(sourcePath[index].CompletedAt, copiedPath[index].CompletedAt);
        Assert.Equal(0, copiedPath[index].SiblingIndex.Value);
        Assert.Equal(index == 0 ? null : copiedPath[index - 1].Id, copiedPath[index].ParentMessageId);
    }

    Assert.Equal(source.UserId, branch.UserId);
    Assert.Equal("Branch: Planning chat", branch.Title.Value);
    Assert.True(branch.IsTemporary);
    Assert.False(branch.IsPinned);
    Assert.False(branch.IsArchived);
    Assert.Equal(branchedAt, branch.CreatedAt);
    Assert.Equal(branchedAt, branch.UpdatedAt);
    Assert.Equal(copiedPath[^1].Id, branch.CurrentMessageId);
    Assert.Equal(source.Id, branch.BranchOrigin!.SourceChatId);
    Assert.Equal(branchPoint.Id, branch.BranchOrigin.SourceMessageId);
    Assert.Equal(6, source.Messages.Count);
}

[Fact]
public void BranchFromPreservesFailedAssistantState()
{
    ChatThread source = TestChatFactory.CreateThread();
    ChatMessage assistant = BeginAssistant(source);
    FailureReason reason = TestChatFactory.CreateFailureReason("Rate limited");
    DateTimeOffset failedAt = TestChatFactory.CreatedAt.AddMinutes(2);
    _ = source.FailAssistantMessage(assistant.Id, reason, failedAt);

    ErrorOr<ChatThread> result = ChatThread.BranchFrom
    (
        source,
        assistant.Id,
        TestChatFactory.CreatedAt.AddHours(1)
    );

    Assert.False(result.IsError);
    ChatMessage copied = GetActivePath(result.Value)[^1];
    Assert.Equal(MessageStatus.Failed, copied.Status);
    Assert.Equal(reason, copied.FailureReason);
    Assert.Equal(failedAt, copied.CompletedAt);
    Assert.Null(copied.Content);
}

[Fact]
public void BranchFromRejectsUserBranchPoint()
{
    ChatThread source = TestChatFactory.CreateThread();
    ChatMessage root = TestChatFactory.RootMessage(source);

    ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, root.Id, TestChatFactory.CreatedAt);

    AssertError(result, ErrorType.Conflict, "Chat.BranchPointMustBeAssistant");
}

[Fact]
public void BranchFromRejectsGeneratingAssistantBranchPoint()
{
    ChatThread source = TestChatFactory.CreateThread();
    ChatMessage assistant = BeginAssistant(source);

    ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, assistant.Id, TestChatFactory.CreatedAt);

    AssertError(result, ErrorType.Conflict, "Chat.CannotBranchWhileGenerating");
}

[Fact]
public void BranchFromReturnsMessageNotFoundForUnknownBranchPoint()
{
    ChatThread source = TestChatFactory.CreateThread();

    ErrorOr<ChatThread> result = ChatThread.BranchFrom
    (
        source,
        ChatMessageId.New(),
        TestChatFactory.CreatedAt
    );

    AssertError(result, ErrorType.NotFound, "Chat.MessageNotFound");
}

[Fact]
public void BranchFromRejectsCyclicPersistedPath()
{
    ChatThread source = TestChatFactory.CreateThread();
    ChatMessage root = TestChatFactory.RootMessage(source);
    ChatMessage assistant = CompleteAssistant(source);
    SetParentForCorruptionTest(root, assistant.Id);

    ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, assistant.Id, TestChatFactory.CreatedAt);

    AssertError(result, ErrorType.Unexpected, "Chat.InvalidBranchPath");
}

[Fact]
public void BranchFromRejectsMissingPersistedAncestor()
{
    ChatThread source = TestChatFactory.CreateThread();
    ChatMessage root = TestChatFactory.RootMessage(source);
    ChatMessage assistant = CompleteAssistant(source);
    SetParentForCorruptionTest(root, ChatMessageId.New());

    ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, assistant.Id, TestChatFactory.CreatedAt);

    AssertError(result, ErrorType.Unexpected, "Chat.InvalidBranchPath");
}

[Fact]
public void BranchFromRejectsAssistantAsPersistedRoot()
{
    ChatThread source = TestChatFactory.CreateThread();
    ChatMessage assistant = CompleteAssistant(source);
    SetParentForCorruptionTest(assistant, null);

    ErrorOr<ChatThread> result = ChatThread.BranchFrom(source, assistant.Id, TestChatFactory.CreatedAt);

    AssertError(result, ErrorType.Unexpected, "Chat.InvalidBranchPath");
}
```

Add these test helpers beside the existing private helpers:

```csharp
private static ChatMessage[] GetActivePath(ChatThread chat)
{
    List<ChatMessage> path = [];
    ChatMessage? cursor = chat.FindMessage(chat.CurrentMessageId);

    while (cursor is not null)
    {
        path.Add(cursor);
        cursor = cursor.ParentMessageId is null ? null : chat.FindMessage(cursor.ParentMessageId);
    }

    path.Reverse();
    return [.. path];
}

private static void SetParentForCorruptionTest(ChatMessage message, ChatMessageId? parentMessageId)
{
    typeof(ChatMessage)
        .GetProperty(nameof(ChatMessage.ParentMessageId))!
        .SetValue(message, parentMessageId);
}
```

Reflection is deliberately confined to corruption tests; no production rehydration/testing hook is added.

- [ ] **Step 2: Run the focused aggregate tests and verify they fail**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~ChatThreadTests.BranchFrom"
```

Expected: FAIL to compile because `BranchFrom` and `BranchOrigin` do not exist.

- [ ] **Step 3: Add branch-specific domain errors**

Append these methods to `ChatErrors`:

```csharp
public static Error BranchPointMustBeAssistant(ChatMessageId messageId) =>
    Error.Conflict
    (
        code: "Chat.BranchPointMustBeAssistant",
        description: $"Only an assistant message can be used as a branch point; '{messageId.Value}' is not an assistant message."
    );

public static Error CannotBranchWhileGenerating(ChatMessageId messageId) =>
    Error.Conflict
    (
        code: "Chat.CannotBranchWhileGenerating",
        description: $"Message '{messageId.Value}' is still generating and cannot be branched yet."
    );

public static Error InvalidBranchPath(ChatMessageId messageId) =>
    Error.Unexpected
    (
        code: "Chat.InvalidBranchPath",
        description: $"The persisted ancestry for message '{messageId.Value}' is invalid."
    );
```

- [ ] **Step 4: Add immutable message copying**

Keep the existing private constructor and both creation factories unchanged. Add this internal factory after `CreateAssistantMessage`:

```csharp
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
```

`FailureReason` remains nullable on the entity because it is populated only for failed assistant messages. The copy factory reads it from the already-valid source message and assigns it internally, avoiding a nullable parameter on the shared constructor while preserving failed messages exactly.

- [ ] **Step 5: Generalize aggregate construction and implement `BranchFrom`**

Replace the private `ChatThread` constructor with a constructor that accepts the whole initial path:

```csharp
private ChatThread
(
    ChatId id,
    UserId userId,
    ChatTitle title,
    IReadOnlyCollection<ChatMessage> messages,
    ChatMessageId currentMessageId,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt,
    bool isTemporary,
    ChatBranchOrigin? branchOrigin
) : base(id)
{
    UserId = userId;
    Title = title;
    CurrentMessageId = currentMessageId;
    CreatedAt = createdAt;
    UpdatedAt = updatedAt;
    IsTemporary = isTemporary;
    BranchOrigin = branchOrigin;
    _messages = [.. messages];
}
```

Add the aggregate property:

```csharp
public ChatBranchOrigin? BranchOrigin { get; private set; }
```

Update `Create` to call the generalized constructor with `messages: [root]`, `currentMessageId: root.Id`, and `branchOrigin: null`.

Add this factory after `Create`:

```csharp
public static ErrorOr<ChatThread> BranchFrom
(
    ChatThread source,
    ChatMessageId branchPointId,
    DateTimeOffset createdAt
)
{
    ArgumentNullException.ThrowIfNull(source);

    ChatMessage? branchPoint = source.FindMessage(branchPointId);

    if (branchPoint is null)
    {
        return ChatErrors.MessageNotFound(branchPointId);
    }

    if (branchPoint.Role != MessageRole.Assistant)
    {
        return ChatErrors.BranchPointMustBeAssistant(branchPointId);
    }

    if (branchPoint.Status is not MessageStatus.Completed and not MessageStatus.Failed)
    {
        return ChatErrors.CannotBranchWhileGenerating(branchPointId);
    }

    List<ChatMessage> sourcePath = [];
    HashSet<ChatMessageId> visited = [];
    ChatMessage? cursor = branchPoint;

    while (cursor is not null)
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

        cursor = source.FindMessage(cursor.ParentMessageId!);

        if (cursor is null)
        {
            return ChatErrors.InvalidBranchPath(branchPointId);
        }
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
                : copiedIds[message.ParentMessageId!]
        ))
        .ToList();

    return new ChatThread
    (
        id: branchId,
        userId: source.UserId,
        title: ChatTitle.CreateBranch(source.Title),
        messages: copiedMessages,
        currentMessageId: copiedIds[branchPoint.Id],
        createdAt: createdAt,
        updatedAt: createdAt,
        isTemporary: source.IsTemporary,
        branchOrigin: ChatBranchOrigin.Create(source.Id, branchPoint.Id)
    );
}
```

- [ ] **Step 6: Run all chat domain tests**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~Chat.Domain.Tests.Chats"
```

Expected: PASS, including all existing aggregate tests.

- [ ] **Step 7: Commit the domain snapshot slice**

```bash
git add src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs src/services/Chat/Chat.Domain/Chats/ChatThread.cs src/services/Chat/Chat.Domain/Chats/ChatErrors.cs tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs
git commit -m "feat(chat): add domain snapshot branching"
```

---

### Task 3: Extend and validate the create-chat command

**Files:**
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommand.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommandValidator.cs`
- Create: `tests/Chat/Chat.Application.Tests/Turns/CreateChatCommandValidatorTests.cs`

- [ ] **Step 1: Write failing command-validator tests**

Create `CreateChatCommandValidatorTests.cs`:

```csharp
using Chat.Application.Chats.Commands.CreateChat;

using FluentValidation.Results;

namespace Chat.Application.Tests.Turns;

public sealed class CreateChatCommandValidatorTests
{
    private readonly CreateChatCommandValidator _validator = new();

    [Fact]
    public void ValidateAcceptsNormalCreationWithoutBranchIds()
    {
        ValidationResult result = _validator.Validate(new CreateChatCommand("Hello", Guid.CreateVersion7()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateAcceptsBranchCreationWithBothIds()
    {
        ValidationResult result = _validator.Validate(new CreateChatCommand
        (
            Message: "Continue differently",
            LlmModelId: Guid.CreateVersion7(),
            BranchingFromChatId: Guid.CreateVersion7(),
            BranchingFromMessageId: Guid.CreateVersion7()
        ));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidateRejectsIncompleteBranchPair(bool includeChatId)
    {
        CreateChatCommand command = new
        (
            Message: "Continue differently",
            LlmModelId: Guid.CreateVersion7(),
            BranchingFromChatId: includeChatId ? Guid.CreateVersion7() : null,
            BranchingFromMessageId: includeChatId ? null : Guid.CreateVersion7()
        );

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains
        (
            result.Errors,
            error => error.PropertyName == (includeChatId
                ? nameof(CreateChatCommand.BranchingFromMessageId)
                : nameof(CreateChatCommand.BranchingFromChatId))
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidateRejectsEmptyBranchId(bool emptyChatId)
    {
        CreateChatCommand command = new
        (
            Message: "Continue differently",
            LlmModelId: Guid.CreateVersion7(),
            BranchingFromChatId: emptyChatId ? Guid.Empty : Guid.CreateVersion7(),
            BranchingFromMessageId: emptyChatId ? Guid.CreateVersion7() : Guid.Empty
        );

        ValidationResult result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }
}
```

- [ ] **Step 2: Run the validator tests and verify they fail**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~CreateChatCommandValidatorTests"
```

Expected: FAIL to compile because the command fields do not exist.

- [ ] **Step 3: Add optional source IDs to the command**

Replace the command declaration with:

```csharp
public sealed record CreateChatCommand
(
    string Message,
    Guid LlmModelId,
    TurnGenerationOptions? GenerationOptions = null,
    bool IsTemporary = false,
    Guid? BranchingFromChatId = null,
    Guid? BranchingFromMessageId = null
) : ICommand<ErrorOr<TurnStartedResult>>;
```

Appending the fields preserves all existing positional call sites.

- [ ] **Step 4: Add both-or-neither validation**

Append these rules to `CreateChatCommandValidator`:

```csharp
RuleFor(x => x.BranchingFromChatId)
    .NotEmpty()
    .When(x => x.BranchingFromChatId.HasValue || x.BranchingFromMessageId.HasValue);

RuleFor(x => x.BranchingFromMessageId)
    .NotEmpty()
    .When(x => x.BranchingFromChatId.HasValue || x.BranchingFromMessageId.HasValue);
```

The condition skips both rules for normal creation. Once either value is supplied, both nullable GUIDs must be present and non-empty.

- [ ] **Step 5: Run the validator and existing create-handler tests**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~CreateChatCommandValidatorTests|FullyQualifiedName~CreateChatHandlerTests"
```

Expected: PASS; existing normal creation behavior remains unchanged.

- [ ] **Step 6: Commit the command-contract slice**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommand.cs src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommandValidator.cs tests/Chat/Chat.Application.Tests/Turns/CreateChatCommandValidatorTests.cs
git commit -m "feat(chat): validate branch creation inputs"
```

---

### Task 4: Orchestrate lazy branch creation in the application layer

**Files:**
- Modify: `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Errors/ChatOperationErrors.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`
- Modify: `tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs`
- Modify: `tests/Chat/Chat.Application.Tests/Turns/CreateChatHandlerTests.cs`

- [ ] **Step 1: Write failing application-flow tests**

Add these tests to `CreateChatHandlerTests.cs`:

```csharp
[Fact]
public async Task HandleCreatesIndependentBranchAndPublishesTurnForNewAssistant()
{
    LlmModel model = SeedModel();
    ChatThread source = CreateSourceThread(model, isTemporary: true);
    ChatMessage sourceBranchPoint = source.FindMessage(source.CurrentMessageId)!;
    int sourceMessageCount = source.Messages.Count;
    _chats.Seed(source);

    ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
    (
        new CreateChatCommand
        (
            Message: "Explore another direction",
            LlmModelId: model.Id.Value,
            IsTemporary: false,
            BranchingFromChatId: source.Id.Value,
            BranchingFromMessageId: sourceBranchPoint.Id.Value
        ),
        CancellationToken.None
    );

    Assert.False(result.IsError);
    ChatThread branch = Assert.Single(_chats.AddedThreads);
    Assert.NotEqual(source.Id, branch.Id);
    Assert.Equal("Branch: Source chat", branch.Title.Value);
    Assert.True(branch.IsTemporary);
    Assert.Equal(source.Id, branch.BranchOrigin!.SourceChatId);
    Assert.Equal(sourceBranchPoint.Id, branch.BranchOrigin.SourceMessageId);
    Assert.Equal(sourceMessageCount, source.Messages.Count);

    ChatMessage newUser = branch.FindMessage(ChatMessageId.FromDatabase(result.Value.UserMessageId))!;
    ChatMessage newAssistant = branch.FindMessage(ChatMessageId.FromDatabase(result.Value.AssistantMessageId))!;
    Assert.Equal(MessageRole.User, newUser.Role);
    Assert.Equal("Explore another direction", newUser.Content!.Value);
    Assert.Equal(MessageRole.Assistant, newAssistant.Role);
    Assert.Equal(MessageStatus.Generating, newAssistant.Status);
    Assert.Equal(newUser.Id, newAssistant.ParentMessageId);
    Assert.Equal(newAssistant.Id, branch.CurrentMessageId);

    TurnRequested turn = Assert.IsType<TurnRequested>(Assert.Single(_messageBus.Published));
    Assert.Equal(branch.Id.Value, turn.ChatId);
    Assert.Equal(newAssistant.Id.Value, turn.AssistantMessageId);
    Assert.Equal(1, _unitOfWork.SaveCount);
    Assert.Equal(1, _chats.SnapshotGetCallCount);
}

[Fact]
public async Task HandleReturnsNotFoundWithoutSideEffectsForForeignSource()
{
    LlmModel model = SeedModel();
    ChatThread source = CreateSourceThread(model, userId: "auth0|other-user");
    _chats.Seed(source);

    ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
    (
        new CreateChatCommand
        (
            Message: "Continue",
            LlmModelId: model.Id.Value,
            BranchingFromChatId: source.Id.Value,
            BranchingFromMessageId: source.CurrentMessageId.Value
        ),
        CancellationToken.None
    );

    Assert.True(result.IsError);
    Assert.Equal("Chat.NotFound", result.FirstError.Code);
    Assert.Empty(_chats.AddedThreads);
    Assert.Empty(_messageBus.Published);
    Assert.Equal(0, _unitOfWork.SaveCount);
}

[Fact]
public async Task HandleReturnsBranchConflictWithoutSideEffectsForUserPoint()
{
    LlmModel model = SeedModel();
    ChatThread source = CreateSourceThread(model);
    ChatMessage root = source.Messages.Single(message => message.ParentMessageId is null);
    _chats.Seed(source);

    ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
    (
        new CreateChatCommand
        (
            Message: "Continue",
            LlmModelId: model.Id.Value,
            BranchingFromChatId: source.Id.Value,
            BranchingFromMessageId: root.Id.Value
        ),
        CancellationToken.None
    );

    Assert.True(result.IsError);
    Assert.Equal("Chat.BranchPointMustBeAssistant", result.FirstError.Code);
    Assert.Empty(_chats.AddedThreads);
    Assert.Empty(_messageBus.Published);
    Assert.Equal(0, _unitOfWork.SaveCount);
}

[Fact]
public async Task HandleRejectsIncompleteBranchOriginWhenCalledWithoutPipeline()
{
    LlmModel model = SeedModel();

    ErrorOr<TurnStartedResult> result = await CreateHandler().Handle
    (
        new CreateChatCommand
        (
            Message: "Continue",
            LlmModelId: model.Id.Value,
            BranchingFromChatId: Guid.CreateVersion7()
        ),
        CancellationToken.None
    );

    Assert.True(result.IsError);
    Assert.Equal("Chat.BranchOriginIncomplete", result.FirstError.Code);
    Assert.Empty(_chats.AddedThreads);
    Assert.Empty(_messageBus.Published);
    Assert.Equal(0, _unitOfWork.SaveCount);
}
```

Add this helper to `CreateChatHandlerTests`:

```csharp
private static ChatThread CreateSourceThread
(
    LlmModel model,
    string userId = "auth0|user-1",
    bool isTemporary = false
)
{
    ChatThread source = ChatThread.Create
    (
        userId: UserId.FromDatabase(userId),
        title: ChatTitle.FromDatabase("Source chat"),
        firstUserMessage: MessageContent.FromDatabase("Original prompt"),
        createdAt: Now.AddHours(-1),
        isTemporary: isTemporary
    );

    ChatMessage assistant = source.BeginAssistantMessage
    (
        parentMessageId: source.CurrentMessageId,
        llmModelId: model.Id,
        createdAt: Now.AddMinutes(-59)
    ).Value;

    _ = source.CompleteAssistantMessage
    (
        messageId: assistant.Id,
        content: MessageContent.FromDatabase("Original answer"),
        completedAt: Now.AddMinutes(-58)
    );

    return source;
}
```

Add `using Chat.Domain.Shared;` to the test file.

- [ ] **Step 2: Run the focused handler tests and verify they fail**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~CreateChatHandlerTests"
```

Expected: FAIL to compile because repository snapshot lookup, `AddedThreads`, and handler branching do not exist.

- [ ] **Step 3: Add the owner-scoped snapshot repository method**

Add this method to `IChatRepository`:

```csharp
Task<ChatThread?> GetSnapshotByIdAsync
(
    ChatId id,
    UserId userId,
    CancellationToken cancellationToken = default
);
```

Implement it in `ChatRepository`:

```csharp
public async Task<ChatThread?> GetSnapshotByIdAsync
(
    ChatId id,
    UserId userId,
    CancellationToken cancellationToken = default
)
{
    return await db.ChatThreads
        .AsNoTracking()
        .Include(chat => chat.Messages)
        .FirstOrDefaultAsync(chat => chat.Id == id && chat.UserId == userId, cancellationToken);
}
```

Extend `FakeChatRepository` with:

```csharp
private readonly List<ChatThread> _addedThreads = [];

public IReadOnlyList<ChatThread> AddedThreads => _addedThreads;

public int SnapshotGetCallCount { get; private set; }

public Task<ChatThread?> GetSnapshotByIdAsync
(
    ChatId id,
    UserId userId,
    CancellationToken cancellationToken = default
)
{
    SnapshotGetCallCount++;
    ChatThread? thread = _threads.FirstOrDefault(candidate => candidate.Id == id && candidate.UserId == userId);

    return Task.FromResult(thread);
}
```

Update its existing `Add` method:

```csharp
public void Add(ChatThread chat)
{
    _threads.Add(chat);
    _addedThreads.Add(chat);
}
```

- [ ] **Step 4: Add direct-handler validation error**

Append to `ChatOperationErrors`:

```csharp
public static Error BranchOriginIncomplete =>
    Error.Validation
    (
        code: "Chat.BranchOriginIncomplete",
        description: "Branching source chat and message ids must be supplied together."
    );
```

- [ ] **Step 5: Refactor `CreateChatHandler` into normal and branch creation paths**

Keep the existing constructor dependencies and shared user/content/model validation. Extend that validation with:

```csharp
ChatId? sourceChatId = null;
ChatMessageId? sourceMessageId = null;
bool hasSourceChatId = command.BranchingFromChatId.HasValue;
bool hasSourceMessageId = command.BranchingFromMessageId.HasValue;

if (hasSourceChatId != hasSourceMessageId)
{
    errors.Add(ChatOperationErrors.BranchOriginIncomplete);
}
else if (hasSourceChatId)
{
    ErrorOr<ChatId> sourceChatIdResult = ChatId.Create(command.BranchingFromChatId!.Value);
    ErrorOr<ChatMessageId> sourceMessageIdResult = ChatMessageId.Create(command.BranchingFromMessageId!.Value);

    if (sourceChatIdResult.IsError)
    {
        errors.AddRange(sourceChatIdResult.Errors);
    }
    else
    {
        sourceChatId = sourceChatIdResult.Value;
    }

    if (sourceMessageIdResult.IsError)
    {
        errors.AddRange(sourceMessageIdResult.Errors);
    }
    else
    {
        sourceMessageId = sourceMessageIdResult.Value;
    }
}
```

After shared model-usability validation and `DateTimeOffset now = dateTimeProvider.UtcNow`, replace unconditional normal creation with:

```csharp
ChatThread thread;
ChatMessageId userMessageId;

if (sourceChatId is null)
{
    string titleSource = content.Value.Length <= ChatTitle.MaxLength
        ? content.Value
        : content.Value[..ChatTitle.MaxLength];

    ErrorOr<ChatTitle> titleResult = ChatTitle.Create(titleSource);

    if (titleResult.IsError)
    {
        return titleResult.Errors;
    }

    thread = ChatThread.Create
    (
        userId: userId,
        title: titleResult.Value,
        firstUserMessage: content,
        createdAt: now,
        isTemporary: command.IsTemporary
    );

    userMessageId = thread.CurrentMessageId;
}
else
{
    ChatThread? source = await chats.GetSnapshotByIdAsync
    (
        id: sourceChatId,
        userId: userId,
        cancellationToken: cancellationToken
    );

    if (source is null)
    {
        return ChatOperationErrors.ChatNotFound(sourceChatId);
    }

    ErrorOr<ChatThread> branchResult = ChatThread.BranchFrom(source, sourceMessageId!, now);

    if (branchResult.IsError)
    {
        return branchResult.Errors;
    }

    thread = branchResult.Value;

    ErrorOr<ChatMessage> userMessageResult = thread.AddUserMessage
    (
        parentMessageId: thread.CurrentMessageId,
        content: content,
        createdAt: now
    );

    if (userMessageResult.IsError)
    {
        return userMessageResult.Errors;
    }

    userMessageId = userMessageResult.Value.Id;
}
```

Leave the common `BeginAssistantMessage`, `chats.Add`, `TurnRequested` publication-before-save, and `TurnStartedResult` code after this branch unchanged. This guarantees the outbox still contains new IDs only.

- [ ] **Step 6: Run the focused handler and validator tests**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~CreateChatHandlerTests|FullyQualifiedName~CreateChatCommandValidatorTests"
```

Expected: PASS.

- [ ] **Step 7: Run all application tests**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj
```

Expected: PASS with no regression in send-message, turn orchestration, queries, model catalog, or favorites.

- [ ] **Step 8: Commit the application orchestration slice**

```bash
git add src/services/Chat/Chat.Domain/Chats/IChatRepository.cs src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs src/services/Chat/Chat.Application/Chats/Errors/ChatOperationErrors.cs src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs tests/Chat/Chat.Application.Tests/Turns/CreateChatHandlerTests.cs
git commit -m "feat(chat): create branch snapshots on first message"
```

---

### Task 5: Expose branch source fields through FastEndpoints

**Files:**
- Modify: `src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Endpoint.cs`

- [ ] **Step 1: Extend the FastEndpoints request contract**

Replace the positional request declaration with:

```csharp
internal sealed record Request
(
    string Message,
    Guid ModelId,
    bool ForceUseSearch = false,
    Guid? BranchingFromChatId = null,
    Guid? BranchingFromMessageId = null,
    [property: QueryParam, BindFrom("temporary-chat")]
    bool IsTemporary = false
);
```

- [ ] **Step 2: Map the branch fields into the Mediator command**

Replace the command construction with:

```csharp
CreateChatCommand command = new
(
    Message: request.Message,
    LlmModelId: request.ModelId,
    GenerationOptions: new TurnGenerationOptions(ForceUseSearch: request.ForceUseSearch),
    IsTemporary: request.IsTemporary,
    BranchingFromChatId: request.BranchingFromChatId,
    BranchingFromMessageId: request.BranchingFromMessageId
);
```

Update the endpoint description to:

```csharp
.WithDescription("Creates a normal chat or lazily snapshots a source path into a branched chat, then starts generating the assistant reply asynchronously.")
```

Keep the existing `201 Created`, `Location`, problem responses, tags, and `TurnStartedResponse` unchanged.

- [ ] **Step 3: Build the API project**

Request elevated permission, then run:

```bash
dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj --no-restore
```

Expected: BUILD SUCCEEDED with zero warnings and errors.

- [ ] **Step 4: Commit the HTTP contract slice**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Endpoint.cs
git commit -m "feat(chat): expose lazy branch creation contract"
```

---

### Task 6: Persist optional branch origin

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatThreadConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Database/Migrations/*_ChatBranchOrigin.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Database/Migrations/*_ChatBranchOrigin.Designer.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

- [ ] **Step 1: Map the optional complex value and database invariant**

Change the table mapping at the start of `ChatThreadConfiguration.Configure` to:

```csharp
builder.ToTable("chats", table => table.HasCheckConstraint
(
    "ck_chats_branch_origin_complete",
    "(branched_from_chat_id is null) = (branched_from_message_id is null)"
));
```

Add this mapping after `CurrentMessageId`:

```csharp
builder.ComplexProperty(chat => chat.BranchOrigin, origin =>
{
    origin.IsRequired(false);

    origin.Property(value => value.SourceChatId)
        .HasConversion
        (
            id => id.Value,
            value => ChatId.FromDatabase(value)
        )
        .HasColumnName("branched_from_chat_id");

    origin.Property(value => value.SourceMessageId)
        .HasConversion
        (
            id => id.Value,
            value => ChatMessageId.FromDatabase(value)
        )
        .HasColumnName("branched_from_message_id");
});
```

Do not configure relationships or indexes for these columns.

- [ ] **Step 2: Generate the EF migration**

Request elevated permission, then run:

```bash
dotnet ef migrations add ChatBranchOrigin --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj --context ChatDbContext --output-dir Database/Migrations
```

Expected: EF creates the timestamped `ChatBranchOrigin` migration, designer file, and updates `ChatDbContextModelSnapshot.cs`.

- [ ] **Step 3: Inspect the generated migration for the exact schema**

The generated `Up` must contain the equivalent of:

```csharp
migrationBuilder.AddColumn<Guid>(
    name: "branched_from_chat_id",
    table: "chats",
    type: "uuid",
    nullable: true);

migrationBuilder.AddColumn<Guid>(
    name: "branched_from_message_id",
    table: "chats",
    type: "uuid",
    nullable: true);

migrationBuilder.AddCheckConstraint(
    name: "ck_chats_branch_origin_complete",
    table: "chats",
    sql: "(branched_from_chat_id is null) = (branched_from_message_id is null)");
```

The generated `Down` must drop the constraint and both columns. Confirm there are no foreign keys and no lineage indexes. If EF emits anything else, correct the model configuration and regenerate rather than hand-editing the model snapshot.

- [ ] **Step 4: Verify the migration is reproducible and the solution builds**

Request elevated permission, then run:

```bash
dotnet ef migrations script --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj --context ChatDbContext --no-transactions
dotnet build Nova.slnx --no-restore
```

Expected: migration SQL contains two nullable UUID columns plus the pair-completeness check, and BUILD SUCCEEDED with zero warnings/errors.

- [ ] **Step 5: Commit persistence**

```bash
git add src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatThreadConfiguration.cs src/services/Chat/Chat.Infrastructure/Database/Migrations
git commit -m "feat(chat): persist chat branch origin"
```

---

### Task 7: Document the frontend branch handoff

**Files:**
- Create: `docs/diagrams/chat-branching-flow.md`

- [ ] **Step 1: Write the frontend contract document**

Create the file with this content:

````markdown
# Chat Branching Frontend Flow

Nova creates a permanent branched chat only when the user sends the first new message. Clicking **Branch in new chat** prepares a client-side draft and does not call a backend mutation endpoint.

## Route lifecycle

```text
/branch/{sourceChatId}/{sourceMessageId}
    -> /c/WEB:{clientGeneratedUuid}
    -> POST /v1/chats
    -> /c/{permanentChatId}
```

1. Read the source chat and assistant message IDs from `/branch/...`.
2. Retain those IDs in client navigation state.
3. Generate a temporary `WEB:<uuid>` identifier and navigate to `/c/WEB:<uuid>`.
4. Render the source path through the selected assistant message from existing client data or `GET /v1/chats/{sourceChatId}`.
5. On the first send, include both source IDs in `POST /v1/chats`.
6. Replace the temporary URL with `/c/{chatId}` from the successful response.
7. Subscribe to `streamPath` exactly as for normal chat creation.

The backend never stores or interprets the `WEB:` identifier.

## First-send request

```http
POST /v1/chats
Content-Type: application/json
```

```json
{
  "message": "Explore a different direction",
  "modelId": "a5e69b6d-b4f8-4d60-8f85-b59eb5541572",
  "forceUseSearch": false,
  "branchingFromChatId": "6a367f70-5f2c-83ed-bcde-39cd7687000a",
  "branchingFromMessageId": "5b117028-ef93-41fe-b961-434f82ba5425"
}
```

Do not send `action`, `parentMessageId`, or the `WEB:` identifier. Normal chat creation uses the same endpoint and omits both branching fields.

## Success

`201 Created` returns the permanent `chatId`, the new user and assistant message IDs, and the normal stream path. Replace the temporary route before streaming or immediately after subscribing; either order must use the permanent IDs from the response.

## Errors

- `400`: malformed message/model fields, only one branch source ID, or an empty source ID. Keep the draft and show validation feedback.
- `404`: the source chat/message no longer exists or is not owned by the user. Keep the typed text and offer normal-chat creation or cancellation.
- `409`: the selected point is not an assistant message or is still generating. Keep the draft; generating conflicts may be retried after the source turn finishes.

The source chat is never changed by this flow. After success, the branch is an independent snapshot and remains valid if the source is later changed or deleted.
````

- [ ] **Step 2: Check the document and repository diff**

```bash
git diff --check
rg -n "WEB:|branchingFromChatId|branchingFromMessageId|POST /v1/chats" docs/diagrams/chat-branching-flow.md
```

Expected: no diff errors and all four contract terms are present.

- [ ] **Step 3: Commit documentation**

```bash
git add docs/diagrams/chat-branching-flow.md
git commit -m "docs(chat): document frontend branch flow"
```

---

### Task 8: Final verification

**Files:**
- Verify only; do not add new scope.

- [ ] **Step 1: Run all approved tests**

Request elevated permission, then run:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --no-restore
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --no-restore
```

Expected: both projects PASS with zero failed tests.

- [ ] **Step 2: Build the full solution**

Request elevated permission, then run:

```bash
dotnet build Nova.slnx --no-restore
```

Expected: BUILD SUCCEEDED with zero warnings and errors.

- [ ] **Step 3: Audit the implementation against the approved design**

```bash
git diff development...HEAD --check
git status --short
git log --oneline --decorate development..HEAD
```

Confirm:

- Normal `POST /v1/chats` behavior remains intact.
- Branch creation requires both source IDs.
- Only terminal assistant points are accepted.
- Only the selected ancestor path is copied.
- All copied IDs and parents belong to the new chat.
- `BranchOrigin` is optional, atomic, and persisted without foreign keys.
- The source thread is loaded owner-scoped and no-tracking.
- New chat/messages and `TurnRequested` remain in one outbox-backed transaction.
- No frontend code, lineage query, idempotency feature, endpoint/infrastructure tests, MassTransit upgrade, or unrelated refactor was introduced.
- The worktree is clean after the planned commits.
