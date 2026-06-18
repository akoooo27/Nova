# Get Chat With Messages Query Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `GET /chats/{chatId}` that returns a chat's metadata plus its full message tree as a ChatGPT-style `mapping` (keyed by message id, each node `{ id, parentId, children[], message }`) and `currentNode`, with no message pagination.

**Architecture:** A CQRS read slice mirroring `GetChats`: `GetChatQuery` → `ValidationBehavior` → `GetChatHandler` resolves `UserId`/`ChatId` and delegates to `IChatDetailReader`; a Dapper `ChatDetailReader` runs an owner-scoped chat row + a messages query (LEFT JOIN to `llm_models`) in one `QueryMultipleAsync`. The reader returns a flat read model; an Application-layer `ChatMappingBuilder` turns the flat messages into tree nodes (the tested logic), and a thin Api `ResponseMapper` renders those nodes to the wire `mapping` (lowercasing `role`/`status`).

**Tech Stack:** .NET, `Mediator` (source-generated, not MediatR), FastEndpoints (`BaseEndpoint` + `SendErrorOrAsync`), FluentValidation, Dapper 2.1.79 over `NpgsqlDataSource`, ErrorOr, xUnit.

## Global Constraints

- Use the `Mediator` package family (`IQuery`/`IQueryHandler`); do **not** introduce MediatR.
- Use FastEndpoints; do **not** use ASP.NET Core controllers. This endpoint uses `BaseEndpoint<Request, Response>` + `SendErrorOrAsync` (the `UpdateChat` style).
- Read side uses Dapper over `NpgsqlDataSource`; EF Core is not involved.
- `chats` columns (snake_case): `id`, `user_id`, `title`, `pinned_at`, `is_archived`, `is_temporary`, `created_at`, `updated_at`, `current_message_id`.
- `chat_messages` columns: `id`, `chat_id`, `parent_message_id`, `role`, `content`, `status`, `llm_model_id`, `failure_reason`, `created_at`, `completed_at`, `sibling_index`. `role`/`status` are persisted as enum **names** (`HasConversion<string>`), e.g. `"User"`, `"Assistant"`, `"Generating"`, `"Completed"`, `"Failed"`.
- `llm_models` columns used: `id`, `external_model_id` (the slug), `name`.
- The whole tree is returned in one call — **no pagination**.
- Chat missing **or owned by another user** → `404 Chat.NotFound` (never distinguish the two). Build `ChatId` via `ChatId.Create(Guid)`.
- On the wire, `role`/`status` are **lowercased** (`"user"`, `"assistant"`, `"completed"`…); roots have `parentId = null` (no synthetic root node).
- **Spec revision:** the spec said build the mapping in the Api `ResponseMapper`. This plan instead builds the tree in an Application-layer `ChatMappingBuilder` (tested in `Chat.Application.Tests`, like `ModelCatalogResultMapperTests`); the Api `ResponseMapper` is a thin shim. This keeps the testable logic inside the repo's existing test boundary (Application + Domain only).
- Tests included per explicit user request: `GetChatQueryValidator`, `GetChatHandler`, and `ChatMappingBuilder`. The Dapper reader and the thin Api mapper are manually verified (no Infrastructure/Api test project).
- Commit messages follow the repo convention `feat(chat): ...` / `test(chat): ...` with **no co-author trailer**.
- `[Fact]`/`[Theory]`/`Assert` come from `global using Xunit;` in `tests/Chat/Chat.Application.Tests/GlobalUsings.cs` — do not add `using Xunit;`. Reuse the existing `FakeUserContext` from `Chat.Application.Tests.FavoriteModels` (the `UpdateChatHandlerTests` do this).

---

