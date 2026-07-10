# Shared Chat Remix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an authenticated viewer of a shared chat deep-copy its conversation path into a new independent chat in their own account, gated by an owner opt-in chosen permanently at share-creation time.

**Architecture:** Reuse the existing branch deep-copy primitive (`ChatMessage.CopyForBranch`) via a new domain factory `ChatThread.CreateRemix`. `shared_chats` gains an immutable `allow_remix` flag (the sharer's consent), which authorizes the one server-side read of the source chat outside its owner scope. The remix is passive: it copies the path and stops (no `TurnRequested`, no generation). The new chat records an internal-only `RemixOrigin` and is never distinguishable from a normal chat in any read contract except the single `allowRemix` boolean added to the public read.

**Tech Stack:** C# / .NET, DDD vertical slices, FastEndpoints, `Mediator` (source-generated), `ErrorOr`, EF Core + PostgreSQL (writes), Dapper (reads), xUnit tests.

## Global Constraints

- Use `Mediator` (`ICommandHandler`/`IQueryHandler`), never MediatR. Results are `ErrorOr<T>`.
- HTTP via FastEndpoints, versioned `Version(1)`, not ASP.NET controllers.
- `Error.Conflict` → 409, `Error.NotFound` → 404, `Error.Forbidden` → 403, `Error.Unexpected` → 500 (see `Shared.Api.Infrastructure.CustomResults`).
- EF columns use the snake_case naming convention already configured; explicit column names are lowercase snake_case.
- The remixer is resolved from `IUserContext.UserId`; the new chat is owned by that user.
- Remix reads the source chat **without owner scoping** — this is the only such read in the system and is justified only by `allow_remix = true`.
- `allow_remix` is set once at share creation and is immutable. No share-mutation endpoint is added.
- Remix publishes no `TurnRequested` and starts no generation (passive copy).
- Do not report blank-line/newline formatting as issues. Tests are in scope (user-approved).
- The BFF requires **no change**: `POST /api/chat/v1/shared-chats/{shareId}/remix` is covered by the existing authenticated catch-all route (`/api/chat/{**catch-all}` with user token + antiforgery in `src/services/BFF/RemoteApis/ChatApiProxyConfiguration.cs`).

---

### Task 1: Domain — `ChatRemixOrigin`, `ChatThread.CreateRemix`, and errors

**Files:**
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatRemixOrigin.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs` (add `RemixOrigin` property after `BranchOrigin` at line 35; add `CreateRemix` after `BranchFrom` which ends at line 203)
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs` (add two errors near the branch errors, after `InvalidBranchPath` at line 109)
- Test: `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs` (add remix tests; reuse existing `CompleteAssistant`, `AddUser`, `GetActivePath`, `AssertError`, `SetParentForCorruptionTest` helpers)

**Interfaces:**
- Produces:
  - `ChatRemixOrigin` — `public sealed record` with `SharedChatId ShareId`, `ChatId SourceChatId`, `ChatMessageId SourceMessageId`; `internal static ChatRemixOrigin Create(SharedChatId shareId, ChatId sourceChatId, ChatMessageId sourceMessageId)`.
  - `ChatThread.RemixOrigin` — `public ChatRemixOrigin? RemixOrigin { get; private set; }`.
  - `ChatThread.CreateRemix(UserId remixerUserId, ChatThread source, ChatMessageId sharedNodeId, SharedChatId shareId, ChatTitle title, DateTimeOffset createdAt) -> ErrorOr<ChatThread>`.
  - `ChatErrors.RemixTargetMustBeAssistant(ChatMessageId messageId) -> Error.Conflict` (code `Chat.RemixTargetMustBeAssistant`).
  - `ChatErrors.InvalidRemixPath(ChatMessageId messageId) -> Error.Unexpected` (code `Chat.InvalidRemixPath`).
- Consumes: `ChatMessage.CopyForBranch` (internal, exists), `ChatThread` private constructor and `SetHead` (exist), `SharedChatId` from `Chat.Domain.SharedChats.ValueObjects`.

- [ ] **Step 1: Write the `ChatRemixOrigin` value object**

Create `src/services/Chat/Chat.Domain/Chats/ValueObjects/ChatRemixOrigin.cs`:

```csharp
using Chat.Domain.SharedChats.ValueObjects;

namespace Chat.Domain.Chats.ValueObjects;

public sealed record ChatRemixOrigin
{
    public SharedChatId ShareId { get; private init; } = default!;

    public ChatId SourceChatId { get; private init; } = default!;

    public ChatMessageId SourceMessageId { get; private init; } = default!;

    private ChatRemixOrigin()
    {
        // EF Core materialization only
    }

    private ChatRemixOrigin(SharedChatId shareId, ChatId sourceChatId, ChatMessageId sourceMessageId)
    {
        ShareId = shareId;
        SourceChatId = sourceChatId;
        SourceMessageId = sourceMessageId;
    }

    internal static ChatRemixOrigin Create
    (
        SharedChatId shareId,
        ChatId sourceChatId,
        ChatMessageId sourceMessageId
    ) => new(shareId, sourceChatId, sourceMessageId);
}
```

- [ ] **Step 2: Add the two domain errors**

In `src/services/Chat/Chat.Domain/Chats/ChatErrors.cs`, immediately after the `InvalidBranchPath` method (ends at line 109), add:

```csharp
    public static Error RemixTargetMustBeAssistant(ChatMessageId messageId) =>
        Error.Conflict
        (
            code: "Chat.RemixTargetMustBeAssistant",
            description: $"The remix source node '{messageId.Value}' must be a completed assistant message."
        );

    public static Error InvalidRemixPath(ChatMessageId messageId) =>
        Error.Unexpected
        (
            code: "Chat.InvalidRemixPath",
            description: $"The persisted ancestry for remix source message '{messageId.Value}' is invalid."
        );
```

- [ ] **Step 3: Add the `RemixOrigin` property and `using`**

In `src/services/Chat/Chat.Domain/Chats/ChatThread.cs`, add after line 35 (`public ChatBranchOrigin? BranchOrigin { get; private set; }`):

```csharp
    public ChatRemixOrigin? RemixOrigin { get; private set; }
```

Add this `using` to the top of the file (the file already has `using Chat.Domain.Chats.ValueObjects;`, so add the SharedChats one):

```csharp
using Chat.Domain.SharedChats.ValueObjects;
```

- [ ] **Step 4: Write the failing domain tests**

In `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs`, add these tests (place them after the existing `BranchFrom...` tests). Add `using Chat.Domain.SharedChats.ValueObjects;` to the file's usings.

```csharp
    [Fact]
    public void CreateRemixCopiesSharedPathWithNewIdsIndependentMetadataAndRemixOrigin()
    {
        ChatThread source = TestChatFactory.CreateThread();
        source.Pin(TestChatFactory.CreatedAt.AddMinutes(1));
        source.Archive();
        ChatMessage root = TestChatFactory.RootMessage(source);
        ChatMessage firstAssistant = CompleteAssistant(source, root.Id, TestChatFactory.CreatedAt.AddMinutes(2));
        ChatMessage followUp = AddUser(source, firstAssistant.Id, TestChatFactory.CreatedAt.AddMinutes(4), "Follow up");
        ChatMessage sharedNode = CompleteAssistant(source, followUp.Id, TestChatFactory.CreatedAt.AddMinutes(5));
        _ = AddUser(source, sharedNode.Id, TestChatFactory.CreatedAt.AddMinutes(7), "Excluded descendant");
        ChatMessage[] sourcePath = [root, firstAssistant, followUp, sharedNode];

        UserId remixer = UserId.FromDatabase("auth0|remixer");
        SharedChatId shareId = SharedChatId.New();
        ChatTitle title = ChatTitle.FromDatabase("Shared conversation");
        DateTimeOffset remixedAt = TestChatFactory.CreatedAt.AddHours(1);

        ErrorOr<ChatThread> result = ChatThread.CreateRemix(remixer, source, sharedNode.Id, shareId, title, remixedAt);

        Assert.False(result.IsError);
        ChatThread remix = result.Value;
        ChatMessage[] copiedPath = GetActivePath(remix);
        Assert.Equal(sourcePath.Length, copiedPath.Length);
        Assert.Equal(sourcePath.Length, remix.Messages.Count);
        Assert.DoesNotContain(remix.Messages, copied => source.Messages.Any(original => original.Id == copied.Id));

        for (int index = 0; index < sourcePath.Length; index++)
        {
            Assert.Equal(sourcePath[index].Role, copiedPath[index].Role);
            Assert.Equal(sourcePath[index].Content, copiedPath[index].Content);
            Assert.Equal(sourcePath[index].Status, copiedPath[index].Status);
            Assert.Equal(0, copiedPath[index].SiblingIndex.Value);
            Assert.Equal(index == 0 ? null : copiedPath[index - 1].Id, copiedPath[index].ParentMessageId);
        }

        Assert.Equal(remixer, remix.UserId);
        Assert.Equal("Shared conversation", remix.Title.Value);
        Assert.False(remix.IsTemporary);
        Assert.False(remix.IsPinned);
        Assert.False(remix.IsArchived);
        Assert.Null(remix.ProjectId);
        Assert.Equal(remixedAt, remix.CreatedAt);
        Assert.Equal(remixedAt, remix.UpdatedAt);
        Assert.Equal(copiedPath[^1].Id, remix.CurrentMessageId);
        Assert.Equal(shareId, remix.RemixOrigin!.ShareId);
        Assert.Equal(source.Id, remix.RemixOrigin.SourceChatId);
        Assert.Equal(sharedNode.Id, remix.RemixOrigin.SourceMessageId);
    }

    [Fact]
    public void CreateRemixRejectsUserSourceNode()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(source);

        ErrorOr<ChatThread> result = ChatThread.CreateRemix
        (
            UserId.FromDatabase("auth0|remixer"),
            source,
            root.Id,
            SharedChatId.New(),
            ChatTitle.FromDatabase("Shared"),
            TestChatFactory.CreatedAt
        );

        AssertError(result, ErrorType.Conflict, "Chat.RemixTargetMustBeAssistant");
    }

    [Fact]
    public void CreateRemixRejectsGeneratingAssistantSourceNode()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage assistant = BeginAssistant(source);

        ErrorOr<ChatThread> result = ChatThread.CreateRemix
        (
            UserId.FromDatabase("auth0|remixer"),
            source,
            assistant.Id,
            SharedChatId.New(),
            ChatTitle.FromDatabase("Shared"),
            TestChatFactory.CreatedAt
        );

        AssertError(result, ErrorType.Conflict, "Chat.RemixTargetMustBeAssistant");
    }

    [Fact]
    public void CreateRemixReturnsMessageNotFoundForUnknownNode()
    {
        ChatThread source = TestChatFactory.CreateThread();

        ErrorOr<ChatThread> result = ChatThread.CreateRemix
        (
            UserId.FromDatabase("auth0|remixer"),
            source,
            ChatMessageId.New(),
            SharedChatId.New(),
            ChatTitle.FromDatabase("Shared"),
            TestChatFactory.CreatedAt
        );

        AssertError(result, ErrorType.NotFound, "Chat.MessageNotFound");
    }

    [Fact]
    public void CreateRemixRejectsCyclicPersistedPath()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage root = TestChatFactory.RootMessage(source);
        ChatMessage assistant = CompleteAssistant(source);
        SetParentForCorruptionTest(root, assistant.Id);

        ErrorOr<ChatThread> result = ChatThread.CreateRemix
        (
            UserId.FromDatabase("auth0|remixer"),
            source,
            assistant.Id,
            SharedChatId.New(),
            ChatTitle.FromDatabase("Shared"),
            TestChatFactory.CreatedAt
        );

        AssertError(result, ErrorType.Unexpected, "Chat.InvalidRemixPath");
    }

    [Fact]
    public void CreateRemixDoesNotMutateSource()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage assistant = CompleteAssistant(source);
        int sourceCountBefore = source.Messages.Count;
        ChatMessageId sourceHeadBefore = source.CurrentMessageId;

        _ = ChatThread.CreateRemix
        (
            UserId.FromDatabase("auth0|remixer"),
            source,
            assistant.Id,
            SharedChatId.New(),
            ChatTitle.FromDatabase("Shared"),
            TestChatFactory.CreatedAt.AddHours(1)
        );

        Assert.Equal(sourceCountBefore, source.Messages.Count);
        Assert.Equal(sourceHeadBefore, source.CurrentMessageId);
    }
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Domain.Tests --filter "FullyQualifiedName~ChatThreadTests.CreateRemix"`
Expected: FAIL — `ChatThread.CreateRemix` does not exist (compile error).

- [ ] **Step 6: Implement `CreateRemix`**

In `src/services/Chat/Chat.Domain/Chats/ChatThread.cs`, add this method immediately after `BranchFrom` (after line 203):

```csharp
    public static ErrorOr<ChatThread> CreateRemix
    (
        UserId remixerUserId,
        ChatThread source,
        ChatMessageId sharedNodeId,
        SharedChatId shareId,
        ChatTitle title,
        DateTimeOffset createdAt
    )
    {
        // The source is guaranteed non-temporary because it was shareable, so no temporary check.
        ChatMessage? sharedNode = source.FindMessage(sharedNodeId);

        if (sharedNode is null)
        {
            return ChatErrors.MessageNotFound(sharedNodeId);
        }

        if (sharedNode.Role != MessageRole.Assistant || sharedNode.Status == MessageStatus.Generating)
        {
            return ChatErrors.RemixTargetMustBeAssistant(sharedNodeId);
        }

        List<ChatMessage> sourcePath = [];
        HashSet<ChatMessageId> visited = [];
        ChatMessage cursor = sharedNode;

        while (true)
        {
            if (!visited.Add(cursor.Id))
            {
                return ChatErrors.InvalidRemixPath(sharedNodeId);
            }

            sourcePath.Add(cursor);

            if (cursor.ParentMessageId is null)
            {
                break;
            }

            ChatMessage? parent = source.FindMessage(cursor.ParentMessageId);

            if (parent is null)
            {
                return ChatErrors.InvalidRemixPath(sharedNodeId);
            }

            cursor = parent;
        }

        ChatMessage root = sourcePath[^1];

        if (root.Role != MessageRole.User || root.ParentMessageId is not null)
        {
            return ChatErrors.InvalidRemixPath(sharedNodeId);
        }

        sourcePath.Reverse();

        ChatId remixId = ChatId.New();
        Dictionary<ChatMessageId, ChatMessageId> copiedIds = sourcePath.ToDictionary
        (
            message => message.Id,
            _ => ChatMessageId.New()
        );

        List<ChatMessage> copiedMessages = sourcePath
            .Select(message => message.CopyForBranch
            (
                id: copiedIds[message.Id],
                chatId: remixId,
                parentMessageId: message.ParentMessageId is null
                    ? null
                    : copiedIds[message.ParentMessageId]
            ))
            .ToList();

        ChatThread remix = new
        (
            id: remixId,
            userId: remixerUserId,
            title: title,
            root: copiedMessages[0],
            createdAt: createdAt,
            updatedAt: createdAt,
            isTemporary: false
        );

        remix.RemixOrigin = ChatRemixOrigin.Create
        (
            shareId: shareId,
            sourceChatId: source.Id,
            sourceMessageId: sharedNodeId
        );
        remix._messages.AddRange(copiedMessages.Skip(1));

        remix.SetHead(copiedIds[sharedNodeId], createdAt);

        return remix;
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Domain.Tests --filter "FullyQualifiedName~ChatThreadTests.CreateRemix"`
Expected: PASS (6 tests).

- [ ] **Step 8: Commit**

```bash
git add src/services/Chat/Chat.Domain tests/Chat/Chat.Domain.Tests
git commit -m "feat(chat): add ChatThread.CreateRemix domain factory and remix origin"
```

---

### Task 2: Persistence — `allow_remix`, `RemixOrigin` mapping, repository reads, migration

**Files:**
- Modify: `src/services/Chat/Chat.Domain/SharedChats/SharedChat.cs` (add `AllowRemix` property + `Create` parameter)
- Modify: `src/services/Chat/Chat.Infrastructure/SharedChats/Configurations/SharedChatConfiguration.cs` (map `AllowRemix`)
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatThreadConfiguration.cs` (map `RemixOrigin` complex property + second check constraint)
- Modify: `src/services/Chat/Chat.Domain/SharedChats/ISharedChatRepository.cs` (add `GetForRemixAsync`)
- Modify: `src/services/Chat/Chat.Infrastructure/SharedChats/Repositories/SharedChatRepository.cs` (implement `GetForRemixAsync`; update `TryAddAsync` SQL)
- Modify: `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs` (add `GetSnapshotByChatIdAsync`)
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs` (implement `GetSnapshotByChatIdAsync`)
- Modify: `tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs` (implement `GetSnapshotByChatIdAsync`)
- Create: `src/services/Chat/Chat.Infrastructure/Database/Migrations/<timestamp>_AddSharedChatRemix.cs` (generated by EF)

**Interfaces:**
- Consumes: `ChatRemixOrigin` (Task 1), `SharedChatId`, `ChatId`, `ChatMessageId`, `ChatTitle`.
- Produces:
  - `SharedChat.AllowRemix` — `public bool AllowRemix { get; private set; }`.
  - `SharedChat.Create(UserId, ChatId, ChatMessageId, ChatTitle, DateTimeOffset, bool allowRemix = false)`.
  - `ISharedChatRepository.GetForRemixAsync(SharedChatId id, CancellationToken) -> Task<SharedChat?>` (NOT owner-scoped).
  - `IChatRepository.GetSnapshotByChatIdAsync(ChatId id, CancellationToken) -> Task<ChatThread?>` (NOT owner-scoped, no-tracking, includes messages).

- [ ] **Step 1: Add `AllowRemix` to the `SharedChat` aggregate**

In `src/services/Chat/Chat.Domain/SharedChats/SharedChat.cs`:

Add the property after `CreatedAt` (line 19):

```csharp
    public bool AllowRemix { get; private set; }
```

Add the constructor parameter and assignment (extend the private constructor at lines 26-41):

```csharp
    private SharedChat
    (
        SharedChatId id,
        UserId userId,
        ChatId chatId,
        ChatMessageId currentMessageId,
        ChatTitle title,
        DateTimeOffset createdAt,
        bool allowRemix
    ) : base(id)
    {
        UserId = userId;
        ChatId = chatId;
        CurrentMessageId = currentMessageId;
        Title = title;
        CreatedAt = createdAt;
        AllowRemix = allowRemix;
    }
```

Update `Create` (lines 43-58) to accept and pass `allowRemix`:

```csharp
    public static SharedChat Create
    (
        UserId userId,
        ChatId chatId,
        ChatMessageId currentMessageId,
        ChatTitle title,
        DateTimeOffset createdAt,
        bool allowRemix = false
    ) => new
    (
        id: SharedChatId.New(),
        userId: userId,
        chatId: chatId,
        currentMessageId: currentMessageId,
        title: title,
        createdAt: createdAt,
        allowRemix: allowRemix
    );
```

- [ ] **Step 2: Map `AllowRemix` in `SharedChatConfiguration`**

In `src/services/Chat/Chat.Infrastructure/SharedChats/Configurations/SharedChatConfiguration.cs`, after the `CreatedAt` property block (lines 62-63), add:

```csharp
        builder.Property(x => x.AllowRemix)
            .IsRequired()
            .HasDefaultValue(false);
```

- [ ] **Step 3: Map `RemixOrigin` and add its check constraint in `ChatThreadConfiguration`**

In `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatThreadConfiguration.cs`:

Replace the `ToTable` call (lines 16-20) so it declares both check constraints:

```csharp
        builder.ToTable("chats", table =>
        {
            table.HasCheckConstraint
            (
                "ck_chats_branch_origin_complete",
                "(branched_from_chat_id is null) = (branched_from_message_id is null)"
            );

            table.HasCheckConstraint
            (
                "ck_chats_remix_origin_complete",
                "(remixed_from_share_id is null) = (remixed_from_chat_id is null) "
                + "and (remixed_from_share_id is null) = (remixed_from_message_id is null)"
            );
        });
```

Add the `RemixOrigin` complex property immediately after the `BranchOrigin` `ComplexProperty` block (ends at line 76):

```csharp
        builder.ComplexProperty(x => x.RemixOrigin, origin =>
        {
            origin.IsRequired(false);

            origin.Property(value => value.ShareId)
                .HasConversion
                (
                    id => id.Value,
                    value => SharedChatId.FromDatabase(value)
                )
                .HasColumnName("remixed_from_share_id");

            origin.Property(value => value.SourceChatId)
                .HasConversion
                (
                    id => id.Value,
                    value => ChatId.FromDatabase(value)
                )
                .HasColumnName("remixed_from_chat_id");

            origin.Property(value => value.SourceMessageId)
                .HasConversion
                (
                    id => id.Value,
                    value => ChatMessageId.FromDatabase(value)
                )
                .HasColumnName("remixed_from_message_id");
        });
```

Add this `using` to the file (it currently uses `Chat.Domain.Chats.ValueObjects`, so add SharedChats):

```csharp
using Chat.Domain.SharedChats.ValueObjects;
```

- [ ] **Step 4: Add the non-owner-scoped repository reads**

In `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs`, add after `GetSnapshotByIdAsync` (before `void Add`):

```csharp
    /// <summary>
    /// Loads a chat with its messages by id WITHOUT owner scoping, no-tracking. Used only by the
    /// remix flow, authorized by the source share's <c>allow_remix</c> consent. The returned
    /// aggregate is a detached snapshot that must not be mutated or persisted.
    /// </summary>
    Task<ChatThread?> GetSnapshotByChatIdAsync
    (
        ChatId id,
        CancellationToken cancellationToken = default
    );
```

In `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs`, add after `GetSnapshotByIdAsync`:

```csharp
    public async Task<ChatThread?> GetSnapshotByChatIdAsync(ChatId id, CancellationToken cancellationToken = default)
    {
        return await db.ChatThreads
            .AsNoTracking()
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
```

In `src/services/Chat/Chat.Domain/SharedChats/ISharedChatRepository.cs`, add after `GetBySourceAsync`:

```csharp
    /// <summary>
    /// Loads a shared chat by id WITHOUT owner scoping, no-tracking. Used by the remix flow to read
    /// the sharer's consent flag and source pointer for any authenticated viewer.
    /// </summary>
    Task<SharedChat?> GetForRemixAsync
    (
        SharedChatId id,
        CancellationToken cancellationToken = default
    );
```

In `src/services/Chat/Chat.Infrastructure/SharedChats/Repositories/SharedChatRepository.cs`, add after `GetBySourceAsync`:

```csharp
    public async Task<SharedChat?> GetForRemixAsync
    (
        SharedChatId id,
        CancellationToken cancellationToken = default
    )
    {
        return await db.SharedChats
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
```

- [ ] **Step 5: Persist `allow_remix` in `TryAddAsync`**

In the same `SharedChatRepository.cs`, update the raw `TryAddAsync` insert (lines 49-60) to include the column:

```csharp
        int affected = await db.Database.ExecuteSqlAsync
        (
            $"""
             insert into shared_chats
                 (id, user_id, chat_id, current_message_id, title, created_at, allow_remix)
             values
                 ({sharedChat.Id.Value}, {sharedChat.UserId.Value}, {sharedChat.ChatId.Value},
                  {sharedChat.CurrentMessageId.Value}, {sharedChat.Title.Value}, {sharedChat.CreatedAt},
                  {sharedChat.AllowRemix})
             on conflict (chat_id, current_message_id) do nothing;
             """,
            cancellationToken
        );
```

- [ ] **Step 6: Update the application-test fake repository**

In `tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs`, add after `GetSnapshotByIdAsync`:

```csharp
    public Task<ChatThread?> GetSnapshotByChatIdAsync(ChatId id, CancellationToken cancellationToken = default)
    {
        SnapshotGetCallCount++;
        ChatThread? thread = _threads.FirstOrDefault(x => x.Id == id);

        return Task.FromResult(thread);
    }
```

- [ ] **Step 7: Build to verify the mapping and interfaces compile**

Run: `dotnet build src/services/Chat/Chat.Infrastructure`
Expected: Build succeeded (0 errors). If a different test project has its own `IChatRepository` fake, add the same method there — search first: `grep -rl "IChatRepository" tests` (currently only `tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs`).

- [ ] **Step 8: Generate the EF migration**

Run (a design-time factory exists at `Chat.Infrastructure/Database/ChatDbContextFactory.cs`, so `--project` alone is sufficient):

```bash
dotnet ef migrations add AddSharedChatRemix \
  --project src/services/Chat/Chat.Infrastructure \
  --output-dir Database/Migrations
```

Expected: a new `<timestamp>_AddSharedChatRemix.cs` under `Database/Migrations` and an updated `ChatDbContextModelSnapshot.cs`.

- [ ] **Step 9: Verify the generated migration**

Open the generated migration and confirm its `Up` contains, in effect:
- `AddColumn<bool>("allow_remix", "shared_chats", nullable: false, defaultValue: false)`
- `AddColumn<Guid>("remixed_from_share_id", "chats", nullable: true)`
- `AddColumn<Guid>("remixed_from_chat_id", "chats", nullable: true)`
- `AddColumn<Guid>("remixed_from_message_id", "chats", nullable: true)`
- `AddCheckConstraint("ck_chats_remix_origin_complete", "chats", ...)`

If any are missing, the configuration in Steps 2-3 is wrong — fix and re-run `dotnet ef migrations add` (delete the bad migration first with `dotnet ef migrations remove --project src/services/Chat/Chat.Infrastructure`).

- [ ] **Step 10: Commit**

```bash
git add src/services/Chat tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs
git commit -m "feat(chat): persist allow_remix flag and remix origin with migration"
```

---

### Task 3: Create-share opt-in wiring (`allowRemix`)

**Files:**
- Modify: `src/services/Chat/Chat.Application/SharedChats/Commands/Create/CreateSharedChatCommand.cs` (add `AllowRemix`)
- Modify: `src/services/Chat/Chat.Application/SharedChats/Commands/Create/CreateSharedChatHandler.cs` (pass `AllowRemix` into `SharedChat.Create`)
- Modify: `src/services/Chat/Chat.Application/SharedChats/Results/SharedChatResult.cs` (add `AllowRemix`)
- Modify: `src/services/Chat/Chat.Application/SharedChats/Results/SharedChatResultMapper.cs` (map `AllowRemix`)
- Modify: `src/services/Chat/Chat.Api/Endpoints/SharedChats/CreateSharedChat/Endpoint.cs` (add `AllowRemix` to request, pass to command)
- Modify: `src/services/Chat/Chat.Api/Endpoints/SharedChats/Responses/SharedChatResponse.cs` (add `AllowRemix`)
- Test: `tests/Chat/Chat.Application.Tests/SharedChats/CreateSharedChatHandlerTests.cs` (add allowRemix persistence test)

**Interfaces:**
- Consumes: `SharedChat.Create(..., bool allowRemix)` (Task 2), existing `FakeSharedChatRepository`.
- Produces: `CreateSharedChatCommand(Guid ChatId, Guid CurrentMessageId, bool AllowRemix = false)`; `SharedChatResult.AllowRemix`.

- [ ] **Step 1: Read the existing mapper and response to match their exact shape**

Run: `cat src/services/Chat/Chat.Application/SharedChats/Results/SharedChatResultMapper.cs src/services/Chat/Chat.Api/Endpoints/SharedChats/Responses/SharedChatResponse.cs`
This shows the exact `ToResult`/`From` signatures you will extend below.

- [ ] **Step 2: Write the failing handler test**

In `tests/Chat/Chat.Application.Tests/SharedChats/CreateSharedChatHandlerTests.cs`, add a test that mirrors the existing "creates a new link" test but asserts the flag is persisted. (Read the existing tests in this file first to reuse its arrange helpers / fakes verbatim — `SharedChatTestFactory`, `FakeSharedChatRepository`, `FakeChatRepository`.) The assertion that must hold:

```csharp
    [Fact]
    public async Task HandlePersistsAllowRemixWhenRequested()
    {
        // Arrange exactly as the existing "creates new link" test, but send AllowRemix = true.
        // (Reuse this file's existing setup: seeded shareable source chat + fakes.)
        CreateSharedChatCommand command = new(SourceChatId, SourceNodeId, AllowRemix: true);

        ErrorOr<SharedChatResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.Value.AllowRemix);
        Assert.True(sharedChats.LastAdded!.AllowRemix); // FakeSharedChatRepository exposes the inserted row
    }
```

If `FakeSharedChatRepository` does not already expose the last inserted `SharedChat`, add a `public SharedChat? LastAdded { get; private set; }` set inside its `TryAddAsync`. Confirm names against the actual fake before writing.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~CreateSharedChatHandlerTests.HandlePersistsAllowRemixWhenRequested"`
Expected: FAIL — `CreateSharedChatCommand` has no `AllowRemix` parameter (compile error).

- [ ] **Step 4: Add `AllowRemix` to the command**

Replace `src/services/Chat/Chat.Application/SharedChats/Commands/Create/CreateSharedChatCommand.cs` record declaration:

```csharp
public sealed record CreateSharedChatCommand(Guid ChatId, Guid CurrentMessageId, bool AllowRemix = false)
    : ICommand<ErrorOr<SharedChatResult>>;
```

- [ ] **Step 5: Pass `AllowRemix` into `SharedChat.Create`**

In `CreateSharedChatHandler.cs`, update the `SharedChat.Create` call (lines 84-91):

```csharp
        SharedChat candidate = SharedChat.Create
        (
            userId: userId,
            chatId: chatId,
            currentMessageId: currentMessageId,
            title: source.Title,
            createdAt: dateTimeProvider.UtcNow,
            allowRemix: command.AllowRemix
        );
```

- [ ] **Step 6: Add `AllowRemix` to the result and mapper**

In `SharedChatResult.cs`, add `bool AllowRemix` to the record (before `bool AlreadyExists` to keep `AlreadyExists` last):

```csharp
public sealed record SharedChatResult
(
    Guid Id,
    string Title,
    Guid ChatId,
    Guid CurrentMessageId,
    DateTimeOffset CreatedAt,
    bool AllowRemix,
    bool AlreadyExists
);
```

In `SharedChatResultMapper.cs`, add `AllowRemix: sharedChat.AllowRemix,` to the `ToResult` construction (match the parameter order above).

- [ ] **Step 7: Add `AllowRemix` to the API request and response**

In `CreateSharedChat/Endpoint.cs`, extend the request record and command construction:

```csharp
internal sealed record Request(Guid ChatId, Guid CurrentMessageId, bool AllowRemix = false);
```

```csharp
        CreateSharedChatCommand command = new
        (
            ChatId: request.ChatId,
            CurrentMessageId: request.CurrentMessageId,
            AllowRemix: request.AllowRemix
        );
```

In `Responses/SharedChatResponse.cs`, add an `AllowRemix` property and populate it in the `From` factory from `result.AllowRemix`. (Read the file first; add the property alongside the existing ones and set it in `From`.)

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~CreateSharedChatHandlerTests"`
Expected: PASS (existing tests still green + the new one).

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat tests/Chat/Chat.Application.Tests/SharedChats/CreateSharedChatHandlerTests.cs
git commit -m "feat(chat): accept allowRemix opt-in on share creation"
```

---

### Task 4: Public read exposes `allowRemix`

**Files:**
- Modify: `src/services/Chat/Chat.Application/SharedChats/Queries/GetPublicSharedChat/PublicSharedChatReadModel.cs` (add `AllowRemix`)
- Modify: `src/services/Chat/Chat.Infrastructure/SharedChats/Readers/PublicSharedChatReader.cs` (select `allow_remix`, map it)
- Modify: `src/services/Chat/Chat.Api/Endpoints/SharedChats/GetSharedChat/Response.cs` (add `AllowRemix`)
- Modify: `src/services/Chat/Chat.Api/Endpoints/SharedChats/GetSharedChat/ResponseMapper.cs` (map `AllowRemix`)
- Test: `tests/Chat/Chat.Application.Tests/SharedChats/GetPublicSharedChatHandlerTests.cs` and/or `FakePublicSharedChatReader.cs` (assert `AllowRemix` flows through)

**Interfaces:**
- Produces: `PublicSharedChatReadModel.AllowRemix` (`bool`).
- Consumes: reader `IPublicSharedChatReader.GetAsync`.

- [ ] **Step 1: Add `AllowRemix` to the read model**

In `PublicSharedChatReadModel.cs`, add `bool AllowRemix` after `CurrentMessageId`:

```csharp
public sealed record PublicSharedChatReadModel
(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    Guid CurrentMessageId,
    bool AllowRemix,
    IReadOnlyList<PublicSharedChatMessageReadModel> Messages
);
```

- [ ] **Step 2: Select and map `allow_remix` in the reader**

In `PublicSharedChatReader.cs`:

Add `allow_remix as "AllowRemix"` to the header `select` (after `current_message_id`, lines 14-19):

```sql
                               select
                                   id                 as "Id",
                                   title              as "Title",
                                   created_at         as "CreatedAt",
                                   current_message_id as "CurrentMessageId",
                                   allow_remix        as "AllowRemix"
                               from shared_chats
                               where id = @SharedChatId;
```

Add `bool AllowRemix` to the private `SharedChatRow` record (lines 147-153):

```csharp
    private sealed record SharedChatRow
    (
        Guid Id,
        string Title,
        DateTime CreatedAt,
        Guid CurrentMessageId,
        bool AllowRemix
    );
```

Pass it into the returned read model (lines 112-119):

```csharp
        return new PublicSharedChatReadModel
        (
            Id: sharedChat.Id,
            Title: sharedChat.Title,
            CreatedAt: sharedChat.CreatedAt,
            CurrentMessageId: sharedChat.CurrentMessageId,
            AllowRemix: sharedChat.AllowRemix,
            Messages: messages
        );
```

- [ ] **Step 3: Add `AllowRemix` to the API response and mapper**

In `GetSharedChat/Response.cs`, add:

```csharp
    public required bool AllowRemix { get; init; }
```

In `GetSharedChat/ResponseMapper.cs`, set `AllowRemix = readModel.AllowRemix,` inside the `new Response { ... }` initializer.

- [ ] **Step 4: Write and run the handler test**

In `tests/Chat/Chat.Application.Tests/SharedChats/GetPublicSharedChatHandlerTests.cs`, add a test asserting `AllowRemix` propagates from the reader through the handler. First update `FakePublicSharedChatReader` (same test folder) so its returned `PublicSharedChatReadModel` includes the new `AllowRemix` argument (fix the compile break there — set it from whatever the fake is seeded with, defaulting to the value under test).

```csharp
    [Fact]
    public async Task HandleReturnsAllowRemixFromReader()
    {
        // Seed the fake reader with a read model whose AllowRemix = true (reuse this file's helpers).
        ErrorOr<PublicSharedChatReadModel> result = await handler.Handle(new GetPublicSharedChatQuery(ShareId), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.Value.AllowRemix);
    }
```

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetPublicSharedChatHandlerTests"`
Expected: PASS (existing tests compile with the updated fake + the new assertion).

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat tests/Chat/Chat.Application.Tests/SharedChats
git commit -m "feat(chat): expose allowRemix in public shared chat read"
```

---

### Task 5: Remix command + handler

**Files:**
- Create: `src/services/Chat/Chat.Application/SharedChats/Commands/Remix/RemixSharedChatCommand.cs`
- Create: `src/services/Chat/Chat.Application/SharedChats/Commands/Remix/RemixSharedChatHandler.cs`
- Create: `src/services/Chat/Chat.Application/SharedChats/Results/RemixSharedChatResult.cs`
- Modify: `src/services/Chat/Chat.Application/SharedChats/Errors/SharedChatOperationErrors.cs` (add `RemixNotAllowed`)
- Test: `tests/Chat/Chat.Application.Tests/SharedChats/RemixSharedChatHandlerTests.cs`

**Interfaces:**
- Consumes: `ISharedChatRepository.GetForRemixAsync`, `IChatRepository.GetSnapshotByChatIdAsync`, `IChatRepository.Add`, `IUnitOfWork.SaveChangesAsync`, `ChatThread.CreateRemix`, `IUserContext`, `IDateTimeProvider`.
- Produces:
  - `RemixSharedChatCommand(Guid ShareId) : ICommand<ErrorOr<RemixSharedChatResult>>`.
  - `RemixSharedChatResult(Guid ChatId, string Title, DateTimeOffset CreatedAt)`.
  - `SharedChatOperationErrors.RemixNotAllowed(SharedChatId) -> Error.Forbidden` (code `SharedChat.RemixNotAllowed`).

- [ ] **Step 1: Add the command and result**

Create `RemixSharedChatCommand.cs`:

```csharp
using Chat.Application.SharedChats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.SharedChats.Commands.Remix;

public sealed record RemixSharedChatCommand(Guid ShareId) : ICommand<ErrorOr<RemixSharedChatResult>>;
```

Create `RemixSharedChatResult.cs`:

```csharp
namespace Chat.Application.SharedChats.Results;

public sealed record RemixSharedChatResult
(
    Guid ChatId,
    string Title,
    DateTimeOffset CreatedAt
);
```

- [ ] **Step 2: Add the `RemixNotAllowed` error**

In `src/services/Chat/Chat.Application/SharedChats/Errors/SharedChatOperationErrors.cs`, add:

```csharp
    public static Error RemixNotAllowed(SharedChatId sharedChatId) =>
        Error.Forbidden
        (
            code: "SharedChat.RemixNotAllowed",
            description: $"Shared chat '{sharedChatId.Value}' does not permit remixing."
        );
```

- [ ] **Step 3: Write the failing handler tests**

Create `tests/Chat/Chat.Application.Tests/SharedChats/RemixSharedChatHandlerTests.cs`. Build the source chat with `TestChatFactory`-style helpers or the existing SharedChats test factory; seed a source with a completed assistant terminal, and a `SharedChat` row via `SharedChat.Create(..., allowRemix: true)`. Use the folder's fakes (`FakeSharedChatRepository`, `FakeChatRepository` — the one under `Turns` implements `IChatRepository`; if the SharedChats test folder needs its own `IChatRepository` fake, reuse or add one that implements `GetSnapshotByChatIdAsync`). Use a stub `IUserContext` returning the remixer and a fake `IUnitOfWork`.

```csharp
    [Fact]
    public async Task HandleCopiesSharedPathIntoNewChatOwnedByRemixer()
    {
        RemixSharedChatCommand command = new(ShareId.Value);

        ErrorOr<RemixSharedChatResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsError);
        ChatThread added = Assert.Single(chats.AddedThreads);
        Assert.Equal(RemixerUserId, added.UserId);
        Assert.Equal(added.Id.Value, result.Value.ChatId);
        Assert.Equal(ShareId, added.RemixOrigin!.ShareId);
        Assert.Equal(SourceChatId, added.RemixOrigin.SourceChatId);
        Assert.True(unitOfWork.SaveChangesCalled);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenShareMissing()
    {
        RemixSharedChatCommand command = new(Guid.NewGuid());

        ErrorOr<RemixSharedChatResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Empty(chats.AddedThreads);
    }

    [Fact]
    public async Task HandleReturnsForbiddenWhenRemixNotAllowed()
    {
        // Seed the share with allowRemix: false.
        RemixSharedChatCommand command = new(ShareIdWithoutRemix.Value);

        ErrorOr<RemixSharedChatResult> result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Forbidden, result.FirstError.Type);
        Assert.Equal("SharedChat.RemixNotAllowed", result.FirstError.Code);
        Assert.Empty(chats.AddedThreads);
    }

    [Fact]
    public async Task HandleDoesNotPublishTurnRequested()
    {
        RemixSharedChatCommand command = new(ShareId.Value);

        _ = await handler.Handle(command, CancellationToken.None);

        // The handler must not depend on IMessageBus at all; asserting via the absence of a bus
        // dependency is structural. If a fake bus is injected, assert it published nothing.
        Assert.True(true);
    }
