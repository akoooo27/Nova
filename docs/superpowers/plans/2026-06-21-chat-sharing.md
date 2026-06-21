# Chat Sharing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (\`- [ ]\`) syntax for tracking.

**Goal:** Add anonymous, reference-backed shared-chat links with owner management, public path-only reads, cascade revocation, and latest-only assistant regeneration.

**Architecture:** Persist one SharedChat row per conversation/node pair and retain the source message tree as the content store. Authenticated FastEndpoints dispatch Mediator commands and queries; application handlers enforce ownership, PostgreSQL enforces pair uniqueness and source relationships, Dapper recursively reads only the stored node's ancestor chain, and a dedicated BFF route exposes only the anonymous GET operation.

**Tech Stack:** .NET 10, C# 14, FastEndpoints 8.1, Mediator 3.0, EF Core 10 with Npgsql, Dapper 2.1, PostgreSQL, ASP.NET Core rate limiting, YARP/Duende BFF, xUnit 2.9, Testcontainers.PostgreSql 4.12.0.

---

## Execution constraints

- Read the approved design first: docs/superpowers/specs/2026-06-21-chat-sharing-design.md.
- Follow AGENTS.md: retain Mediator.SourceGenerator / Mediator.Abstractions, FastEndpoints, and the intentionally pinned MassTransit version.
- Test work is explicitly approved for this feature.
- Before every dotnet build, dotnet test, dotnet restore, dotnet run, or dotnet ef command, request elevated permission and explain why that command is required.
- Use the superpowers:using-git-worktrees skill before implementation if execution needs isolation.
- Preserve unrelated worktree changes and stage only files named by the current task.
- Do not implement frontend UI. The backend frontend URL and public response are contracts only.

## File structure

### New production files