### Task 1: Application contract + query validation

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMessageModelReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMessageReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatDetailReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/GetChatQuery.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/IChatDetailReader.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/GetChatQueryValidator.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatQueryValidatorTests.cs`

**Interfaces:**
- Consumes: `MessageRole`, `MessageStatus` (`Chat.Domain.Chats.ValueObjects`); `ChatId`, `UserId` (`Chat.Domain.Shared`).
- Produces:
  - `GetChatQuery(Guid ChatId) : IQuery<ErrorOr<ChatDetailReadModel>>`
  - `ChatDetailReadModel(Guid Id, string Title, bool IsPinned, DateTimeOffset? PinnedAt, bool IsArchived, bool IsTemporary, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid CurrentMessageId, IReadOnlyList<ChatMessageReadModel> Messages)`
  - `ChatMessageReadModel(Guid Id, Guid? ParentMessageId, MessageRole Role, string? Content, MessageStatus Status, string? FailureReason, int SiblingIndex, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, ChatMessageModelReadModel? Model)`
  - `ChatMessageModelReadModel(Guid Id, string? Slug, string? Name)`
  - `IChatDetailReader.GetAsync(ChatId chatId, UserId userId, CancellationToken cancellationToken) -> Task<ChatDetailReadModel?>`
  - `GetChatQueryValidator : AbstractValidator<GetChatQuery>`

- [ ] **Step 1: Write the failing validator tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatQueryValidatorTests.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChat;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class GetChatQueryValidatorTests
{
    private readonly GetChatQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsNonEmptyChatId()
    {
        GetChatQuery query = new(ChatId: Guid.CreateVersion7());

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateRejectsEmptyChatId()
    {
        GetChatQuery query = new(ChatId: Guid.Empty);

        ValidationResult result = _validator.Validate(query);

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal(nameof(GetChatQuery.ChatId), failure.PropertyName);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetChatQueryValidatorTests"`
Expected: FAIL — build error, `GetChatQuery` / `GetChatQueryValidator` do not exist yet.

- [ ] **Step 3: Create the read models**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMessageModelReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatMessageModelReadModel
(
    Guid Id,
    string? Slug,
    string? Name
);
```

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMessageReadModel.cs`:

```csharp
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatMessageReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    MessageRole Role,
    string? Content,
    MessageStatus Status,
    string? FailureReason,
    int SiblingIndex,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    ChatMessageModelReadModel? Model
);
```

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatDetailReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatDetailReadModel
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CurrentMessageId,
    IReadOnlyList<ChatMessageReadModel> Messages
);
```

- [ ] **Step 4: Create the query and reader interface**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChat/GetChatQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.GetChat;

public sealed record GetChatQuery(Guid ChatId) : IQuery<ErrorOr<ChatDetailReadModel>>;
```

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChat/IChatDetailReader.cs`:

```csharp
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.GetChat;