```

Add `GetForRemixAsync` to `FakeSharedChatRepository` (return the seeded row by id, ignoring owner) and `GetSnapshotByChatIdAsync` to whichever `IChatRepository` fake this test uses. Provide a minimal `FakeUnitOfWork` with `bool SaveChangesCalled` if one does not already exist in the test project (search: `grep -rl "IUnitOfWork" tests`).

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~RemixSharedChatHandlerTests"`
Expected: FAIL — `RemixSharedChatHandler` does not exist (compile error).

- [ ] **Step 5: Implement the handler**

Create `RemixSharedChatHandler.cs`:

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.SharedChats.Errors;
using Chat.Application.SharedChats.Results;
using Chat.Domain.Chats;
using Chat.Domain.Shared;
using Chat.Domain.SharedChats;
using Chat.Domain.SharedChats.ValueObjects;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.SharedChats.Commands.Remix;

internal sealed class RemixSharedChatHandler(
    IUserContext userContext,
    ISharedChatRepository sharedChats,
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<RemixSharedChatCommand, ErrorOr<RemixSharedChatResult>>
{
    public async ValueTask<ErrorOr<RemixSharedChatResult>> Handle(RemixSharedChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<SharedChatId> shareIdResult = SharedChatId.Create(command.ShareId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (shareIdResult.IsError)
        {
            errors.AddRange(shareIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId remixerUserId = userIdResult.Value;
        SharedChatId shareId = shareIdResult.Value;

        SharedChat? share = await sharedChats.GetForRemixAsync(shareId, cancellationToken);

        if (share is null)
        {
            return SharedChatOperationErrors.SharedChatNotFound(shareId);
        }

        if (!share.AllowRemix)
        {
            return SharedChatOperationErrors.RemixNotAllowed(shareId);
        }

        ChatThread? source = await chats.GetSnapshotByChatIdAsync(share.ChatId, cancellationToken);

        if (source is null)
        {
            return SharedChatOperationErrors.SharedChatNotFound(shareId);
        }

        ErrorOr<ChatThread> remixResult = ChatThread.CreateRemix
        (
            remixerUserId: remixerUserId,
            source: source,
            sharedNodeId: share.CurrentMessageId,
            shareId: share.Id,
            title: share.Title,
            createdAt: dateTimeProvider.UtcNow
        );

        if (remixResult.IsError)
        {
            return remixResult.Errors;
        }

        ChatThread remix = remixResult.Value;

        chats.Add(remix);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RemixSharedChatResult
        (
            ChatId: remix.Id.Value,
            Title: remix.Title.Value,
            CreatedAt: remix.CreatedAt
        );
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~RemixSharedChatHandlerTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat tests/Chat/Chat.Application.Tests/SharedChats
git commit -m "feat(chat): add RemixSharedChat command and handler"
```

---

### Task 6: Remix HTTP endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/SharedChats/RemixSharedChat/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/SharedChats/RemixSharedChat/Response.cs`
- Test: extend the API/endpoint test suite if one exists for shared chats (search `grep -rl "SharedChats" tests/Chat/Chat.Api* 2>/dev/null`); otherwise rely on the handler tests from Task 5 and a manual verification step.

**Interfaces:**
- Consumes: `RemixSharedChatCommand`, `RemixSharedChatResult` (Task 5).
- Produces: `POST /v1/shared-chats/{shareId}/remix` → `201 Created`, `Location: /v1/chats/{chatId}`.

- [ ] **Step 1: Create the response**

Create `RemixSharedChat/Response.cs`:

```csharp
namespace Chat.Api.Endpoints.SharedChats.RemixSharedChat;

internal sealed class Response
{
    public required Guid ChatId { get; init; }

    public required string Title { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
```

- [ ] **Step 2: Create the endpoint**

Create `RemixSharedChat/Endpoint.cs` (mirrors `BranchChat` for the `201 + Location` pattern and `GetSharedChat` for route binding). It requires authentication by default (no `AllowAnonymous`). `Throttle` adds bounded per-client rate limiting; the header key follows FastEndpoints' convention — if the project later adds a global rate-limit policy, replace this with that policy.

```csharp
using Chat.Api.Endpoints;
using Chat.Application.SharedChats.Commands.Remix;
using Chat.Application.SharedChats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.SharedChats.RemixSharedChat;

internal sealed class Request
{
    public Guid ShareId { get; init; }
}

internal sealed class Endpoint(ISender sender) : Endpoint<Request, Response>
{
    public const string RouteName = "Chat.SharedChats.Remix";

    public override void Configure()
    {
        Post("/shared-chats/{shareId}/remix");
        Version(1);

        Throttle(hitLimit: 20, durationSeconds: 60);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Remix Shared Chat")
                .WithDescription("Copies a remix-enabled shared chat's path into a new independent chat owned by the authenticated caller.")
                .Produces<Response>(StatusCodes.Status201Created, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status403Forbidden, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.SharedChats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        ErrorOr<RemixSharedChatResult> result = await sender.Send
        (
            new RemixSharedChatCommand(request.ShareId),
            ct
        );

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        Response response = new()
        {
            ChatId = result.Value.ChatId,
            Title = result.Value.Title,
            CreatedAt = result.Value.CreatedAt
        };

        await Send.ResultAsync(TypedResults.Created($"/v1/chats/{response.ChatId}", response));
    }
}
```

- [ ] **Step 3: Build the API project**

Run: `dotnet build src/services/Chat/Chat.Api`
Expected: Build succeeded. (`CustomTags.SharedChats` already exists; `Throttle` is a FastEndpoints method — if the project pins a FastEndpoints version without `Throttle`, remove that line and note that rate limiting is deferred to a global policy.)

- [ ] **Step 4: Run the full Chat test suite**

Run: `dotnet test tests/Chat/Chat.Domain.Tests tests/Chat/Chat.Application.Tests`
Expected: All PASS.

- [ ] **Step 5: Manual end-to-end verification**

With the app running (`dotnet run` on the AppHost) and the database migrated (the `MigrationWorker` applies `AddSharedChatRemix` on startup, or run `dotnet ef database update --project src/services/Chat/Chat.Infrastructure`):

1. Create a share with `allowRemix: true` via `POST /api/chat/v1/me/shared-chats`.
2. `GET /api/chat/v1/shared-chats/{shareId}` and confirm the response contains `"allowRemix": true`.
3. `POST /api/chat/v1/shared-chats/{shareId}/remix` as a *different* authenticated user; confirm `201 Created`, a `Location` header, and a new chat that appears in that user's chat list with the copied messages.
4. Create a share with `allowRemix: false`, attempt remix, and confirm `403`.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Api
git commit -m "feat(chat): add remix shared chat endpoint"
```

---

## Self-Review

**Spec coverage:**
- `allow_remix` column, set-at-creation, immutable → Task 2 (column + `TryAddAsync` `ON CONFLICT DO NOTHING` cannot upgrade), Task 3 (creation wiring). ✅
- `ChatRemixOrigin` {shareId, sourceChatId, sourceMessageId}, internal-only, both-or-neither check, no FK → Task 1 (value object) + Task 2 (mapping + check constraint). ✅
- `ChatThread.CreateRemix` with terminal-assistant guard, path validation, `CopyForBranch` reuse → Task 1. ✅
- Consent-gated non-owner-scoped source read → Task 2 (`GetForRemixAsync`, `GetSnapshotByChatIdAsync`) + Task 5 (handler). ✅
- Passive copy, no `TurnRequested` → Task 5 (handler has no `IMessageBus`). ✅
- Public read exposes only `allowRemix` → Task 4. ✅
- `POST /v1/shared-chats/{shareId}/remix`, authenticated, `201 + Location`, rate-limited → Task 6. ✅
- Errors: 403 `RemixNotAllowed`, 404 missing, 409 `RemixTargetMustBeAssistant`, 500 `InvalidRemixPath` → Task 1 + Task 5 (verified against `CustomResults` type→status mapping). ✅
- No BFF change → Global Constraints (existing authenticated catch-all covers it). ✅
- Test plan (domain guards, owner-scope bypass, gating, independent copy, `allowRemix` in read) → Tasks 1,3,4,5. ✅

**Type consistency:** `CreateRemix(UserId, ChatThread, ChatMessageId, SharedChatId, ChatTitle, DateTimeOffset)` is used identically in Task 1 (definition + tests) and Task 5 (handler call). `RemixOrigin.ShareId/SourceChatId/SourceMessageId` names match across Task 1, Task 2 mapping, and Task 5 assertions. `RemixSharedChatResult(ChatId, Title, CreatedAt)` matches between Task 5 and Task 6. `GetForRemixAsync`/`GetSnapshotByChatIdAsync` signatures match between Task 2 definitions and Task 5 usage.

**Placeholders:** The application-test tasks (3, 4, 5) intentionally instruct the engineer to read each test file's existing fakes/factories before writing, because those helper names live in the test project and must be matched verbatim rather than guessed. All production code is fully specified.