- src/services/Chat/Chat.Domain/SharedChats/SharedChat.cs — immutable share metadata.
- src/services/Chat/Chat.Domain/SharedChats/ISharedChatRepository.cs — atomic create/get and owner deletion boundary.
- src/services/Chat/Chat.Domain/SharedChats/ValueObjects/SharedChatId.cs — random UUIDv4 bearer identifier.
- src/services/Chat/Chat.Application/SharedChats/SharedChatLimits.cs — pagination defaults and bounds.
- src/services/Chat/Chat.Application/SharedChats/Commands/CreateSharedChat/* — create-or-return-existing command.
- src/services/Chat/Chat.Application/SharedChats/Commands/DeleteSharedChat/* — owner-scoped single deletion.
- src/services/Chat/Chat.Application/SharedChats/Commands/DeleteAllSharedChats/* — owner-scoped bulk deletion.
- src/services/Chat/Chat.Application/SharedChats/Queries/GetSharedChats/* — owner list query and reader contract.
- src/services/Chat/Chat.Application/SharedChats/Queries/GetPublicSharedChat/* — anonymous public query and reader contract.
- src/services/Chat/Chat.Infrastructure/SharedChats/Configurations/SharedChatConfiguration.cs — EF mapping and constraints.
- src/services/Chat/Chat.Infrastructure/SharedChats/Repositories/SharedChatRepository.cs — PostgreSQL create/get/delete implementation.
- src/services/Chat/Chat.Infrastructure/SharedChats/Readers/SharedChatListReader.cs — paginated owner Dapper reader.
- src/services/Chat/Chat.Infrastructure/SharedChats/Readers/PublicSharedChatReader.cs — recursive ancestor-path Dapper reader.
- src/services/Chat/Chat.Api/Options/SharedLinksOptions.cs — validated public frontend base URL.
- src/services/Chat/Chat.Api/SharedChats/SharedLinkUrlBuilder.cs — canonical frontend URL construction.
- src/services/Chat/Chat.Api/Endpoints/SharedChats/* — create, list, delete-one, delete-all, and public endpoints.
- src/services/Chat/Chat.Api/Security/PublicSharedChatRateLimit.cs — anonymous endpoint policy.
- src/services/Chat/Chat.Api/Infrastructure/ChatConcurrencyExceptionHandler.cs — EF concurrency-to-409 translation.
- Timestamped ChatSharing EF migration files generated under src/services/Chat/Chat.Infrastructure/Database/Migrations.

### New test files/projects

- tests/Chat/Chat.Domain.Tests/SharedChats/SharedChatTests.cs.
- tests/Chat/Chat.Application.Tests/SharedChats/* — command/query tests and fakes.
- tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj.
- tests/Chat/Chat.Infrastructure.Tests/Database/PostgreSqlFixture.cs.
- tests/Chat/Chat.Infrastructure.Tests/SharedChats/SharedChatRepositoryTests.cs.
- tests/Chat/Chat.Infrastructure.Tests/SharedChats/SharedChatListReaderTests.cs.
- tests/Chat/Chat.Infrastructure.Tests/SharedChats/PublicSharedChatReaderTests.cs.
- tests/Chat/Chat.Api.Tests/Chat.Api.Tests.csproj.
- tests/Chat/Chat.Api.Tests/SharedChats/* — URL, response, anonymous-header, and concurrency-handler tests.
- tests/BFF/BFF.Tests/BFF.Tests.csproj.
- tests/BFF/BFF.Tests/RemoteApis/ChatApiProxyConfigurationTests.cs.

### Existing files modified

- Directory.Packages.props and Nova.slnx — test dependencies/projects.
- ChatThread.cs, ChatErrors.cs, and ChatThreadTests.cs — sharing eligibility and latest-only regeneration.
- RegenerateMessageHandlerTests.cs — stale target side-effect coverage.
- ChatMessageConfiguration.cs — composite principal key for a selected-node foreign key.
- ChatDbContext.cs and Chat.Infrastructure/DependencyInjection.cs — SharedChat persistence registrations.
- Chat.Api/DependencyInjection.cs, Chat.Api/Program.cs, appsettings.Development.json, and CustomTags.cs — options, error handling, rate limiting, and endpoint metadata.
- BFF/RemoteApis/ChatApiProxyConfiguration.cs and BFF/Program.cs — anonymous GET-only proxy route.

---

### Task 1: Enforce latest-only regeneration

**Files:**
- Modify: src/services/Chat/Chat.Domain/Chats/ChatErrors.cs
- Modify: src/services/Chat/Chat.Domain/Chats/ChatThread.cs
- Modify: tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs
- Modify: tests/Chat/Chat.Application.Tests/Turns/RegenerateMessageHandlerTests.cs

- [ ] **Step 1: Write the failing domain test**

Add this test beside the existing regeneration tests:

~~~csharp
[Fact]
public void RegenerateAssistantRejectsTerminalAssistantThatIsNotCurrent()
{
    ChatThread chat = TestChatFactory.CreateThread();
    ChatMessage stale = CompleteAssistant(chat);
    ChatMessage followUp = AddUser(chat, stale.Id, TestChatFactory.CreatedAt.AddMinutes(3));
    ChatMessage current = BeginAssistant(chat, followUp.Id, TestChatFactory.CreatedAt.AddMinutes(4));
    _ = chat.CompleteAssistantMessage
    (
        current.Id,
        TestChatFactory.CreateContent("Latest answer"),
        TestChatFactory.CreatedAt.AddMinutes(5)
    );

    ErrorOr<ChatMessage> result = chat.RegenerateAssistant
    (
        stale.Id,
        LlmModelId.New(),
        TestChatFactory.CreatedAt.AddMinutes(6)
    );

    AssertError(result, ErrorType.Conflict, "Chat.RegenerationTargetMustBeCurrent");
    Assert.Equal(current.Id, chat.CurrentMessageId);
}
~~~

- [ ] **Step 2: Add the failing handler side-effect test**

Build a thread with two completed assistant turns, send RegenerateMessageCommand for the first assistant, and assert:

~~~csharp
Assert.True(result.IsError);
Assert.Equal("Chat.RegenerationTargetMustBeCurrent", Assert.Single(result.Errors).Code);
Assert.Equal(0, unitOfWork.SaveCount);
Assert.Empty(messageBus.PublishedMessages);
~~~

Use the existing FakeChatRepository, FakeLlmProviderRepository, Recording message bus, and TurnFakeUnitOfWork helpers from RegenerateMessageHandlerTests.

- [ ] **Step 3: Run the focused tests and verify failure**

Request elevated permission, then run:

~~~bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~RegenerateAssistant"
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~RegenerateMessageHandlerTests"
~~~

Expected: the new assertions fail because stale terminal assistant nodes are currently accepted.

- [ ] **Step 4: Add the domain error and guard**

Add to ChatErrors:

~~~csharp
public static Error RegenerationTargetMustBeCurrent(ChatMessageId messageId) =>
    Error.Conflict
    (
        code: "Chat.RegenerationTargetMustBeCurrent",
        description: $"Assistant message '{messageId.Value}' is not the current chat node."
    );
~~~

In ChatThread.RegenerateAssistant, after the assistant/parent guard and before creating a sibling, add:

~~~csharp
if (target.Id != CurrentMessageId)
{
    return ChatErrors.RegenerationTargetMustBeCurrent(messageId);
}
~~~

Keep the generating-status guard. Do not move the rule into the endpoint or handler; it protects every caller of the aggregate.

- [ ] **Step 5: Re-run tests and commit**

Request elevated permission and rerun the Step 3 commands. Expected: PASS.

~~~bash
git add src/services/Chat/Chat.Domain/Chats/ChatErrors.cs src/services/Chat/Chat.Domain/Chats/ChatThread.cs tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs tests/Chat/Chat.Application.Tests/Turns/RegenerateMessageHandlerTests.cs
git commit -m "fix(chat): require latest message for regeneration"
~~~

---

### Task 2: Add the shared-chat domain model and eligibility rules

**Files:**
- Create: src/services/Chat/Chat.Domain/SharedChats/ValueObjects/SharedChatId.cs
- Create: src/services/Chat/Chat.Domain/SharedChats/SharedChat.cs
- Create: src/services/Chat/Chat.Domain/SharedChats/ISharedChatRepository.cs
- Create: tests/Chat/Chat.Domain.Tests/SharedChats/SharedChatTests.cs
- Modify: src/services/Chat/Chat.Domain/Chats/ChatErrors.cs
- Modify: src/services/Chat/Chat.Domain/Chats/ChatThread.cs
- Modify: tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs

- [ ] **Step 1: Write failing SharedChat tests**

Create SharedChatTests with these concrete cases:

~~~csharp
public sealed class SharedChatTests
{
    [Fact]
    public void CreateFreezesSourceMetadata()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage node = CompleteAssistant(source);
        DateTimeOffset createdAt = TestChatFactory.CreatedAt.AddHours(1);

        ErrorOr<SharedChat> result = SharedChat.Create(source, node.Id, createdAt);

        Assert.False(result.IsError);
        Assert.NotEqual(Guid.Empty, result.Value.Id.Value);
        Assert.Equal(source.UserId, result.Value.UserId);
        Assert.Equal(source.Id, result.Value.ConversationId);
        Assert.Equal(node.Id, result.Value.CurrentNodeId);
        Assert.Equal(source.Title, result.Value.Title);
        Assert.Equal(createdAt, result.Value.CreatedAt);
    }

    [Fact]
    public void CreateUsesRandomVersionFourId()
    {
        ChatThread source = TestChatFactory.CreateThread();
        ChatMessage node = CompleteAssistant(source);

        SharedChat first = SharedChat.Create(source, node.Id, TestChatFactory.CreatedAt).Value;
        SharedChat second = SharedChat.Create(source, node.Id, TestChatFactory.CreatedAt).Value;

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(4, first.Id.Value.Version);
        Assert.Equal(4, second.Id.Value.Version);
    }
}
~~~

Add local CompleteAssistant logic matching the existing ChatThreadTests helper.

- [ ] **Step 2: Write failing eligibility tests**

Add tests to ChatThreadTests proving:

~~~csharp
AssertError(temporary.ValidateShareAt(node.Id), ErrorType.Conflict, "Chat.CannotShareTemporaryChat");
AssertError(chat.ValidateShareAt(ChatMessageId.New()), ErrorType.NotFound, "Chat.MessageNotFound");
AssertError(chat.ValidateShareAt(generating.Id), ErrorType.Conflict, "Chat.CannotShareGeneratingMessage");
Assert.False(chat.ValidateShareAt(terminalHistoricalNode.Id).IsError);
~~~

Reuse SetParentForCorruptionTest to add cycle, missing-ancestor, and assistant-root cases; each must produce ErrorType.Unexpected with code Chat.InvalidSharePath.

- [ ] **Step 3: Run focused domain tests and verify compile failure**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~SharedChatTests|FullyQualifiedName~ValidateShareAt"
~~~

Expected: FAIL because SharedChat, SharedChatId, and ValidateShareAt do not exist.

- [ ] **Step 4: Implement SharedChatId**

~~~csharp
using ErrorOr;

namespace Chat.Domain.SharedChats.ValueObjects;

public sealed record SharedChatId
{
    public Guid Value { get; }

    private SharedChatId(Guid value) => Value = value;

    public static SharedChatId New() => new(Guid.NewGuid());

    public static ErrorOr<SharedChatId> Create(Guid value) =>
        value == Guid.Empty
            ? Error.Validation("SharedChatId.Empty", "Shared chat id cannot be empty.")
            : new SharedChatId(value);

    public static SharedChatId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty shared chat id.");

        return new SharedChatId(value);
    }
}
~~~

- [ ] **Step 5: Implement sharing eligibility**

Add these errors:

~~~csharp
public static Error CannotShareTemporaryChat(ChatId chatId) =>
    Error.Conflict("Chat.CannotShareTemporaryChat", $"Temporary chat '{chatId.Value}' cannot be shared.");

public static Error CannotShareGeneratingMessage(ChatMessageId messageId) =>
    Error.Conflict("Chat.CannotShareGeneratingMessage", $"Generating message '{messageId.Value}' cannot be shared.");

public static Error InvalidSharePath(ChatMessageId messageId) =>
    Error.Unexpected("Chat.InvalidSharePath", $"The persisted ancestry for shared message '{messageId.Value}' is invalid.");
~~~

Add ChatThread.ValidateShareAt:

~~~csharp
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
~~~

- [ ] **Step 6: Implement SharedChat and repository contract**

~~~csharp
public sealed class SharedChat : AggregateRoot<SharedChatId>
{
    public UserId UserId { get; private set; } = default!;
    public ChatId ConversationId { get; private set; } = default!;
    public ChatMessageId CurrentNodeId { get; private set; } = default!;
    public ChatTitle Title { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private SharedChat() { }

    private SharedChat(SharedChatId id, ChatThread source, ChatMessageId currentNodeId, DateTimeOffset createdAt)
        : base(id)
    {
        UserId = source.UserId;
        ConversationId = source.Id;
        CurrentNodeId = currentNodeId;
        Title = source.Title;
        CreatedAt = createdAt;
    }

    public static ErrorOr<SharedChat> Create
    (
        ChatThread source,
        ChatMessageId currentNodeId,
        DateTimeOffset createdAt
    )
    {
        ErrorOr<Success> eligibility = source.ValidateShareAt(currentNodeId);

        return eligibility.IsError
            ? eligibility.Errors
            : new SharedChat(SharedChatId.New(), source, currentNodeId, createdAt);
    }
}
~~~

Define ISharedChatRepository with:

~~~csharp
Task<SharedChat?> GetBySourceAsync(UserId userId, ChatId conversationId, ChatMessageId currentNodeId, CancellationToken cancellationToken = default);
Task<bool> TryAddAsync(SharedChat sharedChat, CancellationToken cancellationToken = default);
Task<bool> DeleteAsync(SharedChatId id, UserId userId, CancellationToken cancellationToken = default);
Task<int> DeleteAllAsync(UserId userId, CancellationToken cancellationToken = default);
~~~

- [ ] **Step 7: Re-run tests and commit**

Request elevated permission and rerun Step 3. Expected: PASS.

~~~bash
git add src/services/Chat/Chat.Domain/SharedChats src/services/Chat/Chat.Domain/Chats/ChatErrors.cs src/services/Chat/Chat.Domain/Chats/ChatThread.cs tests/Chat/Chat.Domain.Tests/SharedChats tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs
git commit -m "feat(chat): add shared chat domain model"
~~~

---

### Task 3: Implement create-or-return-existing application flow

**Files:**
- Create: src/services/Chat/Chat.Application/SharedChats/Results/CreateSharedChatResult.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Commands/CreateSharedChat/CreateSharedChatCommand.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Commands/CreateSharedChat/CreateSharedChatCommandValidator.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Commands/CreateSharedChat/CreateSharedChatHandler.cs
- Create: tests/Chat/Chat.Application.Tests/SharedChats/FakeSharedChatRepository.cs
- Create: tests/Chat/Chat.Application.Tests/SharedChats/CreateSharedChatHandlerTests.cs
- Create: tests/Chat/Chat.Application.Tests/SharedChats/CreateSharedChatCommandValidatorTests.cs

- [ ] **Step 1: Write failing command and handler tests**

Cover these named cases:

~~~csharp
[Fact] public async Task HandleCreatesNewShareForOwnedConversationNode()
[Fact] public async Task HandleReturnsExistingShareWithoutReplacingMetadata()
[Fact] public async Task HandleCreatesDifferentLinkForDifferentNode()
[Fact] public async Task HandleReturnsNotFoundForForeignConversation()
[Fact] public async Task HandleRejectsTemporaryChat()
[Theory] public async Task ValidatorRejectsEmptyConversationOrNodeId(bool emptyConversation)
~~~

The successful assertion block must include:

~~~csharp
Assert.False(result.IsError);
Assert.False(result.Value.AlreadyExists);
Assert.Equal(source.Id.Value, result.Value.ConversationId);
Assert.Equal(node.Id.Value, result.Value.CurrentNodeId);
Assert.Equal(source.Title.Value, result.Value.Title);
Assert.Equal(UtcNow, result.Value.CreatedAt);
Assert.Single(sharedChats.Items);
~~~

For the existing case, seed an older SharedChat and assert its ID, title, and timestamp are returned unchanged with AlreadyExists true.

- [ ] **Step 2: Run tests and verify compile failure**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~CreateSharedChat"
~~~

Expected: FAIL because the command, validator, handler, result, and fake repository do not exist.

- [ ] **Step 3: Add command, validator, and result**

~~~csharp
public sealed record CreateSharedChatCommand
(
    Guid ConversationId,
    Guid CurrentNodeId
) : ICommand<ErrorOr<CreateSharedChatResult>>;

internal sealed class CreateSharedChatCommandValidator : AbstractValidator<CreateSharedChatCommand>
{
    public CreateSharedChatCommandValidator()
    {
        RuleFor(x => x.ConversationId).NotEmpty();
        RuleFor(x => x.CurrentNodeId).NotEmpty();
    }
}

public sealed record CreateSharedChatResult
(
    Guid Id,
    string Title,
    Guid ConversationId,
    Guid CurrentNodeId,
    DateTimeOffset CreatedAt,
    bool AlreadyExists
);
~~~

- [ ] **Step 4: Implement the handler**

Use IUserContext, IChatRepository, ISharedChatRepository, and IDateTimeProvider. The core flow must be:

~~~csharp
ChatThread? source = await chats.GetSnapshotByIdAsync(chatId, userId, cancellationToken);

if (source is null)
    return ChatOperationErrors.ChatNotFound(chatId);

ErrorOr<SharedChat> candidateResult = SharedChat.Create
(
    source,
    currentNodeId,
    dateTimeProvider.UtcNow
);

if (candidateResult.IsError)
    return candidateResult.Errors;

SharedChat? existing = await sharedChats.GetBySourceAsync
(
    userId,
    chatId,
    currentNodeId,
    cancellationToken
);

if (existing is not null)
    return ToResult(existing, alreadyExists: true);

SharedChat candidate = candidateResult.Value;
bool inserted = await sharedChats.TryAddAsync(candidate, cancellationToken);

if (inserted)
    return ToResult(candidate, alreadyExists: false);

SharedChat concurrentWinner = await sharedChats.GetBySourceAsync
(
    userId,
    chatId,
    currentNodeId,
    cancellationToken
) ?? throw new InvalidOperationException("Conflicting shared chat row was not visible.");

return ToResult(concurrentWinner, alreadyExists: true);
~~~

Aggregate value-object validation errors before repository access, following GetChatHandler.

- [ ] **Step 5: Implement the fake repository**

The fake stores a List<SharedChat>, exposes Items, and implements TryAddAsync as:

~~~csharp
if (_items.Any(x => x.ConversationId == sharedChat.ConversationId &&
                    x.CurrentNodeId == sharedChat.CurrentNodeId))
{
    return Task.FromResult(false);
}

_items.Add(sharedChat);
return Task.FromResult(true);
~~~

GetBySourceAsync and delete methods must also compare UserId so ownership tests are meaningful.

- [ ] **Step 6: Re-run tests and commit**

Request elevated permission and rerun Step 2. Expected: PASS.

~~~bash
git add src/services/Chat/Chat.Application/SharedChats tests/Chat/Chat.Application.Tests/SharedChats
git commit -m "feat(chat): add shared chat creation flow"
~~~

---

### Task 4: Add PostgreSQL persistence and atomic idempotency

**Files:**
- Modify: Directory.Packages.props
- Modify: Nova.slnx
- Create: tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj
- Create: tests/Chat/Chat.Infrastructure.Tests/Database/PostgreSqlFixture.cs
- Create: tests/Chat/Chat.Infrastructure.Tests/SharedChats/SharedChatRepositoryTests.cs
- Create: src/services/Chat/Chat.Infrastructure/Properties/AssemblyInfo.cs
- Create: src/services/Chat/Chat.Infrastructure/SharedChats/Configurations/SharedChatConfiguration.cs
- Create: src/services/Chat/Chat.Infrastructure/SharedChats/Repositories/SharedChatRepository.cs
- Modify: src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatMessageConfiguration.cs
- Modify: src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs
- Modify: src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
- Create: generated ChatSharing migration and designer
- Modify: src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs

- [ ] **Step 1: Add the real-PostgreSQL test project**

Pin Testcontainers.PostgreSql 4.12.0 in Directory.Packages.props. Create the test project with xUnit packages, Testcontainers.PostgreSql, and project references to Chat.Domain and Chat.Infrastructure. Add it to Nova.slnx under /tests/Chat/.

Add:

~~~csharp
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Chat.Infrastructure.Tests")]
~~~

to Chat.Infrastructure/Properties/AssemblyInfo.cs.

- [ ] **Step 2: Create the PostgreSQL fixture**

Use PostgreSqlBuilder with image postgres:17-alpine. Start the container in InitializeAsync, build an NpgsqlDataSource from GetConnectionString, create ChatDbContext with UseNpgsql and UseSnakeCaseNamingConvention, and call Database.MigrateAsync. Dispose the data source and container in DisposeAsync.

Expose:

~~~csharp
public NpgsqlDataSource DataSource { get; private set; } = default!;
public ChatDbContext CreateDbContext() => new(CreateOptions(), new NoOpDomainEventsDispatcher());
~~~

Disable test parallelization for the fixture collection so table cleanup is deterministic.

- [ ] **Step 3: Write failing repository integration tests**

Add tests that seed one source ChatThread through ChatDbContext, then assert:

~~~csharp
Assert.True(await repository.TryAddAsync(first));
Assert.False(await repository.TryAddAsync(secondForSamePair));

SharedChat? stored = await repository.GetBySourceAsync
(
    source.UserId,
    source.Id,
    node.Id
);

Assert.Equal(first.Id, stored!.Id);
~~~

Add a concurrent test using Task.WhenAll with two repository instances and assert one true result, one false result, and one database row.

- [ ] **Step 4: Run integration tests and verify failure**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SharedChatRepositoryTests"
~~~

Expected: FAIL because SharedChat is not mapped, the repository is absent, and shared_chats does not exist.

- [ ] **Step 5: Map SharedChat and selected-node integrity**

Configure:

~~~csharp
builder.ToTable("shared_chats");
builder.HasKey(x => x.Id);
builder.Property(x => x.Id).HasConversion(id => id.Value, SharedChatId.FromDatabase).ValueGeneratedNever();
builder.Property(x => x.UserId).HasConversion(id => id.Value, UserId.FromDatabase).IsRequired();
builder.Property(x => x.ConversationId).HasConversion(id => id.Value, ChatId.FromDatabase).IsRequired();
builder.Property(x => x.CurrentNodeId).HasConversion(id => id.Value, ChatMessageId.FromDatabase).IsRequired();
builder.Property(x => x.Title).HasConversion(title => title.Value, ChatTitle.FromDatabase).HasMaxLength(ChatTitle.MaxLength).IsRequired();
builder.Property(x => x.CreatedAt).IsRequired();
builder.HasIndex(x => new { x.ConversationId, x.CurrentNodeId }).IsUnique();
builder.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id }).IsDescending(false, true, true);
builder.HasOne<ChatThread>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
builder.HasOne<ChatMessage>()
    .WithMany()
    .HasForeignKey(x => new { x.ConversationId, x.CurrentNodeId })
    .HasPrincipalKey(x => new { x.ChatId, x.Id })
    .OnDelete(DeleteBehavior.Cascade);
builder.Ignore(x => x.DomainEvents);
~~~

Add a unique index on ChatMessage (ChatId, Id), add DbSet<SharedChat> SharedChats to ChatDbContext, and register ISharedChatRepository.

- [ ] **Step 6: Implement atomic repository methods**

TryAddAsync opens an Npgsql connection and executes:

~~~sql
insert into shared_chats
    (id, user_id, conversation_id, current_node_id, title, created_at)
values
    (@Id, @UserId, @ConversationId, @CurrentNodeId, @Title, @CreatedAt)
on conflict (conversation_id, current_node_id) do nothing;
~~~

Return affectedRows == 1. Implement GetBySourceAsync with AsNoTracking EF, DeleteAsync with owner-scoped ExecuteDeleteAsync, and DeleteAllAsync with owner-scoped ExecuteDeleteAsync.

- [ ] **Step 7: Generate and inspect the migration**

Request elevated permission:

~~~bash
dotnet ef migrations add ChatSharing --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj --context ChatDbContext --output-dir Database/Migrations
~~~

Expected: the migration creates shared_chats, the unique pair/indexes, both cascade foreign keys, and the chat_messages composite unique key. Its Down method removes them in reverse dependency order.

- [ ] **Step 8: Re-run integration tests and commit**

Request elevated permission and rerun Step 4. Expected: PASS.

~~~bash
git add Directory.Packages.props Nova.slnx src/services/Chat/Chat.Infrastructure tests/Chat/Chat.Infrastructure.Tests
git commit -m "feat(chat): persist shared chat links"
~~~

---

### Task 5: Add paginated owner listing

**Files:**
- Create: src/services/Chat/Chat.Application/SharedChats/SharedChatLimits.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Queries/GetSharedChats/GetSharedChatsQuery.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Queries/GetSharedChats/GetSharedChatsQueryValidator.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Queries/GetSharedChats/GetSharedChatsHandler.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Queries/GetSharedChats/ISharedChatListReader.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Queries/GetSharedChats/SharedChatListReadModel.cs
- Create: tests/Chat/Chat.Application.Tests/SharedChats/GetSharedChatsHandlerTests.cs
- Create: src/services/Chat/Chat.Infrastructure/SharedChats/Readers/SharedChatListReader.cs
- Create: tests/Chat/Chat.Infrastructure.Tests/SharedChats/SharedChatListReaderTests.cs
- Modify: src/services/Chat/Chat.Infrastructure/DependencyInjection.cs

- [ ] **Step 1: Write failing application tests**

Use a FakeSharedChatListReader that records UserId, Limit, and Offset. Verify:

~~~csharp
ErrorOr<SharedChatListReadModel> result = await handler.Handle
(
    new GetSharedChatsQuery(Limit: 50, Offset: 25),
    CancellationToken.None
);

Assert.False(result.IsError);
Assert.Equal("auth0|user-1", reader.UserId!.Value);
Assert.Equal(50, reader.Limit);
Assert.Equal(25, reader.Offset);
~~~

Add validator theories for limit 0, limit 101, and offset -1. Each must produce validation errors.

- [ ] **Step 2: Add limits, read models, query, and handler**

~~~csharp
public static class SharedChatLimits
{
    public const int DefaultLimit = 50;
    public const int MinLimit = 1;
    public const int MaxLimit = 100;
    public const int DefaultOffset = 0;
}

public sealed record SharedChatSummaryReadModel
(
    Guid Id,
    string Title,
    Guid ConversationId,
    Guid CurrentNodeId,
    DateTimeOffset CreatedAt
);

public sealed record SharedChatListReadModel
(
    IReadOnlyList<SharedChatSummaryReadModel> Items,
    int Total,
    int Limit,
    int Offset
);

public sealed record GetSharedChatsQuery(int Limit, int Offset)
    : IQuery<ErrorOr<SharedChatListReadModel>>;
~~~

The handler resolves UserId from IUserContext and calls:

~~~csharp
return await reader.GetAsync(userIdResult.Value, query.Limit, query.Offset, cancellationToken);
~~~

The validator uses InclusiveBetween(1, 100) and GreaterThanOrEqualTo(0).

- [ ] **Step 3: Run application tests**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~GetSharedChats"
~~~

Expected: PASS after adding the types and handler.

- [ ] **Step 4: Write the failing PostgreSQL reader test**

Seed three shares for one owner at distinct CreatedAt values and one share for another owner. Request limit 2, offset 1. Assert total is 3 and returned IDs are the second and third newest owner rows.

- [ ] **Step 5: Implement SharedChatListReader**

Use one QueryMultipleAsync call:

~~~sql
select count(*)
from shared_chats
where user_id = @UserId;

select
    id              as "Id",
    title           as "Title",
    conversation_id as "ConversationId",
    current_node_id as "CurrentNodeId",
    created_at      as "CreatedAt"
from shared_chats
where user_id = @UserId
order by created_at desc, id desc
limit @Limit offset @Offset;
~~~

Map rows to SharedChatSummaryReadModel and return SharedChatListReadModel. Register ISharedChatListReader in AddReaders.

- [ ] **Step 6: Run infrastructure tests and commit**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SharedChatListReaderTests"
~~~

Expected: PASS.

~~~bash
git add src/services/Chat/Chat.Application/SharedChats src/services/Chat/Chat.Infrastructure/SharedChats/Readers/SharedChatListReader.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs tests/Chat/Chat.Application.Tests/SharedChats tests/Chat/Chat.Infrastructure.Tests/SharedChats/SharedChatListReaderTests.cs
git commit -m "feat(chat): list shared chat links"
~~~

---

### Task 6: Add owner-scoped single and bulk deletion

**Files:**
- Create: src/services/Chat/Chat.Application/SharedChats/Errors/SharedChatOperationErrors.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Commands/DeleteSharedChat/DeleteSharedChatCommand.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Commands/DeleteSharedChat/DeleteSharedChatCommandValidator.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Commands/DeleteSharedChat/DeleteSharedChatHandler.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Commands/DeleteAllSharedChats/DeleteAllSharedChatsCommand.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Commands/DeleteAllSharedChats/DeleteAllSharedChatsHandler.cs
- Create: tests/Chat/Chat.Application.Tests/SharedChats/DeleteSharedChatHandlerTests.cs
- Create: tests/Chat/Chat.Application.Tests/SharedChats/DeleteAllSharedChatsHandlerTests.cs
- Modify: tests/Chat/Chat.Infrastructure.Tests/SharedChats/SharedChatRepositoryTests.cs

- [ ] **Step 1: Write failing handler tests**

Cover:

~~~csharp
[Fact] public async Task DeleteOneRemovesOwnedShare()
[Fact] public async Task DeleteOneReturnsNotFoundForForeignShare()
[Fact] public async Task DeleteOneReturnsNotFoundForMissingShare()
[Fact] public async Task DeleteAllRemovesOnlyCurrentUsersShares()
[Fact] public async Task DeleteAllSucceedsWhenOwnerHasNoShares()
~~~

For the bulk result, assert the returned Success and fake repository state rather than exposing a deletion count through HTTP.

- [ ] **Step 2: Add commands, error, and handlers**

~~~csharp
public sealed record DeleteSharedChatCommand(Guid SharedChatId)
    : ICommand<ErrorOr<Success>>;

public sealed record DeleteAllSharedChatsCommand
    : ICommand<ErrorOr<Success>>;

public static Error NotFound(SharedChatId id) =>
    Error.NotFound
    (
        code: "SharedChat.NotFound",
        description: $"No shared chat found with id '{id.Value}'."
    );
~~~

DeleteSharedChatHandler validates UserId and SharedChatId, calls DeleteAsync(id, userId), and returns NotFound when false. DeleteAllSharedChatsHandler validates UserId, calls DeleteAllAsync, ignores the count, and returns Result.Success.

- [ ] **Step 3: Run application tests**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~DeleteSharedChat|FullyQualifiedName~DeleteAllSharedChats"
~~~

Expected: PASS.

- [ ] **Step 4: Add repository ownership and cascade tests**

Using PostgreSQL, prove that:

~~~csharp
Assert.False(await repository.DeleteAsync(ownerShare.Id, anotherUser));
Assert.True(await repository.DeleteAsync(ownerShare.Id, owner));
Assert.Equal(2, await repository.DeleteAllAsync(owner));
Assert.NotNull(await repository.GetBySourceAsync(anotherUser, otherChat.Id, otherNode.Id));
~~~

Also delete a source ChatThread through ChatDbContext and assert its SharedChat row is absent afterward.

- [ ] **Step 5: Run infrastructure tests and commit**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj --filter "FullyQualifiedName~SharedChatRepositoryTests"
~~~

Expected: PASS.

~~~bash
git add src/services/Chat/Chat.Application/SharedChats tests/Chat/Chat.Application.Tests/SharedChats tests/Chat/Chat.Infrastructure.Tests/SharedChats/SharedChatRepositoryTests.cs
git commit -m "feat(chat): revoke shared chat links"
~~~

---

### Task 7: Implement anonymous root-to-selected-node reads

**Files:**
- Create: src/services/Chat/Chat.Application/SharedChats/Queries/GetPublicSharedChat/GetPublicSharedChatQuery.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Queries/GetPublicSharedChat/GetPublicSharedChatHandler.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Queries/GetPublicSharedChat/IPublicSharedChatReader.cs
- Create: src/services/Chat/Chat.Application/SharedChats/Queries/GetPublicSharedChat/PublicSharedChatReadModel.cs
- Create: tests/Chat/Chat.Application.Tests/SharedChats/GetPublicSharedChatHandlerTests.cs
- Create: src/services/Chat/Chat.Infrastructure/SharedChats/Readers/PublicSharedChatReader.cs
- Create: tests/Chat/Chat.Infrastructure.Tests/SharedChats/PublicSharedChatReaderTests.cs
- Modify: src/services/Chat/Chat.Infrastructure/DependencyInjection.cs

- [ ] **Step 1: Write the public-query handler tests**

The handler must not depend on IUserContext. Test valid, empty-ID, and missing-link cases:

~~~csharp
ErrorOr<PublicSharedChatReadModel> result = await handler.Handle
(
    new GetPublicSharedChatQuery(sharedChatId),
    CancellationToken.None
);

Assert.False(result.IsError);
Assert.Equal(sharedChatId, result.Value.Id);
~~~

Missing links return SharedChat.NotFound with ErrorType.NotFound.

- [ ] **Step 2: Add public query contracts**

~~~csharp
public sealed record PublicSharedChatMessageReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    MessageRole Role,
    string? Content,
    MessageStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt
);

public sealed record PublicSharedChatReadModel
(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    Guid CurrentNodeId,
    IReadOnlyList<PublicSharedChatMessageReadModel> Messages
);

public sealed record GetPublicSharedChatQuery(Guid SharedChatId)
    : IQuery<ErrorOr<PublicSharedChatReadModel>>;
~~~

IPublicSharedChatReader.GetAsync accepts SharedChatId and returns nullable PublicSharedChatReadModel. The handler validates the value object, calls the reader, and maps null to SharedChatOperationErrors.NotFound.

- [ ] **Step 3: Run handler tests**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~GetPublicSharedChat"
~~~

Expected: PASS.

- [ ] **Step 4: Write the path-isolation integration test**

Build:

~~~text
root user -> first assistant -> second user -> original assistant (shared)
                                      -> regenerated assistant (current head)
~~~

Share original assistant, regenerate so the current chat head is the sibling, call PublicSharedChatReader, and assert returned IDs equal only root, first assistant, second user, and original assistant in that order. Assert the regenerated ID is absent.

Add corruption tests by changing an ancestor parent ID with SQL; the reader must throw InvalidOperationException rather than return a partial path.

- [ ] **Step 5: Implement the recursive Dapper reader**

Use a cycle-safe CTE anchored by share ID:

~~~sql
with recursive share as
(
    select id, title, created_at, conversation_id, current_node_id
    from shared_chats
    where id = @SharedChatId
),
path as
(
    select
        m.id,
        m.chat_id,
        m.parent_message_id,
        m.role,
        m.content,
        m.status,
        m.created_at,
        m.completed_at,
        0 as depth,
        array[m.id] as visited
    from chat_messages m
    join share s
      on s.conversation_id = m.chat_id
     and s.current_node_id = m.id

    union all

    select
        parent.id,
        parent.chat_id,
        parent.parent_message_id,
        parent.role,
        parent.content,
        parent.status,
        parent.created_at,
        parent.completed_at,
        child.depth + 1,
        child.visited || parent.id
    from path child
    join chat_messages parent
      on parent.chat_id = child.chat_id
     and parent.id = child.parent_message_id
    where not parent.id = any(child.visited)
)
select id, title, created_at, current_node_id
from share;

select id, parent_message_id, role, content, status, created_at, completed_at, depth
from path
order by depth desc;
~~~

After reading, verify:

- at least one path row exists;
- the first row has no parent and role User;
- the last row ID equals CurrentNodeId;
- each row's ParentMessageId equals the preceding row ID;
- IDs are distinct.

Throw InvalidOperationException("Shared chat ancestry is invalid.") for any violation. Register IPublicSharedChatReader in AddReaders.

- [ ] **Step 6: Run integration tests and commit**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj --filter "FullyQualifiedName~PublicSharedChatReaderTests"
~~~

Expected: PASS.

~~~bash
git add src/services/Chat/Chat.Application/SharedChats/Queries/GetPublicSharedChat src/services/Chat/Chat.Infrastructure/SharedChats/Readers/PublicSharedChatReader.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs tests/Chat/Chat.Application.Tests/SharedChats/GetPublicSharedChatHandlerTests.cs tests/Chat/Chat.Infrastructure.Tests/SharedChats/PublicSharedChatReaderTests.cs
git commit -m "feat(chat): read public shared chat paths"
~~~

---

### Task 8: Add authenticated owner HTTP endpoints and canonical URLs

**Files:**
- Create: src/services/Chat/Chat.Api/Options/SharedLinksOptions.cs
- Create: src/services/Chat/Chat.Api/SharedChats/SharedLinkUrlBuilder.cs
- Create: src/services/Chat/Chat.Api/Endpoints/SharedChats/CreateSharedChat/Endpoint.cs
- Create: src/services/Chat/Chat.Api/Endpoints/SharedChats/ListSharedChats/Endpoint.cs
- Create: src/services/Chat/Chat.Api/Endpoints/SharedChats/DeleteSharedChat/Endpoint.cs
- Create: src/services/Chat/Chat.Api/Endpoints/SharedChats/DeleteAllSharedChats/Endpoint.cs
- Modify: src/services/Chat/Chat.Api/Endpoints/CustomTags.cs
- Modify: src/services/Chat/Chat.Api/DependencyInjection.cs
- Modify: src/services/Chat/Chat.Api/appsettings.Development.json
- Create: tests/Chat/Chat.Api.Tests/Chat.Api.Tests.csproj
- Create: src/services/Chat/Chat.Api/Properties/AssemblyInfo.cs
- Create: tests/Chat/Chat.Api.Tests/SharedChats/SharedLinkUrlBuilderTests.cs
- Create: tests/Chat/Chat.Api.Tests/SharedChats/OwnerEndpointContractTests.cs
- Modify: Nova.slnx

- [ ] **Step 1: Create the API test project and failing URL tests**

Reference Chat.Api and existing xUnit packages. Add InternalsVisibleTo("Chat.Api.Tests") in Chat.Api/Properties/AssemblyInfo.cs.

Test:

~~~csharp
[Fact]
public void BuildCreatesCanonicalFrontendShareUrl()
{
    SharedLinkUrlBuilder builder = CreateBuilder("https://nova.example/base/");

    string result = builder.Build(Guid.Parse("03f5233b-37f9-4bf0-b18a-f5f43622573c"));

    Assert.Equal
    (
        "https://nova.example/base/share/03f5233b-37f9-4bf0-b18a-f5f43622573c",
        result
    );
}
~~~

Add option-validation cases for relative URLs and non-loopback HTTP URLs.

- [ ] **Step 2: Implement options and URL builder**

~~~csharp
internal sealed class SharedLinksOptions
{
    public const string SectionName = "SharedLinks";
    public required string PublicBaseUrl { get; init; }
}

internal sealed class SharedLinkUrlBuilder(IOptions<SharedLinksOptions> options)
{
    private readonly Uri _baseUri = new(options.Value.PublicBaseUrl.TrimEnd('/') + "/");

    public string Build(Guid sharedChatId) =>
        new Uri(_baseUri, $"share/{sharedChatId}").AbsoluteUri;
}
~~~

Bind and validate on start. Accept HTTPS, or HTTP only when Uri.IsLoopback. Add development configuration:

~~~json
"SharedLinks": {
  "PublicBaseUrl": "https://localhost:7001"
}
~~~

- [ ] **Step 3: Implement create and list endpoints**

Create request:

~~~csharp
internal sealed record Request(Guid ConversationId, Guid CurrentNodeId);
~~~

Create response fields are ShareId, ShareUrl, Title, ConversationId, CurrentNodeId, CreatedAt, AlreadyExists. Send 201 plus Location for a new row and 200 for an existing row.

List request uses nullable QueryParam Limit and Offset. Default through SharedChatLimits. List response contains Items, Total, Limit, Offset, and each item includes Id, ShareUrl, Title, ConversationId, CurrentNodeId, CreatedAt.

Routes:

~~~csharp
Post("/me/shared-chats");
Get("/me/shared-chats");
Version(1);
~~~

- [ ] **Step 4: Implement deletion endpoints**

Routes:

~~~csharp
Delete("/me/shared-chats/{shareId}");
Delete("/me/shared-chats");
Version(1);
~~~

Send DeleteSharedChatCommand for the route ID and DeleteAllSharedChatsCommand for bulk deletion. Return 204 on success through BaseEndpoint or the existing CustomResults pattern.

- [ ] **Step 5: Add endpoint contract tests**

Use FastEndpoints.Factory.Create with the real generated Mediator sender from a ServiceCollection configured by AddApplication and fake repositories/readers. Assert:

- create maps route/body IDs and returns 201 or 200 correctly;
- list applies defaults 50 and 0;
- delete-one places the route shareId into the command;
- delete-all sends the parameterless command;
- generated response URLs use SharedLinkUrlBuilder;
- all four endpoint definitions omit AllowAnonymous and carry the Shared Chats tag.

- [ ] **Step 6: Run API tests and commit**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Api.Tests/Chat.Api.Tests.csproj --filter "FullyQualifiedName~SharedChats"
~~~

Expected: PASS.

~~~bash
git add Nova.slnx src/services/Chat/Chat.Api tests/Chat/Chat.Api.Tests
git commit -m "feat(chat): expose shared link management endpoints"
~~~

---

### Task 9: Add the public endpoint, security headers, rate limiting, and concurrency mapping

**Files:**
- Create: src/services/Chat/Chat.Api/Endpoints/SharedChats/GetPublicSharedChat/Endpoint.cs
- Create: src/services/Chat/Chat.Api/Endpoints/SharedChats/GetPublicSharedChat/Response.cs
- Create: src/services/Chat/Chat.Api/Endpoints/SharedChats/GetPublicSharedChat/ResponseMapper.cs
- Create: src/services/Chat/Chat.Api/Security/PublicSharedChatRateLimit.cs
- Create: src/services/Chat/Chat.Api/Infrastructure/ChatConcurrencyExceptionHandler.cs
- Modify: src/services/Chat/Chat.Api/DependencyInjection.cs
- Modify: src/services/Chat/Chat.Api/Program.cs
- Create: tests/Chat/Chat.Api.Tests/SharedChats/PublicEndpointTests.cs
- Create: tests/Chat/Chat.Api.Tests/Infrastructure/ChatConcurrencyExceptionHandlerTests.cs

- [ ] **Step 1: Write failing response-mapping and endpoint tests**

Use a two-message PublicSharedChatReadModel. Assert the mapped root has one child, the leaf has no children, and owner/source metadata is absent from the response type.

Create the endpoint with FastEndpoints.Factory and a real Mediator sender backed by a fake IPublicSharedChatReader. Call HandleAsync and assert:

~~~csharp
Assert.Equal("no-store", endpoint.HttpContext.Response.Headers.CacheControl);
Assert.Equal("noindex, nofollow", endpoint.HttpContext.Response.Headers["X-Robots-Tag"]);
Assert.Equal(StatusCodes.Status200OK, endpoint.HttpContext.Response.StatusCode);
~~~

Verify Configure calls AllowAnonymous and requires the named public-share rate-limit policy.

- [ ] **Step 2: Implement the public response**

Use:

~~~csharp
internal sealed record MessageResponse
(
    string Role,
    string? Content,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt
);

internal sealed record MappingNodeResponse
(
    Guid Id,
    Guid? ParentId,
    IReadOnlyList<Guid> Children,
    MessageResponse Message
);
~~~

Response contains Id, Title, CreatedAt, CurrentNode, and IReadOnlyDictionary<string, MappingNodeResponse> Mapping. Build children from the returned linear message list; do not expose source ConversationId, UserId, model metadata, sibling indexes, or failure details.

- [ ] **Step 3: Implement the anonymous endpoint and rate policy**

Configure:

~~~csharp
Get("/public/shared-chats/{shareId}");
Version(1);
AllowAnonymous();
Options(builder => builder.RequireRateLimiting(PublicSharedChatRateLimit.PolicyName));
~~~

Set Cache-Control and X-Robots-Tag before sending either success or not-found results.

Register a fixed-window policy with 60 permits per minute per remote IP, queue limit zero, and auto-replenishment. Add app.UseRateLimiter before app.UseFastEndpoints.

Use this policy registration:

~~~csharp
services.AddRateLimiter(options =>
{
    options.AddPolicy(PublicSharedChatRateLimit.PolicyName, context =>
        RateLimitPartition.GetFixedWindowLimiter
        (
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }
        ));
});
~~~

- [ ] **Step 4: Write and implement concurrency exception tests**

Construct DefaultHttpContext with a MemoryStream response body, call ChatConcurrencyExceptionHandler.TryHandleAsync with DbUpdateConcurrencyException, and assert true, status 409, and a ProblemDetails title of Concurrency conflict. Pass InvalidOperationException and assert false.

Register ChatConcurrencyExceptionHandler before AddSharedApi, add app.UseExceptionHandler, and keep GlobalExceptionHandler as the fallback. The application layer must not reference EF Core.

- [ ] **Step 5: Run API tests and commit**

Request elevated permission:

~~~bash
dotnet test tests/Chat/Chat.Api.Tests/Chat.Api.Tests.csproj
~~~

Expected: PASS.

~~~bash
git add src/services/Chat/Chat.Api tests/Chat/Chat.Api.Tests
git commit -m "feat(chat): expose secure public shared chats"
~~~

---

### Task 10: Add the anonymous GET-only BFF proxy route

**Files:**
- Modify: src/services/BFF/RemoteApis/ChatApiProxyConfiguration.cs
- Modify: src/services/BFF/Program.cs
- Create: src/services/BFF/Properties/AssemblyInfo.cs
- Create: tests/BFF/BFF.Tests/BFF.Tests.csproj
- Create: tests/BFF/BFF.Tests/RemoteApis/ChatApiProxyConfigurationTests.cs
- Modify: Nova.slnx

- [ ] **Step 1: Write failing route-configuration tests**

Assert the public route:

~~~csharp
Assert.Equal("/api/chat/v1/public/shared-chats/{shareId}", route.Match.Path);
Assert.Equal(["GET"], route.Match.Methods);
Assert.Empty(route.Metadata ?? []);
Assert.Contains(route.Transforms!, transform => transform["PathPattern"] == "/v1/public/shared-chats/{shareId}");
~~~

Also assert the existing authenticated catch-all has nonempty metadata after WithAccessToken(RequiredTokenType.User) and WithAntiforgeryCheck are applied.

- [ ] **Step 2: Expose internals to BFF.Tests and add project**

Create AssemblyInfo with InternalsVisibleTo("BFF.Tests"), create the xUnit project referencing BFF.csproj, and add it to Nova.slnx under /tests/BFF/.

- [ ] **Step 3: Refactor Chat API route creation**

Replace CreateRoute with CreateRoutes returning:

~~~csharp
[
    new RouteConfig
    {
        RouteId = "chat-api-public-shared-chats",
        ClusterId = ClusterId,
        Order = -100,
        Match = new RouteMatch
        {
            Path = "/api/chat/v1/public/shared-chats/{shareId}",
            Methods = ["GET"]
        },
        Transforms =
        [
            new Dictionary<string, string>
            {
                ["PathPattern"] = "/v1/public/shared-chats/{shareId}"
            }
        ]
    },
    CreateAuthenticatedRoute()
]
~~~

Do not call WithAccessToken on the public route. Keep WithAccessToken(RequiredTokenType.User) and WithAntiforgeryCheck on the existing authenticated route.

Update BFF Program to spread ChatApiProxyConfiguration.CreateRoutes into LoadFromMemory.

- [ ] **Step 4: Run BFF tests and commit**

Request elevated permission:

~~~bash
dotnet test tests/BFF/BFF.Tests/BFF.Tests.csproj
~~~

Expected: PASS.

~~~bash
git add Nova.slnx src/services/BFF tests/BFF/BFF.Tests
git commit -m "feat(bff): proxy public shared chats"
~~~

---

### Task 11: Run complete verification and inspect contracts

**Files:**
- Verify all files changed by Tasks 1-10.
- Modify only files required to correct failures discovered by these checks.

- [ ] **Step 1: Run formatting and static diff checks**

~~~bash
dotnet format Nova.slnx --verify-no-changes
git diff --check
~~~

Request elevated permission before dotnet format. Expected: both commands succeed with no output indicating formatting errors.

- [ ] **Step 2: Run all tests**

Request elevated permission:

~~~bash
dotnet test Nova.slnx --no-restore
~~~

Expected: every Domain, Application, Infrastructure, API, and BFF test passes.

- [ ] **Step 3: Build the complete solution**

Request elevated permission:

~~~bash
dotnet build Nova.slnx --no-restore
~~~

Expected: BUILD SUCCEEDED with zero warnings and zero errors.

- [ ] **Step 4: Inspect the migration SQL**

Request elevated permission:

~~~bash
dotnet ef migrations script --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj --context ChatDbContext --no-transactions
~~~

Confirm the output contains:

- shared_chats with UUID, owner, source pair, frozen title, and timestamptz fields;
- unique source pair;
- owner/newest index;
- source chat and selected-node cascade foreign keys;
- no copied-message or JSON snapshot table.

- [ ] **Step 5: Review OpenAPI and route behavior manually**

Request elevated permission before starting the API:

~~~bash
dotnet run --project src/services/Chat/Chat.Api/Chat.Api.csproj
~~~

Confirm Scalar/OpenAPI advertises:

~~~text
POST   /v1/me/shared-chats
GET    /v1/me/shared-chats
DELETE /v1/me/shared-chats/{shareId}
DELETE /v1/me/shared-chats
GET    /v1/public/shared-chats/{shareId}
~~~

Stop the process after inspection. Confirm only the final GET endpoint is anonymous.

- [ ] **Step 6: Review scope and commit verification corrections**

Run git status --short. Confirm there is no frontend implementation, no unsupported moderation/PII fields, no message-copy table, no MediatR package, and no MassTransit version change.

If verification required corrections, stage only those corrected files and commit:

~~~bash
git commit -m "test(chat): verify shared chat links"
~~~

If no files changed during verification, do not create an empty commit.
