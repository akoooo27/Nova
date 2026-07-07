# Chat Deletion & Bulk Operations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add single-chat hard delete, archive-all, delete-all endpoints, and purge a user's chats when their account is deleted.

**Architecture:** Three new `IChatRepository` set-based operations (`ExecuteUpdateAsync`/`ExecuteDeleteAsync`, no batching — user-scoped row counts), three Mediator command/handler pairs mirroring the `DeleteAllSharedChats` shape, three FastEndpoints endpoints, and one call added to `UserDeletedConsumer`. DB cascades (already configured) remove `chat_messages` and `shared_chats` rows.

**Tech Stack:** .NET / EF Core (Npgsql), Mediator (`ICommand`/`ICommandHandler`, `ValueTask`), ErrorOr, FluentValidation, FastEndpoints, xUnit with hand-rolled fakes.

**Spec:** `docs/superpowers/specs/2026-07-07-chat-deletion-and-bulk-operations-design.md`

## Global Constraints

- All paths below are relative to the repo root (`/Users/akakijomidava/conductor/workspaces/Nova/atlanta`).
- Match existing code style exactly: file-scoped namespaces, sorted `using` groups separated by blank lines, named arguments on multi-line calls, primary-constructor DI on handlers.
- Commit messages: conventional-commit style used in this repo (`feat(chat): ...`); **no Co-Authored-By trailer**.
- Temporary chats are excluded from all user-facing operations; included only in the account-deletion purge.
- `ArchiveAllAsync` sets **only** `IsArchived` — `ChatThread.Archive()` does not touch `UpdatedAt`, and bulk archive must match.
- Build check: `dotnet build Nova.slnx` — Expected: `Build succeeded`.
- Test check: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj` — Expected: all tests pass, 0 failed.

---

### Task 1: Repository operations (interface, EF implementation, test fake)

The EF implementations are set-based SQL and have no integration-test harness in this repo; behavioral coverage comes from the handler tests in Tasks 2–4 via `FakeChatRepository`. The compile break after the interface change is the "failing test" for this task.

**Files:**
- Modify: `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs`
- Modify: `tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs`

**Interfaces:**
- Consumes: existing `ChatId`, `UserId` value objects; `ChatDbContext.ChatThreads`.
- Produces (Tasks 2–4 and 6 depend on these exact signatures):
  - `Task<int> DeleteByIdAsync(ChatId id, UserId userId, CancellationToken cancellationToken = default)`
  - `Task<int> ArchiveAllAsync(UserId userId, CancellationToken cancellationToken = default)`
  - `Task<int> DeleteAllAsync(UserId userId, bool includeTemporary = false, CancellationToken cancellationToken = default)`

- [ ] **Step 1: Add the three methods to `IChatRepository`**

Append inside the interface in `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs` (after `DeleteExpiredTemporaryChatsAsync`):

```csharp
    /// <summary>
    /// Hard-deletes an owner-scoped, non-temporary chat. Returns the number of
    /// affected rows (0 means not found, not owned, or temporary).
    /// </summary>
    Task<int> DeleteByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Archives every non-temporary, not-yet-archived chat owned by the user.
    /// Sets only the archived flag; UpdatedAt is intentionally untouched so
    /// list ordering matches single-chat archive behavior.
    /// </summary>
    Task<int> ArchiveAllAsync
    (
        UserId userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Hard-deletes every chat owned by the user. Temporary chats are excluded
    /// unless <paramref name="includeTemporary"/> is set (account-deletion purge).
    /// </summary>
    Task<int> DeleteAllAsync
    (
        UserId userId,
        bool includeTemporary = false,
        CancellationToken cancellationToken = default
    );
```

- [ ] **Step 2: Run the build to verify it fails**

Run: `dotnet build Nova.slnx`
Expected: FAIL — `ChatRepository` and `FakeChatRepository` do not implement the new interface members.

- [ ] **Step 3: Implement in `ChatRepository`**

Append inside the class in `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs`:

```csharp
    public async Task<int> DeleteByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.ChatThreads
            .Where(chat => chat.Id == id && chat.UserId == userId && !chat.IsTemporary)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> ArchiveAllAsync
    (
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.ChatThreads
            .Where(chat => chat.UserId == userId && !chat.IsTemporary && !chat.IsArchived)
            .ExecuteUpdateAsync
            (
                setters => setters.SetProperty(chat => chat.IsArchived, true),
                cancellationToken
            );
    }

    public async Task<int> DeleteAllAsync
    (
        UserId userId,
        bool includeTemporary = false,
        CancellationToken cancellationToken = default
    )
    {
        return await db.ChatThreads
            .Where(chat => chat.UserId == userId && (includeTemporary || !chat.IsTemporary))
            .ExecuteDeleteAsync(cancellationToken);
    }
```

- [ ] **Step 4: Implement in `FakeChatRepository`**

Append inside the class in `tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs`:

```csharp
    public Task<int> DeleteByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        int removed = _threads.RemoveAll
        (
            thread => thread.Id == id && thread.UserId == userId && !thread.IsTemporary
        );

        return Task.FromResult(removed);
    }

    public Task<int> ArchiveAllAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        List<ChatThread> targets = _threads
            .Where(thread => thread.UserId == userId && !thread.IsTemporary && !thread.IsArchived)
            .ToList();

        foreach (ChatThread thread in targets)
        {
            thread.Archive();
        }

        return Task.FromResult(targets.Count);
    }

    public Task<int> DeleteAllAsync
    (
        UserId userId,
        bool includeTemporary = false,
        CancellationToken cancellationToken = default
    )
    {
        int removed = _threads.RemoveAll
        (
            thread => thread.UserId == userId && (includeTemporary || !thread.IsTemporary)
        );

        return Task.FromResult(removed);
    }