public interface IChatDetailReader
{
    Task<ChatDetailReadModel?> GetAsync(ChatId chatId, UserId userId, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Create the validator**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChat/GetChatQueryValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.Chats.Queries.GetChat;

internal sealed class GetChatQueryValidator : AbstractValidator<GetChatQuery>
{
    public GetChatQueryValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetChatQueryValidatorTests"`
Expected: PASS (2 cases).

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Queries/GetChat tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatQueryValidatorTests.cs
git commit -m "feat(chat): add get chat query contract and validation"
```

---

### Task 2: Query handler

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/GetChatHandler.cs`
- Create: `tests/Chat/Chat.Application.Tests/Chats/FakeChatDetailReader.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatHandlerTests.cs`

**Interfaces:**
- Consumes: `GetChatQuery`, `ChatDetailReadModel`, `IChatDetailReader` (Task 1); `IUserContext` (`Shared.Application.Authentication`); `UserId`, `ChatId` (`Chat.Domain.Shared` / `Chat.Domain.Chats.ValueObjects`); `ChatOperationErrors` (`Chat.Application.Chats.Errors`); reuses `FakeUserContext` from `Chat.Application.Tests.FavoriteModels`.
- Produces: `GetChatHandler(IUserContext userContext, IChatDetailReader reader)` implementing `IQueryHandler<GetChatQuery, ErrorOr<ChatDetailReadModel>>`.

- [ ] **Step 1: Create the fake reader**

Create `tests/Chat/Chat.Application.Tests/Chats/FakeChatDetailReader.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChat;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Chats;

internal sealed class FakeChatDetailReader(ChatDetailReadModel? readModel) : IChatDetailReader
{
    public ChatId? RequestedChatId { get; private set; }

    public UserId? RequestedUserId { get; private set; }

    public int GetCallCount { get; private set; }

    public Task<ChatDetailReadModel?> GetAsync(ChatId chatId, UserId userId, CancellationToken cancellationToken)
    {
        RequestedChatId = chatId;
        RequestedUserId = userId;
        GetCallCount++;

        return Task.FromResult(readModel);
    }
}
```

- [ ] **Step 2: Write the failing handler tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChat;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class GetChatHandlerTests
{
    [Fact]
    public async Task HandleReturnsChatForOwner()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        Guid chatId = Guid.CreateVersion7();
        ChatDetailReadModel readModel = new
        (
            Id: chatId,
            Title: "ACCA F3",
            IsPinned: false,
            PinnedAt: null,
            IsArchived: false,
            IsTemporary: false,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt: DateTimeOffset.UtcNow,
            CurrentMessageId: Guid.CreateVersion7(),
            Messages: []
        );
        FakeChatDetailReader reader = new(readModel);
        GetChatHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<ChatDetailReadModel> result = await handler.Handle
        (
            new GetChatQuery(chatId),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(chatId, reader.RequestedChatId!.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal(1, reader.GetCallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWhenReaderReturnsNull()
    {
        FakeChatDetailReader reader = new(readModel: null);
        GetChatHandler handler = new
        (
            userContext: new FakeUserContext("auth0|user-1"),
            reader: reader
        );

        ErrorOr<ChatDetailReadModel> result = await handler.Handle
        (
            new GetChatQuery(Guid.CreateVersion7()),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal("Chat.NotFound", result.FirstError.Code);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetChatHandlerTests"`
Expected: FAIL — build error, `GetChatHandler` does not exist yet.

- [ ] **Step 4: Implement the handler**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChat/GetChatHandler.cs`:

```csharp
using Chat.Application.Chats.Errors;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.GetChat;

internal sealed class GetChatHandler(IUserContext userContext, IChatDetailReader reader)
    : IQueryHandler<GetChatQuery, ErrorOr<ChatDetailReadModel>>
{
    public async ValueTask<ErrorOr<ChatDetailReadModel>> Handle
    (
        GetChatQuery query,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(query.ChatId);

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

        ChatDetailReadModel? chat = await reader.GetAsync
        (
            chatIdResult.Value,
            userIdResult.Value,
            cancellationToken
        );

        if (chat is null)
        {
            return ChatOperationErrors.ChatNotFound(chatIdResult.Value);
        }

        return chat;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetChatHandlerTests"`
Expected: PASS (2 cases).

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Queries/GetChat/GetChatHandler.cs tests/Chat/Chat.Application.Tests/Chats/FakeChatDetailReader.cs tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatHandlerTests.cs
git commit -m "feat(chat): add get chat query handler"
```

---

### Task 3: Chat mapping builder (tree assembly)

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMappingNodeReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMappingBuilder.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/ChatMappingBuilderTests.cs`

**Interfaces:**
- Consumes: `ChatMessageReadModel` (Task 1); `MessageRole`, `MessageStatus` (`Chat.Domain.Chats.ValueObjects`).
- Produces:
  - `ChatMappingNodeReadModel(Guid Id, Guid? ParentMessageId, IReadOnlyList<Guid> ChildrenIds, ChatMessageReadModel Message)`
  - `ChatMappingBuilder.Build(IReadOnlyList<ChatMessageReadModel> messages) -> IReadOnlyList<ChatMappingNodeReadModel>` — one node per message; `ChildrenIds` are the ids of messages whose parent is this node, ordered by `SiblingIndex`, then `CreatedAt`, then `Id`; root nodes keep `ParentMessageId = null`.

- [ ] **Step 1: Write the failing builder tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Queries/ChatMappingBuilderTests.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChat;
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class ChatMappingBuilderTests
{
    [Fact]
    public void BuildWiresRootParentAndChildren()
    {
        Guid root = Guid.CreateVersion7();
        Guid a1 = Guid.CreateVersion7();
        Guid a2 = Guid.CreateVersion7();

        ChatMessageReadModel[] messages =
        [
            Message(root, parent: null, sibling: 0, role: MessageRole.User),
            Message(a1, parent: root, sibling: 0, role: MessageRole.Assistant),
            Message(a2, parent: root, sibling: 1, role: MessageRole.Assistant)
        ];

        IReadOnlyList<ChatMappingNodeReadModel> nodes = ChatMappingBuilder.Build(messages);

        ChatMappingNodeReadModel rootNode = nodes.Single(node => node.Id == root);
        Assert.Null(rootNode.ParentMessageId);
        Assert.Equal(new[] { a1, a2 }, rootNode.ChildrenIds);

        Assert.Empty(nodes.Single(node => node.Id == a1).ChildrenIds);
        Assert.Empty(nodes.Single(node => node.Id == a2).ChildrenIds);
    }

    [Fact]
    public void BuildOrdersChildrenBySiblingIndexNotCreatedAt()
    {
        Guid root = Guid.CreateVersion7();
        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();

        ChatMessageReadModel[] messages =
        [
            Message(root, parent: null, sibling: 0, role: MessageRole.User),
            Message(second, parent: root, sibling: 1, role: MessageRole.Assistant, createdAt: DateTimeOffset.UtcNow.AddMinutes(-5)),
            Message(first, parent: root, sibling: 0, role: MessageRole.Assistant, createdAt: DateTimeOffset.UtcNow)
        ];

        IReadOnlyList<ChatMappingNodeReadModel> nodes = ChatMappingBuilder.Build(messages);

        Assert.Equal(new[] { first, second }, nodes.Single(node => node.Id == root).ChildrenIds);
    }

    private static ChatMessageReadModel Message
    (
        Guid id,
        Guid? parent,
        int sibling,
        MessageRole role,
        DateTimeOffset? createdAt = null
    ) => new
    (
        Id: id,
        ParentMessageId: parent,
        Role: role,
        Content: "x",
        Status: MessageStatus.Completed,
        FailureReason: null,
        SiblingIndex: sibling,
        CreatedAt: createdAt ?? DateTimeOffset.UtcNow,
        CompletedAt: null,
        Model: null
    );
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ChatMappingBuilderTests"`
Expected: FAIL — build error, `ChatMappingBuilder` / `ChatMappingNodeReadModel` do not exist yet.

- [ ] **Step 3: Create the node read model**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMappingNodeReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatMappingNodeReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    IReadOnlyList<Guid> ChildrenIds,
    ChatMessageReadModel Message
);
```

- [ ] **Step 4: Implement the builder**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMappingBuilder.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChat;

public static class ChatMappingBuilder
{
    public static IReadOnlyList<ChatMappingNodeReadModel> Build(IReadOnlyList<ChatMessageReadModel> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        ILookup<Guid?, ChatMessageReadModel> byParent = messages.ToLookup(message => message.ParentMessageId);

        return messages
            .Select(message => new ChatMappingNodeReadModel
            (
                Id: message.Id,
                ParentMessageId: message.ParentMessageId,
                ChildrenIds: byParent[message.Id]
                    .OrderBy(child => child.SiblingIndex)
                    .ThenBy(child => child.CreatedAt)
                    .ThenBy(child => child.Id)
                    .Select(child => child.Id)
                    .ToList(),
                Message: message
            ))
            .ToList();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ChatMappingBuilderTests"`
Expected: PASS (2 cases).

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMappingNodeReadModel.cs src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatMappingBuilder.cs tests/Chat/Chat.Application.Tests/Chats/Queries/ChatMappingBuilderTests.cs
git commit -m "feat(chat): add chat message tree mapping builder"
```

---

### Task 4: Dapper reader + DI registration

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatDetailReader.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs` (the `AddReaders` method, ~line 146-156, and the `using` block)

**Interfaces:**
- Consumes: `IChatDetailReader`, `ChatDetailReadModel`, `ChatMessageReadModel`, `ChatMessageModelReadModel` (Task 1); `MessageRole`, `MessageStatus`; `ChatId`, `UserId`; `NpgsqlDataSource`.
- Produces: `ChatDetailReader : IChatDetailReader` registered scoped, so the API host can resolve the handler's reader dependency.

No unit test (the repo has no Infrastructure test project). Verification is a successful build; runtime behavior is exercised in Task 5's manual smoke.

- [ ] **Step 1: Implement the Dapper reader**

Create `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatDetailReader.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChat;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatDetailReader(NpgsqlDataSource dataSource) : IChatDetailReader
{
    private const string Sql = """
                               select id           as "Id",
                                      title        as "Title",
                                      pinned_at    as "PinnedAt",
                                      is_archived  as "IsArchived",
                                      is_temporary as "IsTemporary",
                                      created_at   as "CreatedAt",
                                      updated_at   as "UpdatedAt",
                                      current_message_id as "CurrentMessageId"
                               from chats
                               where id = @ChatId and user_id = @UserId;

                               select m.id               as "Id",
                                      m.parent_message_id as "ParentMessageId",
                                      m.role             as "Role",
                                      m.content          as "Content",
                                      m.status           as "Status",
                                      m.failure_reason   as "FailureReason",
                                      m.sibling_index    as "SiblingIndex",
                                      m.created_at       as "CreatedAt",
                                      m.completed_at     as "CompletedAt",
                                      m.llm_model_id     as "ModelId",
                                      lm.external_model_id as "ModelSlug",
                                      lm.name            as "ModelName"
                               from chat_messages m
                               left join llm_models lm on lm.id = m.llm_model_id
                               where m.chat_id = @ChatId
                               order by m.created_at, m.id;
                               """;

    public async Task<ChatDetailReadModel?> GetAsync
    (
        ChatId chatId,
        UserId userId,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            Sql,
            new { ChatId = chatId.Value, UserId = userId.Value },
            cancellationToken: cancellationToken
        );

        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        ChatRow? chat = await grid.ReadSingleOrDefaultAsync<ChatRow>();

        if (chat is null)
        {
            return null;
        }

        MessageRow[] rows = (await grid.ReadAsync<MessageRow>()).ToArray();

        ChatMessageReadModel[] messages = rows
            .Select(row => new ChatMessageReadModel
            (
                Id: row.Id,
                ParentMessageId: row.ParentMessageId,
                Role: Enum.Parse<MessageRole>(row.Role),
                Content: row.Content,
                Status: Enum.Parse<MessageStatus>(row.Status),
                FailureReason: row.FailureReason,
                SiblingIndex: row.SiblingIndex,
                CreatedAt: row.CreatedAt,
                CompletedAt: row.CompletedAt,
                Model: row.ModelId is null
                    ? null
                    : new ChatMessageModelReadModel(row.ModelId.Value, row.ModelSlug, row.ModelName)
            ))
            .ToArray();

        return new ChatDetailReadModel
        (
            Id: chat.Id,
            Title: chat.Title,
            IsPinned: chat.PinnedAt is not null,
            PinnedAt: chat.PinnedAt,
            IsArchived: chat.IsArchived,
            IsTemporary: chat.IsTemporary,
            CreatedAt: chat.CreatedAt,
            UpdatedAt: chat.UpdatedAt,
            CurrentMessageId: chat.CurrentMessageId,
            Messages: messages
        );
    }

    private sealed record ChatRow
    (
        Guid Id,
        string Title,
        DateTimeOffset? PinnedAt,
        bool IsArchived,
        bool IsTemporary,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid CurrentMessageId
    );

    private sealed record MessageRow
    (
        Guid Id,
        Guid? ParentMessageId,
        string Role,
        string? Content,
        string Status,
        string? FailureReason,
        int SiblingIndex,
        DateTimeOffset CreatedAt,
        DateTimeOffset? CompletedAt,
        Guid? ModelId,
        string? ModelSlug,
        string? ModelName
    );
}
```

- [ ] **Step 2: Register the reader in DI**

In `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`, add these two `using` directives to the existing `using` block:

```csharp
using Chat.Application.Chats.Queries.GetChat;
using Chat.Infrastructure.Chats.Readers;
```

Then in the `AddReaders` method, add the registration next to the existing `IFavoriteModelsReader` line:

```csharp
    private static IServiceCollection AddReaders(this IServiceCollection services)
    {
        services.AddScoped<PublicModelCatalogDapperReader>();
        services.AddScoped<IPublicModelCatalogReader, CachedPublicModelCatalogReader>();

        services.AddScoped<IManagedModelCatalogReader, ManagedModelCatalogDapperReader>();

        services.AddScoped<IFavoriteModelsReader, FavoriteModelsReader>();

        services.AddScoped<IChatDetailReader, ChatDetailReader>();

        return services;
    }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Nova.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatDetailReader.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): add chat detail dapper reader"
```

---

### Task 5: API endpoint, response, and mapper

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/MessageModelResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/MessageResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/MappingNodeResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/Response.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/ResponseMapper.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/Endpoint.cs`

**Interfaces:**
- Consumes: `GetChatQuery`, `ChatDetailReadModel`, `ChatMessageReadModel`, `ChatMappingNodeReadModel`, `ChatMappingBuilder` (Tasks 1, 3); `ISender`; `BaseEndpoint`/`SendErrorOrAsync` (`Shared.Api.Endpoints`); `CustomTags` (`Chat.Api.Endpoints`).
- Produces: `GET /v1/chats/{chatId}` returning `Response`; route name `Chat.Chats.Get`.

No unit test — the endpoint and this thin mapper are not unit-tested in this repo (the tree logic is covered by `ChatMappingBuilderTests`). Verification is a successful build plus a manual smoke call.

- [ ] **Step 1: Create the model response**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/MessageModelResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class MessageModelResponse
{
    public required Guid Id { get; init; }

    public required string? Slug { get; init; }

    public required string? Name { get; init; }
}
```

- [ ] **Step 2: Create the message response**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/MessageResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class MessageResponse
{
    public required string Role { get; init; }

    public required string? Content { get; init; }

    public required string Status { get; init; }

    public required string? FailureReason { get; init; }

    public required int SiblingIndex { get; init; }

    public required MessageModelResponse? Model { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset? CompletedAt { get; init; }
}
```

- [ ] **Step 3: Create the mapping node response**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/MappingNodeResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class MappingNodeResponse
{
    public required Guid Id { get; init; }

    public required Guid? ParentId { get; init; }

    public required IReadOnlyList<Guid> Children { get; init; }

    public required MessageResponse Message { get; init; }
}
```

- [ ] **Step 4: Create the top-level response**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/Response.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class Response
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required bool IsPinned { get; init; }

    public required DateTimeOffset? PinnedAt { get; init; }

    public required bool IsArchived { get; init; }

    public required bool IsTemporary { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required Guid CurrentNode { get; init; }

    public required IReadOnlyDictionary<string, MappingNodeResponse> Mapping { get; init; }
}
```

- [ ] **Step 5: Create the response mapper**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/ResponseMapper.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChat;

namespace Chat.Api.Endpoints.Chats.GetChat;

internal static class ResponseMapper
{
    public static Response ToResponse(ChatDetailReadModel readModel)
    {
        IReadOnlyList<ChatMappingNodeReadModel> nodes = ChatMappingBuilder.Build(readModel.Messages);

        Dictionary<string, MappingNodeResponse> mapping = nodes.ToDictionary
        (
            node => node.Id.ToString(),
            ToMappingNode
        );

        return new Response
        {
            Id = readModel.Id,
            Title = readModel.Title,
            IsPinned = readModel.IsPinned,
            PinnedAt = readModel.PinnedAt,
            IsArchived = readModel.IsArchived,
            IsTemporary = readModel.IsTemporary,
            CreatedAt = readModel.CreatedAt,
            UpdatedAt = readModel.UpdatedAt,
            CurrentNode = readModel.CurrentMessageId,
            Mapping = mapping
        };
    }

    private static MappingNodeResponse ToMappingNode(ChatMappingNodeReadModel node) => new()
    {
        Id = node.Id,
        ParentId = node.ParentMessageId,
        Children = node.ChildrenIds,
        Message = ToMessage(node.Message)
    };

    private static MessageResponse ToMessage(ChatMessageReadModel message) => new()
    {
        Role = message.Role.ToString().ToLowerInvariant(),
        Content = message.Content,
        Status = message.Status.ToString().ToLowerInvariant(),
        FailureReason = message.FailureReason,
        SiblingIndex = message.SiblingIndex,
        Model = message.Model is null
            ? null
            : new MessageModelResponse
            {
                Id = message.Model.Id,
                Slug = message.Model.Slug,
                Name = message.Model.Name
            },
        CreatedAt = message.CreatedAt,
        CompletedAt = message.CompletedAt
    };
}
```

- [ ] **Step 6: Create the endpoint**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints;
using Chat.Application.Chats.Queries.GetChat;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class Request
{
    public Guid ChatId { get; init; }
}

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, Response>
{
    public const string RouteName = "Chat.Chats.Get";

    public override void Configure()
    {
        Get("/chats/{chatId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Chat")
                .WithDescription("Gets a chat with its full message tree (ChatGPT-style mapping) for the authenticated user.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        ErrorOr<ChatDetailReadModel> result = await sender.Send(new GetChatQuery(request.ChatId), ct);

        await SendErrorOrAsync(result, ResponseMapper.ToResponse, cancellationToken: ct);
    }
}
```

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build Nova.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Manual smoke test**

Start the app host:

Run: `dotnet run --project Nova.AppHost`

Then call the endpoint for a chat the token's user owns (replace `$TOKEN`, the Chat API port from the Aspire dashboard, and `$CHAT_ID`):

```bash
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:<chat-api-port>/v1/chats/$CHAT_ID" | jq
```

Expected: `200 OK` with `{ id, title, …, currentNode, mapping: { "<id>": { id, parentId, children, message: { role, content, status, model, … } } } }`.

Verify: `currentNode` matches the chat's head; each root message has `parentId: null`; a regenerated/edited node shows multiple `children` on its parent ordered by sibling; `role`/`status` are lowercase; `model` is `null` on user messages and populated (`id` + `slug`/`name`) on assistant messages. Then confirm a chat id owned by a different user (or a random Guid) returns `404`, and `/v1/chats/00000000-0000-0000-0000-000000000000` returns `400`.

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats/GetChat
git commit -m "feat(chat): add get chat endpoint"
```

---

## Notes for the implementer

- `GetChatQuery` references `ChatDetailReadModel`, and `IChatDetailReader` returns it, so all of Task 1's read models, query, and interface must be created together for the project to compile.
- The handler aggregates `UserId` + `ChatId` errors exactly like `UpdateChatHandler`. `ChatId.Create` rejects `Guid.Empty`; the validator also rejects it, so an empty id is a 400 before the handler runs.
- `role`/`status` are stored as enum names, so the reader uses `Enum.Parse<MessageRole>` / `Enum.Parse<MessageStatus>`; the Api mapper lowercases them for the wire.
- `ChatMappingBuilder` is `public` so the Api `ResponseMapper` can call it; its tree-wiring logic is the unit under test (Task 3), which is why the Api mapper needs no test of its own.
- `Children` / `ChildrenIds` use collection expressions and `IReadOnlyList<Guid>`; the empty case is a node with no children (a leaf), which `ILookup` returns as an empty sequence.
- Read the chat grid first with `ReadSingleOrDefaultAsync`; a `null` chat row means not-found/not-owned — return `null` and let the handler map it to `Chat.NotFound`. The messages grid is left unread in that case (disposing the `GridReader` drains it).
- Register `ChatDetailReader` next to the other readers in `Chat.Infrastructure/DependencyInjection.cs`.