```

- [ ] **Step 5: Build and run the existing suite**

Run: `dotnet build Nova.slnx && dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj`
Expected: build succeeds; all existing tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Domain/Chats/IChatRepository.cs src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs
git commit -m "feat(chat): add owner-scoped delete and bulk chat repository operations"
```

---

### Task 2: DeleteChatCommand — single chat deletion

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/DeleteChat/DeleteChatCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/DeleteChat/DeleteChatCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/DeleteChat/DeleteChatHandler.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Commands/DeleteChatHandlerTests.cs`

**Interfaces:**
- Consumes: `IChatRepository.DeleteByIdAsync(ChatId, UserId, CancellationToken)` from Task 1; `ChatOperationErrors.ChatNotFound(ChatId)`; `IUserContext`; `IUnitOfWork`.
- Produces: `public sealed record DeleteChatCommand(Guid ChatId) : ICommand<ErrorOr<Deleted>>` — used by the endpoint in Task 5.

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Commands/DeleteChatHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Commands.DeleteChat;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class DeleteChatHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task HandleDeletesOwnedChat()
    {
        ChatThread thread = CreateThread();
        _chats.Seed(thread);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteChatCommand(thread.Id.Value),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Empty(_chats.Threads);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenChatDoesNotBelongToUser()
    {
        ChatThread thread = CreateThread(userId: "auth0|other-user");
        _chats.Seed(thread);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteChatCommand(thread.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Error error = Assert.Single(result.Errors);
        Assert.Equal(ErrorType.NotFound, error.Type);
        Assert.Equal("Chat.NotFound", error.Code);
        Assert.Single(_chats.Threads);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundForTemporaryChat()
    {
        ChatThread thread = CreateThread(isTemporary: true);
        _chats.Seed(thread);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteChatCommand(thread.Id.Value),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
        Assert.Single(_chats.Threads);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorsWithoutDeleting()
    {
        ChatThread thread = CreateThread();
        _chats.Seed(thread);

        ErrorOr<Deleted> result = await CreateHandler(userId: string.Empty).Handle
        (
            new DeleteChatCommand(Guid.Empty),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, error => error.Type == ErrorType.Validation);
        Assert.Contains(result.Errors, error => error.Code == "ChatId.Empty");
        Assert.Single(_chats.Threads);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private DeleteChatHandler CreateHandler(string userId = "auth0|user-1") => new
    (
        userContext: new FakeUserContext(userId),
        chats: _chats,
        unitOfWork: _unitOfWork
    );

    private static ChatThread CreateThread
    (
        string userId = "auth0|user-1",
        bool isTemporary = false
    ) => ChatThread.Create
    (
        userId: UserId.Create(userId).Value,
        title: ChatTitle.Create("Planning chat").Value,
        firstUserMessage: MessageContent.Create("Hello").Value,
        createdAt: CreatedAt,
        isTemporary: isTemporary
    );
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter DeleteChatHandlerTests`
Expected: FAIL — compile error, `DeleteChatCommand`/`DeleteChatHandler` do not exist.

- [ ] **Step 3: Create command, validator, and handler**

Create `src/services/Chat/Chat.Application/Chats/Commands/DeleteChat/DeleteChatCommand.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.DeleteChat;

public sealed record DeleteChatCommand(Guid ChatId) : ICommand<ErrorOr<Deleted>>;
```

Create `src/services/Chat/Chat.Application/Chats/Commands/DeleteChat/DeleteChatCommandValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.Chats.Commands.DeleteChat;

internal sealed class DeleteChatCommandValidator : AbstractValidator<DeleteChatCommand>
{
    public DeleteChatCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();
    }
}
```

Create `src/services/Chat/Chat.Application/Chats/Commands/DeleteChat/DeleteChatHandler.cs`:

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Commands.DeleteChat;

internal sealed class DeleteChatHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteChatCommand, ErrorOr<Deleted>>
{
    public async ValueTask<ErrorOr<Deleted>> Handle(DeleteChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ChatId chatId = chatIdResult.Value;

        int deleted = await chats.DeleteByIdAsync
        (
            id: chatId,
            userId: userIdResult.Value,
            cancellationToken: cancellationToken
        );

        if (deleted == 0)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Deleted;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter DeleteChatHandlerTests`
Expected: PASS — 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands/DeleteChat/ tests/Chat/Chat.Application.Tests/Chats/Commands/DeleteChatHandlerTests.cs
git commit -m "feat(chat): add single chat deletion command"
```

---

### Task 3: ArchiveAllChatsCommand

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/ArchiveAllChats/ArchiveAllChatsCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/ArchiveAllChats/ArchiveAllChatsHandler.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Commands/ArchiveAllChatsHandlerTests.cs`

**Interfaces:**
- Consumes: `IChatRepository.ArchiveAllAsync(UserId, CancellationToken)` from Task 1.
- Produces: `public sealed record ArchiveAllChatsCommand : ICommand<ErrorOr<Success>>` — used by the endpoint in Task 5. (No parameters — user comes from `IUserContext`; no validator needed.)

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Commands/ArchiveAllChatsHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Commands.ArchiveAllChats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class ArchiveAllChatsHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task HandleArchivesOnlyOwnedActiveNonTemporaryChats()
    {
        ChatThread active = CreateThread();
        ChatThread inProject = CreateThread();
        inProject.MoveToProject(ProjectId.New(), CreatedAt.AddMinutes(1));
        ChatThread alreadyArchived = CreateThread();
        alreadyArchived.Archive();
        ChatThread temporary = CreateThread(isTemporary: true);
        ChatThread foreign = CreateThread(userId: "auth0|other-user");

        _chats.Seed(active);
        _chats.Seed(inProject);
        _chats.Seed(alreadyArchived);
        _chats.Seed(temporary);
        _chats.Seed(foreign);

        ErrorOr<Success> result = await CreateHandler().Handle
        (
            new ArchiveAllChatsCommand(),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.True(active.IsArchived);
        Assert.Equal(CreatedAt, active.UpdatedAt);
        Assert.True(inProject.IsArchived);
        Assert.True(alreadyArchived.IsArchived);
        Assert.False(temporary.IsArchived);
        Assert.False(foreign.IsArchived);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleSucceedsWhenUserHasNoChats()
    {
        ErrorOr<Success> result = await CreateHandler().Handle
        (
            new ArchiveAllChatsCommand(),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorForEmptyUserContext()
    {
        ChatThread thread = CreateThread();
        _chats.Seed(thread);

        ErrorOr<Success> result = await CreateHandler(userId: string.Empty).Handle
        (
            new ArchiveAllChatsCommand(),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.False(thread.IsArchived);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private ArchiveAllChatsHandler CreateHandler(string userId = "auth0|user-1") => new
    (
        userContext: new FakeUserContext(userId),
        chats: _chats,
        unitOfWork: _unitOfWork
    );

    private static ChatThread CreateThread
    (
        string userId = "auth0|user-1",
        bool isTemporary = false
    ) => ChatThread.Create
    (
        userId: UserId.Create(userId).Value,
        title: ChatTitle.Create("Planning chat").Value,
        firstUserMessage: MessageContent.Create("Hello").Value,
        createdAt: CreatedAt,
        isTemporary: isTemporary
    );
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter ArchiveAllChatsHandlerTests`
Expected: FAIL — compile error, `ArchiveAllChatsCommand`/`ArchiveAllChatsHandler` do not exist.

- [ ] **Step 3: Create command and handler**

Create `src/services/Chat/Chat.Application/Chats/Commands/ArchiveAllChats/ArchiveAllChatsCommand.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.ArchiveAllChats;

public sealed record ArchiveAllChatsCommand : ICommand<ErrorOr<Success>>;
```

Create `src/services/Chat/Chat.Application/Chats/Commands/ArchiveAllChats/ArchiveAllChatsHandler.cs`:

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Domain.Chats;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Commands.ArchiveAllChats;

internal sealed class ArchiveAllChatsHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveAllChatsCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(ArchiveAllChatsCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        await chats.ArchiveAllAsync(userIdResult.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter ArchiveAllChatsHandlerTests`
Expected: PASS — 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands/ArchiveAllChats/ tests/Chat/Chat.Application.Tests/Chats/Commands/ArchiveAllChatsHandlerTests.cs
git commit -m "feat(chat): add archive-all chats command"
```

---

### Task 4: DeleteAllChatsCommand

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/DeleteAllChats/DeleteAllChatsCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/DeleteAllChats/DeleteAllChatsHandler.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Commands/DeleteAllChatsHandlerTests.cs`

**Interfaces:**
- Consumes: `IChatRepository.DeleteAllAsync(UserId, bool includeTemporary, CancellationToken)` from Task 1 — called here with the default `includeTemporary: false`.
- Produces: `public sealed record DeleteAllChatsCommand : ICommand<ErrorOr<Deleted>>` — used by the endpoint in Task 5.

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Commands/DeleteAllChatsHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Commands.DeleteAllChats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Application.Tests.Turns;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Projects.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Commands;

public sealed class DeleteAllChatsHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 7, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeChatRepository _chats = new();
    private readonly TurnFakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task HandleDeletesOwnedChatsIncludingArchivedButKeepsTemporaryAndForeign()
    {
        ChatThread active = CreateThread();
        ChatThread inProject = CreateThread();
        inProject.MoveToProject(ProjectId.New(), CreatedAt.AddMinutes(1));
        ChatThread archived = CreateThread();
        archived.Archive();
        ChatThread temporary = CreateThread(isTemporary: true);
        ChatThread foreign = CreateThread(userId: "auth0|other-user");

        _chats.Seed(active);
        _chats.Seed(inProject);
        _chats.Seed(archived);
        _chats.Seed(temporary);
        _chats.Seed(foreign);

        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteAllChatsCommand(),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal(2, _chats.Threads.Count);
        Assert.Contains(temporary, _chats.Threads);
        Assert.Contains(foreign, _chats.Threads);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleSucceedsWhenUserHasNoChats()
    {
        ErrorOr<Deleted> result = await CreateHandler().Handle
        (
            new DeleteAllChatsCommand(),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Equal(1, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task HandleReturnsValidationErrorForEmptyUserContext()
    {
        ChatThread thread = CreateThread();
        _chats.Seed(thread);

        ErrorOr<Deleted> result = await CreateHandler(userId: string.Empty).Handle
        (
            new DeleteAllChatsCommand(),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Validation, result.FirstError.Type);
        Assert.Single(_chats.Threads);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    private DeleteAllChatsHandler CreateHandler(string userId = "auth0|user-1") => new
    (
        userContext: new FakeUserContext(userId),
        chats: _chats,
        unitOfWork: _unitOfWork
    );

    private static ChatThread CreateThread
    (
        string userId = "auth0|user-1",
        bool isTemporary = false
    ) => ChatThread.Create
    (
        userId: UserId.Create(userId).Value,
        title: ChatTitle.Create("Planning chat").Value,
        firstUserMessage: MessageContent.Create("Hello").Value,
        createdAt: CreatedAt,
        isTemporary: isTemporary
    );
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter DeleteAllChatsHandlerTests`
Expected: FAIL — compile error, `DeleteAllChatsCommand`/`DeleteAllChatsHandler` do not exist.

- [ ] **Step 3: Create command and handler**

Create `src/services/Chat/Chat.Application/Chats/Commands/DeleteAllChats/DeleteAllChatsCommand.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.DeleteAllChats;

public sealed record DeleteAllChatsCommand : ICommand<ErrorOr<Deleted>>;
```

Create `src/services/Chat/Chat.Application/Chats/Commands/DeleteAllChats/DeleteAllChatsHandler.cs`:

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Domain.Chats;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Commands.DeleteAllChats;

internal sealed class DeleteAllChatsHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteAllChatsCommand, ErrorOr<Deleted>>
{
    public async ValueTask<ErrorOr<Deleted>> Handle(DeleteAllChatsCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        await chats.DeleteAllAsync(userIdResult.Value, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Deleted;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter DeleteAllChatsHandlerTests`
Expected: PASS — 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands/DeleteAllChats/ tests/Chat/Chat.Application.Tests/Chats/Commands/DeleteAllChatsHandlerTests.cs
git commit -m "feat(chat): add delete-all chats command"
```

---

### Task 5: API endpoints

No API-level test project exists in this repo; endpoints are thin Mediator dispatchers verified by build + the handler tests above.

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/DeleteChat/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/ArchiveAllChats/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/DeleteAllChats/Endpoint.cs`

**Interfaces:**
- Consumes: `DeleteChatCommand(Guid)` (Task 2), `ArchiveAllChatsCommand` (Task 3), `DeleteAllChatsCommand` (Task 4); `CustomTags.Chats`; `CustomResults.Problem`.
- Produces: routes `DELETE /chats/{chatId}`, `POST /me/chats/archive-all`, `DELETE /me/chats` (all v1, 204 on success).

- [ ] **Step 1: Create the DeleteChat endpoint**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/DeleteChat/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints;
using Chat.Application.Chats.Commands.DeleteChat;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.DeleteChat;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.Delete";

    public override void Configure()
    {
        Delete("/chats/{chatId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Delete Chat")
                .WithDescription("Permanently deletes a chat owned by the authenticated user, including its messages and shared links.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        DeleteChatCommand command = new(Route<Guid>("chatId"));

        ErrorOr<Deleted> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
```

- [ ] **Step 2: Create the ArchiveAllChats endpoint**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/ArchiveAllChats/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints;
using Chat.Application.Chats.Commands.ArchiveAllChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.ArchiveAllChats;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.ArchiveAll";

    public override void Configure()
    {
        Post("/me/chats/archive-all");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Archive All Chats")
                .WithDescription("Archives every active chat owned by the authenticated user, including chats inside projects.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<Success> result = await sender.Send(new ArchiveAllChatsCommand(), ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
```

- [ ] **Step 3: Create the DeleteAllChats endpoint**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/DeleteAllChats/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints;
using Chat.Application.Chats.Commands.DeleteAllChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.DeleteAllChats;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.DeleteAll";

    public override void Configure()
    {
        Delete("/me/chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Delete All Chats")
                .WithDescription("Permanently deletes every chat owned by the authenticated user, including archived chats and chats inside projects.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<Deleted> result = await sender.Send(new DeleteAllChatsCommand(), ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
```

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build Nova.slnx && dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj`
Expected: build succeeds; all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats/DeleteChat/ src/services/Chat/Chat.Api/Endpoints/Chats/ArchiveAllChats/ src/services/Chat/Chat.Api/Endpoints/Chats/DeleteAllChats/
git commit -m "feat(chat): expose chat deletion and bulk chat endpoints"
```

---

### Task 6: Purge chats on account deletion

`UserDeletedConsumer` currently only marks the user read-model deleted; chat content is retained forever (the compliance gap named in the spec). Identity mapping verified during design: chats store `UserId` = the JWT `sub` claim (e.g. `auth0|user-1`), and the Auth0 event mapper sets `ProviderUserId` from the same Auth0 user id, so `UserId.Create(message.ProviderUserId)` addresses the same rows. No consumer test infrastructure exists (consumers depend on the concrete `ChatDbContext`); this task is verified by build + suite. The purge is idempotent, so MassTransit redelivery is safe even though `ExecuteDelete` runs outside the `SaveChangesAsync` transaction.

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/Users/Consumers/UserDeletedConsumer.cs`

**Interfaces:**
- Consumes: `IChatRepository.DeleteAllAsync(UserId, includeTemporary: true, CancellationToken)` from Task 1 (registered in DI as scoped).

- [ ] **Step 1: Add the purge to `UserDeletedConsumer`**

Replace the full contents of `src/services/Chat/Chat.Infrastructure/Users/Consumers/UserDeletedConsumer.cs` with:

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Shared;
using Chat.Infrastructure.Database;
using Chat.Infrastructure.Users.Models;

using ErrorOr;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Shared.Contracts.IdentityIngress.Events;

namespace Chat.Infrastructure.Users.Consumers;

internal sealed partial class UserDeletedConsumer(
    ChatDbContext db,
    IChatRepository chats,
    ILogger<UserDeletedConsumer> logger)
    : IConsumer<UserDeleted>
{
    public async Task Consume(ConsumeContext<UserDeleted> context)
    {
        UserDeleted message = context.Message;

        UserReadModel? user = await db.Users.SingleOrDefaultAsync
        (
            candidate => candidate.ProviderUserId == message.ProviderUserId &&
                         candidate.Provider == message.Provider,
            context.CancellationToken
        );

        if (user is null)
        {
            user = UserReadModel.Create
            (
                providerUserId: message.ProviderUserId,
                provider: message.Provider,
                observedAt: message.OccurredAt
            );

            db.Users.Add(user);
        }

        if (user.IsStale(message.OccurredAt))
        {
            LogStaleUserDeletedIgnored(message.EventId, message.Provider, message.ProviderUserId);
            return;
        }

        user.MarkDeleted(message.OccurredAt);

        ErrorOr<UserId> userIdResult = UserId.Create(message.ProviderUserId);

        if (userIdResult.IsError)
        {
            LogChatPurgeSkippedForInvalidUserId(message.EventId, message.Provider, message.ProviderUserId);
        }
        else
        {
            int purged = await chats.DeleteAllAsync
            (
                userId: userIdResult.Value,
                includeTemporary: true,
                cancellationToken: context.CancellationToken
            );

            LogChatsPurgedForDeletedUser(purged, message.Provider, message.ProviderUserId);
        }

        await db.SaveChangesAsync(context.CancellationToken);

        LogUserDeletedProjected(message.EventId, message.Provider, message.ProviderUserId);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Projected deleted identity user {Provider}:{ProviderUserId} from event {EventId}")]
    private partial void LogUserDeletedProjected(string eventId, string provider, string providerUserId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Ignored stale deleted identity user event {EventId} for {Provider}:{ProviderUserId}")]
    private partial void LogStaleUserDeletedIgnored(string eventId, string provider, string providerUserId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Purged {PurgedChatCount} chats for deleted identity user {Provider}:{ProviderUserId}")]
    private partial void LogChatsPurgedForDeletedUser(int purgedChatCount, string provider, string providerUserId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Skipped chat purge for event {EventId}: invalid user id {Provider}:{ProviderUserId}")]
    private partial void LogChatPurgeSkippedForInvalidUserId(string eventId, string provider, string providerUserId);
}
```

- [ ] **Step 2: Build and run the full suite**

Run: `dotnet build Nova.slnx && dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj && dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj`
Expected: build succeeds; all tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Users/Consumers/UserDeletedConsumer.cs
git commit -m "feat(chat): purge all user chats on account deletion"
```

---

### Task 7: Final verification

- [ ] **Step 1: Full build and both test projects**

Run: `dotnet build Nova.slnx && dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj && dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj`
Expected: build succeeds; all tests pass, 0 failed.

- [ ] **Step 2: Diff review against the spec**

Run: `git diff origin/main...HEAD --stat`
Confirm every file listed in Tasks 1–6 appears, and nothing else (besides the spec/plan docs). Spot-check that no task drifted from the spec: temp-chat exclusions, `IsArchived`-only update, `includeTemporary: true` only in the consumer.
