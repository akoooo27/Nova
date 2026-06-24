# Chat Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build authenticated chat-history search with Elasticsearch-backed ranking, PostgreSQL validation, debounced snapshot indexing, and a dedicated search worker.

**Architecture:** Chat writes publish durable `ChatSearchIndexRequested` messages through the existing MassTransit EF outbox. `Chat.SearchWorker` consumes requests, stores per-chat debounce state in PostgreSQL, and reindexes whole chat snapshots into Elasticsearch after a quiet window. `Chat.Api` exposes `GET /me/chats/search`, queries Elasticsearch for ranked candidate chats, then validates/enriches them from PostgreSQL before returning chat-level results with snippets.

**Tech Stack:** .NET 10, FastEndpoints, Mediator.SourceGenerator / Mediator.Abstractions, FluentValidation, EF Core, Dapper, PostgreSQL, MassTransit/RabbitMQ, Aspire, Elasticsearch .NET client, xUnit.

---

## Implementation Notes

- Follow existing project conventions in `AGENTS.md`.
- Do not replace `Mediator` with MediatR.
- Do not upgrade MassTransit.
- Ask for elevated permissions before any `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet run`, migration generation, or similar .NET command.
- Tests are approved for this feature.
- Use `apply_patch` for manual file edits.
- The design spec is `docs/superpowers/specs/2026-06-24-chat-search-design.md`.
- The current design spec file is staged but may not be committed. Do not assume a clean git tree.

---

## File Structure

### Shared Contracts / Application

- Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexRequested.cs`  
  Durable indexing request contract.
- Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchReason.cs`  
  Central reason constants so publishers do not use ad-hoc strings.
- Create `src/services/Chat/Chat.Application/Chats/Search/IChatSearchIndexRequestPublisher.cs`  
  Application abstraction for publishing search indexing requests.
- Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexRequestPublisher.cs`  
  Publishes `ChatSearchIndexRequested` through `IMessageBus`.

### Search Query API

- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQuery.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQueryValidator.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsHandler.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/IChatSearchReader.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchReadModel.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchResultReadModel.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchSnippetReadModel.cs`
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Endpoint.cs`
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Request.cs`
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Response.cs`
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatResultResponse.cs`
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatSnippetResponse.cs`
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/ResponseMapper.cs`

### Search Infrastructure

- Create `src/services/Chat/Chat.Application/Abstractions/Search/IChatSearchEngine.cs`  
  Search-side abstraction for candidate lookup.
- Create `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchCandidate.cs`
- Create `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchCandidateSnippet.cs`
- Create `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchException.cs`
- Create `src/services/Chat/Chat.Application/Abstractions/Search/IChatSearchIndexer.cs`  
  Index-side abstraction for replacing one chat snapshot.
- Create `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchDocument.cs`
- Create `src/services/Chat/Chat.Infrastructure/Search/ElasticsearchOptions.cs`
- Create `src/services/Chat/Chat.Infrastructure/Search/ElasticsearchChatSearchEngine.cs`
- Create `src/services/Chat/Chat.Infrastructure/Search/ElasticsearchChatSearchIndexer.cs`
- Create `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatSearchValidationReader.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/IChatSearchValidationReader.cs`
- Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

### Debounce / Worker

- Create `src/services/Chat/Chat.Application/Chats/Search/IChatSearchIndexJobStore.cs`
- Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexJob.cs`
- Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexJobStatus.cs`
- Create `src/services/Chat/Chat.Application/Chats/Search/IChatSearchSnapshotReader.cs`
- Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchSnapshot.cs`
- Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchSnapshotMessage.cs`
- Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexer.cs`
- Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchIndexJobStore.cs`
- Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchSnapshotReader.cs`
- Create `src/services/Chat/Chat.Infrastructure/Search/Consumers/ChatSearchIndexRequestedConsumer.cs`
- Create `src/services/Chat/Chat.Infrastructure/Search/Consumers/ChatSearchIndexRequestedConsumerDefinition.cs`
- Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchWorkerOptions.cs`
- Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchIndexingBackgroundService.cs`
- Create `src/services/Chat/Chat.SearchWorker/Chat.SearchWorker.csproj`
- Create `src/services/Chat/Chat.SearchWorker/Program.cs`
- Create `src/services/Chat/Chat.SearchWorker/appsettings.json`

### Persistence / Aspire

- Modify `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`
- Create EF migration under `src/services/Chat/Chat.Infrastructure/Database/Migrations/` for `chat_search_index_jobs`.
- Modify `Directory.Packages.props` for Elasticsearch/Aspire packages after package verification.
- Modify `Nova.AppHost/Nova.AppHost.csproj`
- Modify `Nova.AppHost/AppHost.cs`
- Modify `Nova.slnx` if the project is not automatically discovered by the IDE/tooling.

### Tests

- Create `tests/Chat/Chat.Application.Tests/Chats/Search/ChatSearchIndexRequestPublisherTests.cs`
- Create `tests/Chat/Chat.Application.Tests/Chats/Search/ChatSearchIndexerTests.cs`
- Create `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsQueryValidatorTests.cs`
- Create `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsHandlerTests.cs`
- Create fakes under `tests/Chat/Chat.Application.Tests/Chats/Search/`
- Infrastructure tests can be added only if the repository already has an infrastructure test project; otherwise keep infrastructure verification to focused build/manual checks.

---

## Task 1: Add Elasticsearch Package Choices

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`

- [ ] **Step 1: Add central package version**

Modify `Directory.Packages.props`.

Add:

```xml
<PackageVersion Include="Elastic.Clients.Elasticsearch" Version="9.4.2" />
```

Do not add `Aspire.Hosting.Elasticsearch` or `Aspire.Elastic.Clients.Elasticsearch`: the latest package lookup found `13.3.0`, while this repo pins Aspire packages at `13.4.6`. Use an explicit AppHost container resource in Task 13 instead of mixing Aspire package versions.

- [ ] **Step 2: Add project package reference**

Modify `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj` to reference the Elasticsearch client:

```xml
<PackageReference Include="Elastic.Clients.Elasticsearch" />
```

- [ ] **Step 3: Run restore with elevation**

Ask for elevated permissions, then run:

```bash
dotnet restore Nova.slnx
```

Expected: restore succeeds.

- [ ] **Step 4: Commit package changes**

```bash
git add Directory.Packages.props src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj
git commit -m "build(chat): add search package dependencies"
```

---

## Task 2: Add Search Request Contract And Publisher

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexRequested.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Search/ChatSearchReason.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Search/IChatSearchIndexRequestPublisher.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexRequestPublisher.cs`
- Modify: `src/services/Chat/Chat.Application/DependencyInjection.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Search/ChatSearchIndexRequestPublisherTests.cs`

- [ ] **Step 1: Write publisher tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Search/ChatSearchIndexRequestPublisherTests.cs`:

```csharp
using Chat.Application.Chats.Search;
using Chat.Application.Tests.Turns;

namespace Chat.Application.Tests.Chats.Search;

public sealed class ChatSearchIndexRequestPublisherTests
{
    [Fact]
    public async Task PublishAsyncPublishesSearchIndexRequest()
    {
        FakeMessageBus bus = new();
        ChatSearchIndexRequestPublisher publisher = new(bus);
        Guid chatId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;

        await publisher.PublishAsync
        (
            chatId: chatId,
            userId: userId,
            reason: ChatSearchReason.ChatCreated,
            occurredAt: occurredAt,
            cancellationToken: CancellationToken.None
        );

        ChatSearchIndexRequested request = Assert.IsType<ChatSearchIndexRequested>(Assert.Single(bus.Published));
        Assert.Equal(chatId, request.ChatId);
        Assert.Equal(userId, request.UserId);
        Assert.Equal(ChatSearchReason.ChatCreated, request.Reason);
        Assert.Equal(occurredAt, request.OccurredAt);
    }
}
```

- [ ] **Step 2: Run the focused test and verify it fails**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter ChatSearchIndexRequestPublisherTests
```

Expected: fails because `ChatSearchIndexRequestPublisher` and related types do not exist.

- [ ] **Step 3: Add request contract**

Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexRequested.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public sealed record ChatSearchIndexRequested
(
    Guid ChatId,
    Guid UserId,
    string Reason,
    DateTimeOffset OccurredAt
);
```

- [ ] **Step 4: Add reason constants**

Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchReason.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public static class ChatSearchReason
{
    public const string ChatCreated = "chat-created";
    public const string UserMessageAdded = "user-message-added";
    public const string AssistantMessageCompleted = "assistant-message-completed";
    public const string ChatUpdated = "chat-updated";
    public const string ChatDeleted = "chat-deleted";
    public const string Backfill = "backfill";
}
```

- [ ] **Step 5: Add publisher abstraction**

Create `src/services/Chat/Chat.Application/Chats/Search/IChatSearchIndexRequestPublisher.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public interface IChatSearchIndexRequestPublisher
{
    Task PublishAsync
    (
        Guid chatId,
        Guid userId,
        string reason,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 6: Add publisher implementation**

Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexRequestPublisher.cs`:

```csharp
using Shared.Application.Messaging;

namespace Chat.Application.Chats.Search;

public sealed class ChatSearchIndexRequestPublisher(IMessageBus bus) : IChatSearchIndexRequestPublisher
{
    public Task PublishAsync
    (
        Guid chatId,
        Guid userId,
        string reason,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken
    )
    {
        ChatSearchIndexRequested request = new
        (
            ChatId: chatId,
            UserId: userId,
            Reason: reason,
            OccurredAt: occurredAt
        );

        return bus.PublishAsync(request, cancellationToken);
    }
}
```

- [ ] **Step 7: Register the publisher**

Modify `src/services/Chat/Chat.Application/DependencyInjection.cs`:

```csharp
using Chat.Application.Chats.Search;
using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Shared.Application.Behaviors;

namespace Chat.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;

            options.Assemblies =
            [
                typeof(DependencyInjection).Assembly
            ];

            options.PipelineBehaviors =
            [
                typeof(LoggingBehavior<,>),
                typeof(ValidationBehavior<,>)
            ];
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<IChatSearchIndexRequestPublisher, ChatSearchIndexRequestPublisher>();

        return services;
    }
}
```

- [ ] **Step 8: Run the focused test and verify it passes**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter ChatSearchIndexRequestPublisherTests
```

Expected: test passes.

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Search src/services/Chat/Chat.Application/DependencyInjection.cs tests/Chat/Chat.Application.Tests/Chats/Search/ChatSearchIndexRequestPublisherTests.cs
git commit -m "feat(chat): add search index request publisher"
```

---

## Task 3: Publish Index Requests From Chat Mutations

**Files:**
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageHandler.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/UpdateChat/UpdateChatHandler.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/BranchChat/BranchChatHandler.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/RegenerateMessage/RegenerateMessageHandler.cs`
- Modify: `src/services/Chat/Chat.Application/Turns/ChatTurnOrchestrator.cs`
- Tests: existing handler tests in `tests/Chat/Chat.Application.Tests/Turns/` and `tests/Chat/Chat.Application.Tests/Chats/Commands/`

- [ ] **Step 1: Update CreateChat handler tests to assert index request**

Open `tests/Chat/Chat.Application.Tests/Turns/CreateChatHandlerTests.cs`. Add assertions to the successful non-temporary chat creation test:

```csharp
ChatSearchIndexRequested request = Assert.IsType<ChatSearchIndexRequested>
(
    Assert.Single(bus.Published.OfType<ChatSearchIndexRequested>())
);

Assert.Equal(result.Value.ChatId, request.ChatId);
Assert.Equal(ChatSearchReason.ChatCreated, request.Reason);
```

Add a separate test for temporary chat creation:

```csharp
[Fact]
public async Task HandleDoesNotPublishSearchIndexRequestForTemporaryChat()
{
    // Arrange using the same successful setup as the existing create-chat test,
    // but pass IsTemporary: true in CreateChatCommand.
    // Assert that bus.Published.OfType<ChatSearchIndexRequested>() is empty.
}
```

Use the exact fake setup already present in that test file.

- [ ] **Step 2: Update SendMessage handler tests to assert index request**

Open `tests/Chat/Chat.Application.Tests/Turns/SendMessageHandlerTests.cs`. In the successful send test, assert:

```csharp
ChatSearchIndexRequested request = Assert.IsType<ChatSearchIndexRequested>
(
    Assert.Single(bus.Published.OfType<ChatSearchIndexRequested>())
);

Assert.Equal(command.ChatId, request.ChatId);
Assert.Equal(ChatSearchReason.UserMessageAdded, request.Reason);
```

- [ ] **Step 3: Add/update update-chat handler test**

Open `tests/Chat/Chat.Application.Tests/Chats/Commands/UpdateChatHandlerTests.cs`. In the successful update test, assert a `ChatSearchIndexRequested` with reason `ChatSearchReason.ChatUpdated`.

If the test currently does not use `FakeMessageBus`, add `FakeMessageBus bus = new();` and pass the publisher into the handler after modifying the handler constructor.

- [ ] **Step 4: Add/update assistant completion test**

Open `tests/Chat/Chat.Application.Tests/Turns/ChatTurnOrchestratorTests.cs`. In the successful completion test, assert a `ChatSearchIndexRequested` with reason `ChatSearchReason.AssistantMessageCompleted` is published after the assistant message is completed.

- [ ] **Step 5: Run focused tests and verify failures**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "CreateChatHandlerTests|SendMessageHandlerTests|UpdateChatHandlerTests|ChatTurnOrchestratorTests"
```

Expected: fails because handlers do not publish search index requests yet.

- [ ] **Step 6: Modify CreateChatHandler**

Add `IChatSearchIndexRequestPublisher searchIndexRequests` to the constructor.

After publishing `TurnRequested` and before `unitOfWork.SaveChangesAsync`, add:

```csharp
if (!thread.IsTemporary)
{
    await searchIndexRequests.PublishAsync
    (
        chatId: thread.Id.Value,
        userId: userId.Value,
        reason: ChatSearchReason.ChatCreated,
        occurredAt: now,
        cancellationToken: cancellationToken
    );
}
```

- [ ] **Step 7: Modify SendMessageHandler**

Add `IChatSearchIndexRequestPublisher searchIndexRequests` to the constructor.

After publishing `TurnRequested` and before `unitOfWork.SaveChangesAsync`, add:

```csharp
if (!thread.IsTemporary)
{
    await searchIndexRequests.PublishAsync
    (
        chatId: thread.Id.Value,
        userId: userId.Value,
        reason: ChatSearchReason.UserMessageAdded,
        occurredAt: now,
        cancellationToken: cancellationToken
    );
}
```

- [ ] **Step 8: Modify UpdateChatHandler**

Add `IChatSearchIndexRequestPublisher searchIndexRequests` to the constructor.

After applying rename/pin/archive changes and before `unitOfWork.SaveChangesAsync`, add:

```csharp
if (!thread.IsTemporary)
{
    await searchIndexRequests.PublishAsync
    (
        chatId: thread.Id.Value,
        userId: userId.Value,
        reason: ChatSearchReason.ChatUpdated,
        occurredAt: now,
        cancellationToken: cancellationToken
    );
}
```

If `UpdateChatHandler` does not currently define `now`, use the existing `dateTimeProvider.UtcNow` value already used for pinning.

- [ ] **Step 9: Modify BranchChatHandler and RegenerateMessageHandler**

For successful branch/regenerate flows that create new searchable messages, inject `IChatSearchIndexRequestPublisher` and publish with:

```csharp
reason: ChatSearchReason.UserMessageAdded
```

Use the resulting chat id/user id and the same timestamp already used for the mutation. Skip temporary chats.

- [ ] **Step 10: Modify ChatTurnOrchestrator**

Add `IChatSearchIndexRequestPublisher searchIndexRequests` to the constructor.

After `thread.CompleteAssistantMessage(...)` succeeds and before `unitOfWork.SaveChangesAsync`, add:

```csharp
if (!thread.IsTemporary)
{
    await searchIndexRequests.PublishAsync
    (
        chatId: thread.Id.Value,
        userId: userId.Value,
        reason: ChatSearchReason.AssistantMessageCompleted,
        occurredAt: dateTimeProvider.UtcNow,
        cancellationToken: cancellationToken
    );
}
```

Use a single `DateTimeOffset now = dateTimeProvider.UtcNow;` for completion and event occurrence so the timestamps match:

```csharp
DateTimeOffset now = dateTimeProvider.UtcNow;
ErrorOr<ChatMessage> completionResult = thread.CompleteAssistantMessage
(
    messageId: assistantMessage.Id,
    content: contentResult.Value,
    completedAt: now
);
```

- [ ] **Step 11: Run focused tests**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "CreateChatHandlerTests|SendMessageHandlerTests|UpdateChatHandlerTests|ChatTurnOrchestratorTests"
```

Expected: tests pass.

- [ ] **Step 12: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(chat): publish search indexing requests"
```

---

## Task 4: Add Search Query Application Contract

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQuery.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQueryValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/IChatSearchReader.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchResultReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchSnippetReadModel.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsQueryValidatorTests.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsHandlerTests.cs`
- Create fake: `tests/Chat/Chat.Application.Tests/Chats/FakeChatSearchReader.cs`

- [ ] **Step 1: Write validator tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsQueryValidatorTests.cs`:

```csharp
using Chat.Application.Chats.Queries.SearchChats;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class SearchChatsQueryValidatorTests
{
    private readonly SearchChatsQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsValidQuery()
    {
        SearchChatsQuery query = new(Query: "memory bug", IsArchived: false, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRejectsBlankQuery(string text)
    {
        SearchChatsQuery query = new(Query: text, IsArchived: false, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Query));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateRejectsOutOfRangeLimit(int limit)
    {
        SearchChatsQuery query = new(Query: "memory", IsArchived: false, Limit: limit, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Limit));
    }

    [Fact]
    public void ValidateRejectsNegativeOffset()
    {
        SearchChatsQuery query = new(Query: "memory", IsArchived: false, Limit: 20, Offset: -1);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Offset));
    }
}
```

- [ ] **Step 2: Write handler tests**

Create `tests/Chat/Chat.Application.Tests/Chats/FakeChatSearchReader.cs`:

```csharp
using Chat.Application.Chats.Queries.SearchChats;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Chats;

internal sealed class FakeChatSearchReader(ChatSearchReadModel readModel) : IChatSearchReader
{
    public UserId? RequestedUserId { get; private set; }
    public string? RequestedQuery { get; private set; }
    public bool? RequestedIsArchived { get; private set; }
    public int? RequestedLimit { get; private set; }
    public int? RequestedOffset { get; private set; }
    public int SearchCallCount { get; private set; }

    public Task<ChatSearchReadModel> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        RequestedUserId = userId;
        RequestedQuery = query;
        RequestedIsArchived = isArchived;
        RequestedLimit = limit;
        RequestedOffset = offset;
        SearchCallCount++;

        return Task.FromResult(readModel);
    }
}
```

Create `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Queries.SearchChats;
using Chat.Application.Tests.Chats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class SearchChatsHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleSearchesForCurrentUser(bool isArchived)
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        ChatSearchReadModel readModel = new
        (
            Items: [],
            Total: 0,
            Limit: 20,
            Offset: 0
        );
        FakeChatSearchReader reader = new(readModel);
        SearchChatsHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<ChatSearchReadModel> result = await handler.Handle
        (
            new SearchChatsQuery(Query: "memory bug", IsArchived: isArchived, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal("memory bug", reader.RequestedQuery);
        Assert.Equal(isArchived, reader.RequestedIsArchived);
        Assert.Equal(20, reader.RequestedLimit);
        Assert.Equal(0, reader.RequestedOffset);
        Assert.Equal(1, reader.SearchCallCount);
    }

    [Fact]
    public async Task HandleReturnsErrorAndSkipsReaderWhenUserIdMissing()
    {
        ChatSearchReadModel readModel = new(Items: [], Total: 0, Limit: 20, Offset: 0);
        FakeChatSearchReader reader = new(readModel);
        SearchChatsHandler handler = new
        (
            userContext: new FakeUserContext(string.Empty),
            reader: reader
        );

        ErrorOr<ChatSearchReadModel> result = await handler.Handle
        (
            new SearchChatsQuery(Query: "memory bug", IsArchived: false, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(0, reader.SearchCallCount);
    }
}
```

- [ ] **Step 3: Run focused tests and verify failures**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "SearchChatsQueryValidatorTests|SearchChatsHandlerTests"
```

Expected: fails because query types do not exist.

- [ ] **Step 4: Add query record**

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record SearchChatsQuery
(
    string Query,
    bool IsArchived,
    int Limit,
    int Offset
) : IQuery<ErrorOr<ChatSearchReadModel>>;
```

- [ ] **Step 5: Add read models and reader interface**

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchSnippetReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchSnippetReadModel
(
    Guid MessageId,
    string Role,
    string Text
);
```

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchResultReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchResultReadModel
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MatchCount,
    IReadOnlyList<ChatSearchSnippetReadModel> Snippets
);
```

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchReadModel
(
    IReadOnlyList<ChatSearchResultReadModel> Items,
    int Total,
    int Limit,
    int Offset
);
```

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/IChatSearchReader.cs`:

```csharp
using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.SearchChats;

public interface IChatSearchReader
{
    Task<ChatSearchReadModel> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 6: Add validator**

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQueryValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.Chats.Queries.SearchChats;

internal sealed class SearchChatsQueryValidator : AbstractValidator<SearchChatsQuery>
{
    public SearchChatsQueryValidator()
    {
        RuleFor(query => query.Query)
            .Must(query => !string.IsNullOrWhiteSpace(query))
            .WithMessage("Search query is required.");

        RuleFor(query => query.Limit)
            .InclusiveBetween(1, ChatLimits.MaxQueryLimit);

        RuleFor(query => query.Offset)
            .GreaterThanOrEqualTo(0);
    }
}
```

- [ ] **Step 7: Add handler**

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsHandler.cs`:

```csharp
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.SearchChats;

internal sealed class SearchChatsHandler(IUserContext userContext, IChatSearchReader reader)
    : IQueryHandler<SearchChatsQuery, ErrorOr<ChatSearchReadModel>>
{
    public async ValueTask<ErrorOr<ChatSearchReadModel>> Handle
    (
        SearchChatsQuery query,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        return await reader.SearchAsync
        (
            userId: userIdResult.Value,
            query: query.Query.Trim(),
            isArchived: query.IsArchived,
            limit: query.Limit,
            offset: query.Offset,
            cancellationToken: cancellationToken
        );
    }
}
```

- [ ] **Step 8: Run focused tests**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "SearchChatsQueryValidatorTests|SearchChatsHandlerTests"
```

Expected: tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Queries/SearchChats tests/Chat/Chat.Application.Tests/Chats
git commit -m "feat(chat): add search query contract"
```

---

## Task 5: Add Search Endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Response.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatResultResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatSnippetResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/ResponseMapper.cs`

- [ ] **Step 1: Add request**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Request.cs`:

```csharp
using FastEndpoints;

namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed record Request
(
    [property: QueryParam] string Query,
    [property: QueryParam] bool IsArchived,
    [property: QueryParam] int? Limit,
    [property: QueryParam] int? Offset
);
```

- [ ] **Step 2: Add response records**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatSnippetResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed record SearchChatSnippetResponse
(
    Guid MessageId,
    string Role,
    string Text
);
```

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatResultResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed record SearchChatResultResponse
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MatchCount,
    IReadOnlyList<SearchChatSnippetResponse> Snippets
);
```

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Response.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed record Response
(
    IReadOnlyList<SearchChatResultResponse> Items,
    int Total,
    int Limit,
    int Offset
);
```

- [ ] **Step 3: Add mapper**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/ResponseMapper.cs`:

```csharp
using Chat.Application.Chats.Queries.SearchChats;

namespace Chat.Api.Endpoints.Chats.SearchChats;

internal static class ResponseMapper
{
    public static Response ToResponse(ChatSearchReadModel model) => new
    (
        Items: model.Items.Select(ToResponse).ToArray(),
        Total: model.Total,
        Limit: model.Limit,
        Offset: model.Offset
    );

    private static SearchChatResultResponse ToResponse(ChatSearchResultReadModel model) => new
    (
        Id: model.Id,
        Title: model.Title,
        IsPinned: model.IsPinned,
        PinnedAt: model.PinnedAt,
        IsArchived: model.IsArchived,
        CreatedAt: model.CreatedAt,
        UpdatedAt: model.UpdatedAt,
        MatchCount: model.MatchCount,
        Snippets: model.Snippets.Select(ToResponse).ToArray()
    );

    private static SearchChatSnippetResponse ToResponse(ChatSearchSnippetReadModel model) => new
    (
        MessageId: model.MessageId,
        Role: model.Role,
        Text: model.Text
    );
}
```

- [ ] **Step 4: Add endpoint**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Endpoint.cs`:

```csharp
using Chat.Application.Chats;
using Chat.Application.Chats.Queries.SearchChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed class Endpoint(ISender sender) : Endpoint<Request, Response>
{
    public const string RouteName = "Chat.Chats.Search";

    public override void Configure()
    {
        Get("/me/chats/search");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Search Chats")
                .WithDescription("Searches the authenticated user's chat history and returns chat-level results with matching snippets.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status503ServiceUnavailable, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        SearchChatsQuery query = new
        (
            Query: request.Query,
            IsArchived: request.IsArchived,
            Limit: request.Limit ?? ChatLimits.DefaultQueryLimit,
            Offset: request.Offset ?? ChatLimits.DefaultQueryOffset
        );

        ErrorOr<ChatSearchReadModel> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}
```

- [ ] **Step 5: Build Chat.Api**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj
```

Expected: build succeeds, or fails only because infrastructure `IChatSearchReader` is not registered yet. If DI registration is the only missing piece, continue to Task 6.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats
git commit -m "feat(chat): add search endpoint"
```

---

## Task 6: Add Search Reader Abstractions And PostgreSQL Validation

**Files:**
- Create: `src/services/Chat/Chat.Application/Abstractions/Search/IChatSearchEngine.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchCandidate.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchCandidateSnippet.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchException.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/IChatSearchValidationReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatSearchValidationReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Search/ChatSearchReader.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsReaderCompositionTests.cs` if keeping composition in Application, otherwise verify with build only.

- [ ] **Step 1: Add search engine candidate records**

Create `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchCandidateSnippet.cs`:

```csharp
namespace Chat.Application.Abstractions.Search;

public sealed record ChatSearchCandidateSnippet
(
    Guid MessageId,
    string Role,
    string Text
);
```

Create `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchCandidate.cs`:

```csharp
namespace Chat.Application.Abstractions.Search;

public sealed record ChatSearchCandidate
(
    Guid ChatId,
    int MatchCount,
    IReadOnlyList<ChatSearchCandidateSnippet> Snippets
);
```

Create `src/services/Chat/Chat.Application/Abstractions/Search/IChatSearchEngine.cs`:

```csharp
using Chat.Domain.Shared;

namespace Chat.Application.Abstractions.Search;

public interface IChatSearchEngine
{
    Task<IReadOnlyList<ChatSearchCandidate>> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int candidateLimit,
        int offset,
        CancellationToken cancellationToken
    );
}
```

Create `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchException.cs`:

```csharp
namespace Chat.Application.Abstractions.Search;

public sealed class ChatSearchException(string message, Exception? innerException = null)
    : Exception(message, innerException);
```

- [ ] **Step 2: Add PostgreSQL validation reader interface**

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/IChatSearchValidationReader.cs`:

```csharp
using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.SearchChats;

public interface IChatSearchValidationReader
{
    Task<IReadOnlyList<ChatSearchValidatedChatReadModel>> GetValidChatsAsync
    (
        UserId userId,
        IReadOnlyCollection<Guid> chatIds,
        bool isArchived,
        CancellationToken cancellationToken
    );
}
```

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchValidatedChatReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchValidatedChatReadModel
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
```

- [ ] **Step 3: Add composed ChatSearchReader**

Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchReader.cs`:

```csharp
using Chat.Application.Abstractions.Search;
using Chat.Application.Chats.Queries.SearchChats;
using Chat.Domain.Shared;

namespace Chat.Infrastructure.Search;

internal sealed class ChatSearchReader
(
    IChatSearchEngine searchEngine,
    IChatSearchValidationReader validationReader
) : IChatSearchReader
{
    private const int CandidateMultiplier = 3;

    public async Task<ChatSearchReadModel> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        int candidateLimit = checked(limit * CandidateMultiplier);

        IReadOnlyList<ChatSearchCandidate> candidates = await searchEngine.SearchAsync
        (
            userId: userId,
            query: query,
            isArchived: isArchived,
            candidateLimit: candidateLimit,
            offset: offset,
            cancellationToken: cancellationToken
        );

        Guid[] candidateIds = candidates.Select(candidate => candidate.ChatId).Distinct().ToArray();

        IReadOnlyList<ChatSearchValidatedChatReadModel> validChats = await validationReader.GetValidChatsAsync
        (
            userId: userId,
            chatIds: candidateIds,
            isArchived: isArchived,
            cancellationToken: cancellationToken
        );

        Dictionary<Guid, ChatSearchValidatedChatReadModel> validById = validChats.ToDictionary(chat => chat.Id);

        ChatSearchResultReadModel[] items = candidates
            .Where(candidate => validById.ContainsKey(candidate.ChatId))
            .Take(limit)
            .Select(candidate =>
            {
                ChatSearchValidatedChatReadModel chat = validById[candidate.ChatId];

                return new ChatSearchResultReadModel
                (
                    Id: chat.Id,
                    Title: chat.Title,
                    IsPinned: chat.IsPinned,
                    PinnedAt: chat.PinnedAt,
                    IsArchived: chat.IsArchived,
                    CreatedAt: chat.CreatedAt,
                    UpdatedAt: chat.UpdatedAt,
                    MatchCount: candidate.MatchCount,
                    Snippets: candidate.Snippets
                        .Take(3)
                        .Select(snippet => new ChatSearchSnippetReadModel
                        (
                            MessageId: snippet.MessageId,
                            Role: snippet.Role,
                            Text: snippet.Text
                        ))
                        .ToArray()
                );
            })
            .ToArray();

        return new ChatSearchReadModel
        (
            Items: items,
            Total: candidates.Count,
            Limit: limit,
            Offset: offset
        );
    }
}
```

- [ ] **Step 4: Add Dapper validation reader**

Create `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatSearchValidationReader.cs`:

```csharp
using Chat.Application.Chats.Queries.SearchChats;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatSearchValidationReader(NpgsqlDataSource dataSource) : IChatSearchValidationReader
{
    private const string Sql = """
                               select
                                    id          as "Id",
                                    title       as "Title",
                                    pinned_at   as "PinnedAt",
                                    is_archived as "IsArchived",
                                    created_at  as "CreatedAt",
                                    updated_at  as "UpdatedAt"
                               from chats
                               where user_id = @UserId
                                 and id = any(@ChatIds)
                                 and is_archived = @IsArchived
                                 and is_temporary = false;
                               """;

    public async Task<IReadOnlyList<ChatSearchValidatedChatReadModel>> GetValidChatsAsync
    (
        UserId userId,
        IReadOnlyCollection<Guid> chatIds,
        bool isArchived,
        CancellationToken cancellationToken
    )
    {
        if (chatIds.Count == 0)
        {
            return [];
        }

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            commandText: Sql,
            parameters: new
            {
                UserId = userId.Value,
                ChatIds = chatIds.ToArray(),
                IsArchived = isArchived
            },
            cancellationToken: cancellationToken
        );

        ChatRow[] rows = (await connection.QueryAsync<ChatRow>(command)).ToArray();

        return rows.Select(row => new ChatSearchValidatedChatReadModel
        (
            Id: row.Id,
            Title: row.Title,
            IsPinned: row.PinnedAt is not null,
            PinnedAt: row.PinnedAt,
            IsArchived: row.IsArchived,
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt
        )).ToArray();
    }

    private sealed record ChatRow
    (
        Guid Id,
        string Title,
        DateTimeOffset? PinnedAt,
        bool IsArchived,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt
    );
}
```

- [ ] **Step 5: Register readers**

Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`.

Add usings:

```csharp
using Chat.Application.Chats.Queries.SearchChats;
using Chat.Infrastructure.Search;
```

In `AddReaders()` add:

```csharp
services.AddScoped<IChatSearchValidationReader, ChatSearchValidationReader>();
services.AddScoped<IChatSearchReader, ChatSearchReader>();
```

Do not register `IChatSearchEngine` until the Elasticsearch implementation exists in Task 7.

- [ ] **Step 6: Build**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj
```

Expected: build succeeds if `IChatSearchEngine` is registered later only at runtime; compile should succeed.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application/Abstractions/Search src/services/Chat/Chat.Application/Chats/Queries/SearchChats src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatSearchValidationReader.cs src/services/Chat/Chat.Infrastructure/Search/ChatSearchReader.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): validate search results against postgres"
```

---

## Task 7: Implement Elasticsearch Search And Indexer

**Files:**
- Create: `src/services/Chat/Chat.Application/Abstractions/Search/IChatSearchIndexer.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchDocument.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Search/ElasticsearchOptions.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Search/ElasticsearchChatSearchEngine.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Search/ElasticsearchChatSearchIndexer.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add indexer abstraction and document**

Create `src/services/Chat/Chat.Application/Abstractions/Search/ChatSearchDocument.cs`:

```csharp
namespace Chat.Application.Abstractions.Search;

public sealed record ChatSearchDocument
(
    string Id,
    Guid ChatId,
    Guid MessageId,
    string UserId,
    string ChatTitle,
    string Role,
    string Content,
    DateTimeOffset MessageCreatedAt,
    DateTimeOffset ChatUpdatedAt,
    bool IsArchived
);
```

Create `src/services/Chat/Chat.Application/Abstractions/Search/IChatSearchIndexer.cs`:

```csharp
namespace Chat.Application.Abstractions.Search;

public interface IChatSearchIndexer
{
    Task ReplaceChatAsync
    (
        Guid chatId,
        IReadOnlyCollection<ChatSearchDocument> documents,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 2: Add options**

Create `src/services/Chat/Chat.Infrastructure/Search/ElasticsearchOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Search;

internal sealed class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    [Required]
    public Uri Endpoint { get; init; } = default!;

    [Required]
    public string IndexName { get; init; } = "chat-messages";
}
```

- [ ] **Step 3: Implement ElasticsearchChatSearchIndexer**

Create `src/services/Chat/Chat.Infrastructure/Search/ElasticsearchChatSearchIndexer.cs`.

Use `Elastic.Clients.Elasticsearch` `9.4.2`. The implementation must:

```text
1. Delete existing documents where chatId == input chatId.
2. If documents.Count == 0, return after delete.
3. Bulk index documents with deterministic Id values.
4. Throw ChatSearchException when delete or bulk operations fail.
```

Required public shape:

```csharp
using Chat.Application.Abstractions.Search;

namespace Chat.Infrastructure.Search;

internal sealed class ElasticsearchChatSearchIndexer : IChatSearchIndexer
{
    public Task ReplaceChatAsync
    (
        Guid chatId,
        IReadOnlyCollection<ChatSearchDocument> documents,
        CancellationToken cancellationToken
    );
}
```

Constructor dependencies:

```csharp
ElasticsearchClient client,
IOptions<ElasticsearchOptions> options
```

Acceptance checks for this file:

```text
- ReplaceChatAsync calls DeleteByQueryAsync against options.Value.IndexName for the exact chatId.
- ReplaceChatAsync returns after delete when documents.Count == 0.
- ReplaceChatAsync bulk-indexes each document with document.Id as the Elasticsearch id.
- ReplaceChatAsync throws ChatSearchException if delete or bulk response is not valid.
- The file contains no unfinished method body.
```

- [ ] **Step 4: Implement ElasticsearchChatSearchEngine**

Create `src/services/Chat/Chat.Infrastructure/Search/ElasticsearchChatSearchEngine.cs`.

Use `Elastic.Clients.Elasticsearch` `9.4.2`. The implementation must:

```text
1. Filter by userId and isArchived.
2. Search chatTitle with moderate boost and modest fuzziness.
3. Search content without broad fuzziness.
4. Group/collapse by chatId.
5. Return up to candidateLimit chat candidates.
6. Include matchCount per chat.
7. Include up to 3 plain snippets per chat.
```

Required public shape:

```csharp
using Chat.Application.Abstractions.Search;
using Chat.Domain.Shared;

namespace Chat.Infrastructure.Search;

internal sealed class ElasticsearchChatSearchEngine : IChatSearchEngine
{
    public Task<IReadOnlyList<ChatSearchCandidate>> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int candidateLimit,
        int offset,
        CancellationToken cancellationToken
    );
}
```

Constructor dependencies:

```csharp
ElasticsearchClient client,
IOptions<ElasticsearchOptions> options
```

Acceptance checks for this file:

```text
- SearchAsync queries options.Value.IndexName.
- SearchAsync filters by UserId.Value and isArchived.
- SearchAsync searches chatTitle with a stronger boost than content.
- SearchAsync applies modest fuzziness to title matching only.
- SearchAsync limits returned snippets to 3 per chat.
- SearchAsync returns ChatSearchCandidate objects in Elasticsearch relevance order.
- SearchAsync throws ChatSearchException when the response is not valid.
- The file contains no unfinished method body.
```

- [ ] **Step 5: Register Elasticsearch services**

Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`.

Add a private method:

```csharp
private static IServiceCollection AddSearchServices(this IServiceCollection services, IConfiguration configuration)
{
    services
        .AddOptions<ElasticsearchOptions>()
        .Bind(configuration.GetSection(ElasticsearchOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddSingleton(sp =>
    {
        ElasticsearchOptions options = sp.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
        ElasticsearchClientSettings settings = new(options.Endpoint)
            .DefaultIndex(options.IndexName);

        return new ElasticsearchClient(settings);
    });

    services.AddScoped<IChatSearchEngine, ElasticsearchChatSearchEngine>();
    services.AddScoped<IChatSearchIndexer, ElasticsearchChatSearchIndexer>();

    return services;
}
```

Add these usings:

```csharp
using Elastic.Clients.Elasticsearch;

using Microsoft.Extensions.Options;
```

Call `.AddSearchServices(configuration)` from `AddInfrastructure(...)` for Chat.Api and from the new SearchWorker infrastructure path added in Task 11.

- [ ] **Step 6: Build**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application/Abstractions/Search src/services/Chat/Chat.Infrastructure/Search src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): add elasticsearch chat search infrastructure"
```

---

## Task 8: Add Debounce Job Persistence

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexJob.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexJobStatus.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Search/IChatSearchIndexJobStore.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Search/ChatSearchIndexJobStore.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`
- Create migration: `src/services/Chat/Chat.Infrastructure/Database/Migrations/YYYYMMDDHHMMSS_ChatSearchIndexJobs.cs`
- Test: add unit tests only for non-EF logic; verify EF with migration/build.

- [ ] **Step 1: Add job status**

Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexJobStatus.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public static class ChatSearchIndexJobStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Failed = "failed";
}
```

- [ ] **Step 2: Add job model**

Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexJob.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public sealed record ChatSearchIndexJob
(
    Guid ChatId,
    Guid UserId,
    DateTimeOffset IndexAfter,
    DateTimeOffset LastRequestedAt,
    string Status,
    int AttemptCount,
    string? LastError,
    DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
```

- [ ] **Step 3: Add store interface**

Create `src/services/Chat/Chat.Application/Chats/Search/IChatSearchIndexJobStore.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public interface IChatSearchIndexJobStore
{
    Task UpsertAsync
    (
        ChatSearchIndexRequested request,
        TimeSpan debounceDelay,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<ChatSearchIndexJob>> ClaimDueAsync
    (
        DateTimeOffset now,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken
    );

    Task MarkSucceededAsync(Guid chatId, CancellationToken cancellationToken);

    Task MarkFailedAsync
    (
        Guid chatId,
        string error,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 4: Add EF DbSet**

Modify `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`:

```csharp
using Chat.Application.Chats.Search;
```

Add:

```csharp
public DbSet<ChatSearchIndexJob> ChatSearchIndexJobs => Set<ChatSearchIndexJob>();
```

In `OnModelCreating`, before `ApplyConfigurationsFromAssembly`, add explicit configuration:

```csharp
modelBuilder.Entity<ChatSearchIndexJob>(builder =>
{
    builder.ToTable("chat_search_index_jobs");
    builder.HasKey(job => job.ChatId);
    builder.Property(job => job.ChatId).ValueGeneratedNever();
    builder.Property(job => job.UserId).IsRequired();
    builder.Property(job => job.IndexAfter).IsRequired();
    builder.Property(job => job.LastRequestedAt).IsRequired();
    builder.Property(job => job.Status).HasMaxLength(32).IsRequired();
    builder.Property(job => job.AttemptCount).IsRequired();
    builder.Property(job => job.LastError).HasMaxLength(2048);
    builder.Property(job => job.LockedUntil);
    builder.Property(job => job.CreatedAt).IsRequired();
    builder.Property(job => job.UpdatedAt).IsRequired();
    builder.HasIndex(job => new { job.Status, job.IndexAfter });
    builder.HasIndex(job => job.LockedUntil);
});
```

- [ ] **Step 5: Implement store**

Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchIndexJobStore.cs`:

```csharp
using Chat.Application.Chats.Search;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Search;

internal sealed class ChatSearchIndexJobStore(ChatDbContext db) : IChatSearchIndexJobStore
{
    public async Task UpsertAsync
    (
        ChatSearchIndexRequested request,
        TimeSpan debounceDelay,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset indexAfter = request.OccurredAt.Add(debounceDelay);
        ChatSearchIndexJob? existing = await db.ChatSearchIndexJobs
            .FirstOrDefaultAsync(job => job.ChatId == request.ChatId, cancellationToken);

        if (existing is null)
        {
            db.ChatSearchIndexJobs.Add(new ChatSearchIndexJob
            (
                ChatId: request.ChatId,
                UserId: request.UserId,
                IndexAfter: indexAfter,
                LastRequestedAt: request.OccurredAt,
                Status: ChatSearchIndexJobStatus.Pending,
                AttemptCount: 0,
                LastError: null,
                LockedUntil: null,
                CreatedAt: request.OccurredAt,
                UpdatedAt: request.OccurredAt
            ));
        }
        else
        {
            DateTimeOffset nextIndexAfter = existing.IndexAfter > indexAfter ? existing.IndexAfter : indexAfter;
            DateTimeOffset lastRequestedAt = existing.LastRequestedAt > request.OccurredAt
                ? existing.LastRequestedAt
                : request.OccurredAt;

            db.Entry(existing).CurrentValues.SetValues(existing with
            {
                UserId = request.UserId,
                IndexAfter = nextIndexAfter,
                LastRequestedAt = lastRequestedAt,
                Status = ChatSearchIndexJobStatus.Pending,
                LockedUntil = null,
                UpdatedAt = request.OccurredAt
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatSearchIndexJob>> ClaimDueAsync
    (
        DateTimeOffset now,
        int batchSize,
        TimeSpan lockDuration,
        CancellationToken cancellationToken
    )
    {
        FormattableString sql = $"""
                                 select *
                                 from chat_search_index_jobs
                                 where index_after <= {now}
                                   and (locked_until is null or locked_until <= {now})
                                   and status in ({ChatSearchIndexJobStatus.Pending}, {ChatSearchIndexJobStatus.Failed})
                                 order by index_after, chat_id
                                 limit {batchSize}
                                 for update skip locked
                                 """;

        List<ChatSearchIndexJob> jobs = await db.ChatSearchIndexJobs
            .FromSqlInterpolated(sql)
            .ToListAsync(cancellationToken);

        foreach (ChatSearchIndexJob job in jobs)
        {
            db.Entry(job).CurrentValues.SetValues(job with
            {
                Status = ChatSearchIndexJobStatus.Processing,
                LockedUntil = now.Add(lockDuration),
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return jobs;
    }

    public async Task MarkSucceededAsync(Guid chatId, CancellationToken cancellationToken)
    {
        await db.ChatSearchIndexJobs
            .Where(job => job.ChatId == chatId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task MarkFailedAsync
    (
        Guid chatId,
        string error,
        DateTimeOffset nextAttemptAt,
        CancellationToken cancellationToken
    )
    {
        string truncated = error.Length <= 2048 ? error : error[..2048];

        await db.ChatSearchIndexJobs
            .Where(job => job.ChatId == chatId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, ChatSearchIndexJobStatus.Failed)
                .SetProperty(job => job.AttemptCount, job => job.AttemptCount + 1)
                .SetProperty(job => job.LastError, truncated)
                .SetProperty(job => job.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(job => job.IndexAfter, nextAttemptAt)
                .SetProperty(job => job.UpdatedAt, nextAttemptAt), cancellationToken);
    }
}
```

- [ ] **Step 6: Register store**

Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs` inside `AddDatabaseServices()`:

```csharp
services.AddScoped<IChatSearchIndexJobStore, ChatSearchIndexJobStore>();
```

- [ ] **Step 7: Generate migration**

Ask for elevated permissions, then run the repository’s usual EF migration command. If no command exists, use:

```bash
dotnet ef migrations add ChatSearchIndexJobs --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj --context ChatDbContext
```

Expected: migration creates `chat_search_index_jobs`.

- [ ] **Step 8: Build**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj
```

Expected: build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Search src/services/Chat/Chat.Infrastructure/Search/ChatSearchIndexJobStore.cs src/services/Chat/Chat.Infrastructure/Database src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): add search indexing debounce store"
```

---

## Task 9: Add Snapshot Reader And Indexer Orchestrator

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Search/IChatSearchSnapshotReader.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Search/ChatSearchSnapshot.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Search/ChatSearchSnapshotMessage.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexer.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Search/ChatSearchSnapshotReader.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Search/ChatSearchIndexerTests.cs`

- [ ] **Step 1: Write indexer tests**

Create fakes in `tests/Chat/Chat.Application.Tests/Chats/Search/`.

`FakeChatSearchSnapshotReader.cs`:

```csharp
using Chat.Application.Chats.Search;

namespace Chat.Application.Tests.Chats.Search;

internal sealed class FakeChatSearchSnapshotReader : IChatSearchSnapshotReader
{
    public ChatSearchSnapshot? Snapshot { get; set; }

    public Task<ChatSearchSnapshot?> GetAsync(Guid chatId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Snapshot);
}
```

`FakeChatSearchIndexer.cs`:

```csharp
using Chat.Application.Abstractions.Search;

namespace Chat.Application.Tests.Chats.Search;

internal sealed class FakeChatSearchIndexer : IChatSearchIndexer
{
    public Guid? ReplacedChatId { get; private set; }
    public IReadOnlyCollection<ChatSearchDocument>? Documents { get; private set; }

    public Task ReplaceChatAsync
    (
        Guid chatId,
        IReadOnlyCollection<ChatSearchDocument> documents,
        CancellationToken cancellationToken
    )
    {
        ReplacedChatId = chatId;
        Documents = documents;
        return Task.CompletedTask;
    }
}
```

Create `tests/Chat/Chat.Application.Tests/Chats/Search/ChatSearchIndexerTests.cs`:

```csharp
using Chat.Application.Chats.Search;

namespace Chat.Application.Tests.Chats.Search;

public sealed class ChatSearchIndexerTests
{
    [Fact]
    public async Task ReindexAsyncDeletesDocumentsWhenChatIsMissing()
    {
        Guid chatId = Guid.CreateVersion7();
        FakeChatSearchSnapshotReader reader = new();
        FakeChatSearchIndexer indexer = new();
        ChatSearchIndexer sut = new(reader, indexer);

        await sut.ReindexAsync(chatId, Guid.CreateVersion7(), CancellationToken.None);

        Assert.Equal(chatId, indexer.ReplacedChatId);
        Assert.Empty(Assert.NotNull(indexer.Documents));
    }

    [Fact]
    public async Task ReindexAsyncSkipsTemporaryChat()
    {
        Guid chatId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        FakeChatSearchSnapshotReader reader = new()
        {
            Snapshot = new ChatSearchSnapshot
            (
                ChatId: chatId,
                UserId: userId,
                Title: "Temporary",
                IsArchived: false,
                IsTemporary: true,
                UpdatedAt: DateTimeOffset.UtcNow,
                Messages: []
            )
        };
        FakeChatSearchIndexer indexer = new();
        ChatSearchIndexer sut = new(reader, indexer);

        await sut.ReindexAsync(chatId, userId, CancellationToken.None);

        Assert.Empty(Assert.NotNull(indexer.Documents));
    }

    [Fact]
    public async Task ReindexAsyncIndexesCompletedUserAndAssistantMessages()
    {
        Guid chatId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid userMessageId = Guid.CreateVersion7();
        Guid assistantMessageId = Guid.CreateVersion7();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        FakeChatSearchSnapshotReader reader = new()
        {
            Snapshot = new ChatSearchSnapshot
            (
                ChatId: chatId,
                UserId: userId,
                Title: "Search Architecture",
                IsArchived: false,
                IsTemporary: false,
                UpdatedAt: now,
                Messages:
                [
                    new ChatSearchSnapshotMessage(userMessageId, "User", "how should search work", now.AddMinutes(-2), true),
                    new ChatSearchSnapshotMessage(assistantMessageId, "Assistant", "use debounced indexing", now.AddMinutes(-1), true),
                    new ChatSearchSnapshotMessage(Guid.CreateVersion7(), "Assistant", "still generating", now, false)
                ]
            )
        };
        FakeChatSearchIndexer indexer = new();
        ChatSearchIndexer sut = new(reader, indexer);

        await sut.ReindexAsync(chatId, userId, CancellationToken.None);

        Assert.Equal(chatId, indexer.ReplacedChatId);
        Assert.Collection
        (
            Assert.NotNull(indexer.Documents),
            document =>
            {
                Assert.Equal(userMessageId, document.MessageId);
                Assert.Equal("User", document.Role);
            },
            document =>
            {
                Assert.Equal(assistantMessageId, document.MessageId);
                Assert.Equal("Assistant", document.Role);
            }
        );
    }
}
```

- [ ] **Step 2: Run focused tests and verify failures**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter ChatSearchIndexerTests
```

Expected: fails because snapshot/indexer types do not exist.

- [ ] **Step 3: Add snapshot records and reader interface**

Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchSnapshotMessage.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public sealed record ChatSearchSnapshotMessage
(
    Guid MessageId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    bool IsSearchable
);
```

Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchSnapshot.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public sealed record ChatSearchSnapshot
(
    Guid ChatId,
    Guid UserId,
    string Title,
    bool IsArchived,
    bool IsTemporary,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ChatSearchSnapshotMessage> Messages
);
```

Create `src/services/Chat/Chat.Application/Chats/Search/IChatSearchSnapshotReader.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public interface IChatSearchSnapshotReader
{
    Task<ChatSearchSnapshot?> GetAsync(Guid chatId, Guid userId, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Add indexer orchestrator**

Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchIndexer.cs`:

```csharp
using Chat.Application.Abstractions.Search;

namespace Chat.Application.Chats.Search;

public sealed class ChatSearchIndexer
(
    IChatSearchSnapshotReader snapshots,
    IChatSearchIndexer indexer
)
{
    public async Task ReindexAsync(Guid chatId, Guid userId, CancellationToken cancellationToken)
    {
        ChatSearchSnapshot? snapshot = await snapshots.GetAsync(chatId, userId, cancellationToken);

        if (snapshot is null || snapshot.IsTemporary)
        {
            await indexer.ReplaceChatAsync(chatId, [], cancellationToken);
            return;
        }

        ChatSearchDocument[] documents = snapshot.Messages
            .Where(message => message.IsSearchable)
            .Select(message => new ChatSearchDocument
            (
                Id: $"{snapshot.ChatId:N}:{message.MessageId:N}",
                ChatId: snapshot.ChatId,
                MessageId: message.MessageId,
                UserId: snapshot.UserId.ToString(),
                ChatTitle: snapshot.Title,
                Role: message.Role,
                Content: message.Content,
                MessageCreatedAt: message.CreatedAt,
                ChatUpdatedAt: snapshot.UpdatedAt,
                IsArchived: snapshot.IsArchived
            ))
            .ToArray();

        await indexer.ReplaceChatAsync(chatId, documents, cancellationToken);
    }
}
```

- [ ] **Step 5: Add Dapper snapshot reader**

Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchSnapshotReader.cs`:

```csharp
using Chat.Application.Chats.Search;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Search;

internal sealed class ChatSearchSnapshotReader(NpgsqlDataSource dataSource) : IChatSearchSnapshotReader
{
    private const string ChatSql = """
                                   select
                                        id           as "ChatId",
                                        user_id      as "UserId",
                                        title        as "Title",
                                        is_archived  as "IsArchived",
                                        is_temporary as "IsTemporary",
                                        updated_at   as "UpdatedAt"
                                   from chats
                                   where id = @ChatId and user_id = @UserId;
                                   """;

    private const string MessagesSql = """
                                       select
                                            id         as "MessageId",
                                            role       as "Role",
                                            content    as "Content",
                                            created_at as "CreatedAt",
                                            (status = 'Completed' and content is not null and content <> '') as "IsSearchable"
                                       from chat_messages
                                       where chat_id = @ChatId
                                       order by created_at, id;
                                       """;

    public async Task<ChatSearchSnapshot?> GetAsync(Guid chatId, Guid userId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        ChatRow? chat = await connection.QuerySingleOrDefaultAsync<ChatRow>(new CommandDefinition
        (
            commandText: ChatSql,
            parameters: new { ChatId = chatId, UserId = userId },
            cancellationToken: cancellationToken
        ));

        if (chat is null)
        {
            return null;
        }

        MessageRow[] messages = (await connection.QueryAsync<MessageRow>(new CommandDefinition
        (
            commandText: MessagesSql,
            parameters: new { ChatId = chatId },
            cancellationToken: cancellationToken
        ))).ToArray();

        return new ChatSearchSnapshot
        (
            ChatId: chat.ChatId,
            UserId: chat.UserId,
            Title: chat.Title,
            IsArchived: chat.IsArchived,
            IsTemporary: chat.IsTemporary,
            UpdatedAt: chat.UpdatedAt,
            Messages: messages.Select(message => new ChatSearchSnapshotMessage
            (
                MessageId: message.MessageId,
                Role: message.Role,
                Content: message.Content ?? string.Empty,
                CreatedAt: message.CreatedAt,
                IsSearchable: message.IsSearchable
            )).ToArray()
        );
    }

    private sealed record ChatRow
    (
        Guid ChatId,
        Guid UserId,
        string Title,
        bool IsArchived,
        bool IsTemporary,
        DateTimeOffset UpdatedAt
    );

    private sealed record MessageRow
    (
        Guid MessageId,
        string Role,
        string? Content,
        DateTimeOffset CreatedAt,
        bool IsSearchable
    );
}
```

- [ ] **Step 6: Register services**

Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<IChatSearchSnapshotReader, ChatSearchSnapshotReader>();
services.AddScoped<ChatSearchIndexer>();
```

Place these in database/search service registration used by `Chat.SearchWorker`.

- [ ] **Step 7: Run focused tests**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter ChatSearchIndexerTests
```

Expected: tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Search src/services/Chat/Chat.Infrastructure/Search/ChatSearchSnapshotReader.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs tests/Chat/Chat.Application.Tests/Chats/Search
git commit -m "feat(chat): add chat search snapshot indexing"
```

---

## Task 10: Add Search Index Request Consumer

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Search/Consumers/ChatSearchIndexRequestedConsumer.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Search/Consumers/ChatSearchIndexRequestedConsumerDefinition.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Search/ChatSearchWorkerOptions.cs` or create it in Task 11 if not done here.

- [ ] **Step 1: Add worker options**

Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchWorkerOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Search;

internal sealed class ChatSearchWorkerOptions
{
    public const string SectionName = "ChatSearchWorker";

    [Range(1, 3600)]
    public int DebounceSeconds { get; init; } = 60;

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 50;

    [Range(1, 3600)]
    public int LockSeconds { get; init; } = 300;

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; init; } = 5;
}
```

- [ ] **Step 2: Add consumer**

Create `src/services/Chat/Chat.Infrastructure/Search/Consumers/ChatSearchIndexRequestedConsumer.cs`:

```csharp
using Chat.Application.Chats.Search;
using Chat.Infrastructure.Search;

using MassTransit;

using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Search.Consumers;

internal sealed class ChatSearchIndexRequestedConsumer
(
    IChatSearchIndexJobStore jobs,
    IOptions<ChatSearchWorkerOptions> options
) : IConsumer<ChatSearchIndexRequested>
{
    public async Task Consume(ConsumeContext<ChatSearchIndexRequested> context)
    {
        TimeSpan debounceDelay = TimeSpan.FromSeconds(options.Value.DebounceSeconds);

        await jobs.UpsertAsync(context.Message, debounceDelay, context.CancellationToken);
    }
}
```

- [ ] **Step 3: Add consumer definition**

Create `src/services/Chat/Chat.Infrastructure/Search/Consumers/ChatSearchIndexRequestedConsumerDefinition.cs`:

```csharp
using MassTransit;

namespace Chat.Infrastructure.Search.Consumers;

internal sealed class ChatSearchIndexRequestedConsumerDefinition
    : ConsumerDefinition<ChatSearchIndexRequestedConsumer>
{
    protected override void ConfigureConsumer
    (
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ChatSearchIndexRequestedConsumer> consumerConfigurator,
        IRegistrationContext context
    )
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Exponential
        (
            retryLimit: 5,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromSeconds(30),
            intervalDelta: TimeSpan.FromSeconds(3)
        ));
    }
}
```

- [ ] **Step 4: Build**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Search
git commit -m "feat(chat): consume search indexing requests"
```

---

## Task 11: Add Search Worker Project And Background Processor

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Search/ChatSearchIndexingBackgroundService.cs`
- Create: `src/services/Chat/Chat.SearchWorker/Chat.SearchWorker.csproj`
- Create: `src/services/Chat/Chat.SearchWorker/Program.cs`
- Create: `src/services/Chat/Chat.SearchWorker/appsettings.json`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Modify: `Nova.AppHost/AppHost.cs`
- Modify: `Nova.AppHost/Nova.AppHost.csproj`
- Modify: `Nova.slnx` if needed.

- [ ] **Step 1: Add background service**

Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchIndexingBackgroundService.cs`:

```csharp
using Chat.Application.Chats.Search;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Search;

internal sealed partial class ChatSearchIndexingBackgroundService
(
    IServiceScopeFactory scopeFactory,
    IOptions<ChatSearchWorkerOptions> options,
    ILogger<ChatSearchIndexingBackgroundService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PeriodicTimer timer = new(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ProcessBatchAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();

        IChatSearchIndexJobStore jobs = scope.ServiceProvider.GetRequiredService<IChatSearchIndexJobStore>();
        ChatSearchIndexer indexer = scope.ServiceProvider.GetRequiredService<ChatSearchIndexer>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ChatSearchWorkerOptions currentOptions = options.Value;

        IReadOnlyList<ChatSearchIndexJob> dueJobs = await jobs.ClaimDueAsync
        (
            now: now,
            batchSize: currentOptions.BatchSize,
            lockDuration: TimeSpan.FromSeconds(currentOptions.LockSeconds),
            cancellationToken: cancellationToken
        );

        foreach (ChatSearchIndexJob job in dueJobs)
        {
            try
            {
                await indexer.ReindexAsync(job.ChatId, job.UserId, cancellationToken);
                await jobs.MarkSucceededAsync(job.ChatId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogIndexingFailed(exception, job.ChatId);

                DateTimeOffset nextAttemptAt = DateTimeOffset.UtcNow.AddSeconds
                (
                    Math.Min(300, Math.Pow(2, Math.Min(job.AttemptCount + 1, 8)))
                );

                await jobs.MarkFailedAsync
                (
                    chatId: job.ChatId,
                    error: exception.Message,
                    nextAttemptAt: nextAttemptAt,
                    cancellationToken: cancellationToken
                );
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Chat search indexing failed for chat {ChatId}")]
    private partial void LogIndexingFailed(Exception exception, Guid chatId);
}
```

- [ ] **Step 2: Add infrastructure registration for search worker**

Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`.

Add:

```csharp
public static IServiceCollection AddSearchWorkerInfrastructure
(
    this IServiceCollection services,
    IConfiguration configuration
) =>
    services
        .AddSharedInfrastructure()
        .AddDatabaseServices()
        .AddSearchServices(configuration)
        .AddSearchWorkerMessaging(configuration)
        .AddSearchWorkerServices(configuration);
```

Add:

```csharp
private static IServiceCollection AddSearchWorkerServices(this IServiceCollection services, IConfiguration configuration)
{
    services
        .AddOptions<ChatSearchWorkerOptions>()
        .Bind(configuration.GetSection(ChatSearchWorkerOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddHostedService<ChatSearchIndexingBackgroundService>();

    return services;
}
```

Add MassTransit registration:

```csharp
private static IServiceCollection AddSearchWorkerMessaging(this IServiceCollection services, IConfiguration configuration)
{
    services.AddMassTransit(configurator =>
    {
        configurator.SetKebabCaseEndpointNameFormatter();

        configurator.AddConsumer<ChatSearchIndexRequestedConsumer, ChatSearchIndexRequestedConsumerDefinition>();

        configurator.AddEntityFrameworkOutbox<ChatDbContext>(outbox =>
        {
            outbox.UsePostgres();
            outbox.UseBusOutbox();
        });

        configurator.AddConfigureEndpointsCallback((context, _, endpointConfigurator) =>
        {
            endpointConfigurator.UseEntityFrameworkOutbox<ChatDbContext>(context);
        });

        configurator.UsingRabbitMq((context, rabbitMqConfigurator) =>
        {
            string rabbitMqConnectionString = configuration.GetConnectionString("rabbitmq")
                ?? throw new InvalidOperationException("Connection string 'rabbitmq' is required.");

            rabbitMqConfigurator.Host(new Uri(rabbitMqConnectionString));
            rabbitMqConfigurator.ConfigureEndpoints(context);
        });
    });

    return services;
}
```

- [ ] **Step 3: Create worker project**

Create `src/services/Chat/Chat.SearchWorker/Chat.SearchWorker.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />

    <PackageReference Include="Aspire.Npgsql" />
    <PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="EFCore.NamingConventions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\..\Nova.ServiceDefaults\Nova.ServiceDefaults.csproj" />
    <ProjectReference Include="..\Chat.Application\Chat.Application.csproj" />
    <ProjectReference Include="..\Chat.Infrastructure\Chat.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Create Program.cs**

Create `src/services/Chat/Chat.SearchWorker/Program.cs`:

```csharp
using Chat.Application;
using Chat.Infrastructure;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDbContext<ChatDbContext>((sp, options) =>
{
    string connectionString = builder.Configuration.GetConnectionString("chat-db")
                              ?? throw new InvalidOperationException("Connection string 'chat-db' is required.");

    options.UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention();
});

builder.EnrichNpgsqlDbContext<ChatDbContext>();

builder.Services
    .AddApplication()
    .AddSearchWorkerInfrastructure(builder.Configuration);

WebApplication app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
```

- [ ] **Step 5: Add worker appsettings**

Create `src/services/Chat/Chat.SearchWorker/appsettings.json`:

```json
{
  "ChatSearchWorker": {
    "DebounceSeconds": 60,
    "BatchSize": 50,
    "LockSeconds": 300,
    "PollIntervalSeconds": 5
  },
  "Elasticsearch": {
    "IndexName": "chat-messages"
  }
}
```

- [ ] **Step 6: Register worker in AppHost**

Modify `Nova.AppHost/Nova.AppHost.csproj`:

```xml
<ProjectReference Include="..\src\services\Chat\Chat.SearchWorker\Chat.SearchWorker.csproj" />
```

Modify `Nova.AppHost/AppHost.cs` after `chat-turn-worker`:

```csharp
builder.AddProject<Projects.Chat_SearchWorker>("chat-search-worker")
    .WithReference(chatDb)
    .WithReference(rabbitMq)
    .WaitFor(rabbitMq)
    .WaitForCompletion(chatMigrations);
```

Also wire Elasticsearch reference/configuration according to Task 1 package decision.

- [ ] **Step 7: Add project to solution if required**

Ask for elevated permissions if using `dotnet sln`:

```bash
dotnet sln Nova.slnx add src/services/Chat/Chat.SearchWorker/Chat.SearchWorker.csproj
```

Expected: project is included or command reports it already exists.

- [ ] **Step 8: Build worker**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.SearchWorker/Chat.SearchWorker.csproj
```

Expected: build succeeds.

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.SearchWorker src/services/Chat/Chat.Infrastructure/Search src/services/Chat/Chat.Infrastructure/DependencyInjection.cs Nova.AppHost Nova.slnx
git commit -m "feat(chat): add search indexing worker"
```

---

## Task 12: Add Manual Backfill Path

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Search/IChatSearchBackfillReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Search/ChatSearchBackfillReader.cs`
- Add command/option path in `src/services/Chat/Chat.SearchWorker/Program.cs`
- Test: application-level batching if practical.

- [ ] **Step 1: Add backfill reader interface**

Create `src/services/Chat/Chat.Application/Chats/Search/IChatSearchBackfillReader.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public interface IChatSearchBackfillReader
{
    Task<IReadOnlyList<ChatSearchBackfillChat>> GetBatchAsync
    (
        Guid? afterChatId,
        int batchSize,
        CancellationToken cancellationToken
    );
}
```

Create `src/services/Chat/Chat.Application/Chats/Search/ChatSearchBackfillChat.cs`:

```csharp
namespace Chat.Application.Chats.Search;

public sealed record ChatSearchBackfillChat
(
    Guid ChatId,
    Guid UserId,
    DateTimeOffset UpdatedAt
);
```

- [ ] **Step 2: Add Dapper backfill reader**

Create `src/services/Chat/Chat.Infrastructure/Search/ChatSearchBackfillReader.cs`:

```csharp
using Chat.Application.Chats.Search;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Search;

internal sealed class ChatSearchBackfillReader(NpgsqlDataSource dataSource) : IChatSearchBackfillReader
{
    private const string Sql = """
                               select
                                    id         as "ChatId",
                                    user_id    as "UserId",
                                    updated_at as "UpdatedAt"
                               from chats
                               where is_temporary = false
                                 and (@AfterChatId is null or id > @AfterChatId)
                               order by id
                               limit @BatchSize;
                               """;

    public async Task<IReadOnlyList<ChatSearchBackfillChat>> GetBatchAsync
    (
        Guid? afterChatId,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            commandText: Sql,
            parameters: new { AfterChatId = afterChatId, BatchSize = batchSize },
            cancellationToken: cancellationToken
        );

        return (await connection.QueryAsync<ChatSearchBackfillChat>(command)).ToArray();
    }
}
```

- [ ] **Step 3: Register backfill reader**

Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<IChatSearchBackfillReader, ChatSearchBackfillReader>();
```

- [ ] **Step 4: Add manual command path**

Modify `src/services/Chat/Chat.SearchWorker/Program.cs` before `await app.RunAsync();`:

```csharp
if (args.Contains("--backfill-search", StringComparer.OrdinalIgnoreCase))
{
    using IServiceScope scope = app.Services.CreateScope();
    IChatSearchBackfillReader reader = scope.ServiceProvider.GetRequiredService<IChatSearchBackfillReader>();
    IChatSearchIndexJobStore jobs = scope.ServiceProvider.GetRequiredService<IChatSearchIndexJobStore>();

    Guid? afterChatId = null;
    const int batchSize = 500;

    while (true)
    {
        IReadOnlyList<ChatSearchBackfillChat> batch = await reader.GetBatchAsync(afterChatId, batchSize, app.Lifetime.ApplicationStopping);

        if (batch.Count == 0)
        {
            break;
        }

        foreach (ChatSearchBackfillChat chat in batch)
        {
            await jobs.UpsertAsync(new ChatSearchIndexRequested
            (
                ChatId: chat.ChatId,
                UserId: chat.UserId,
                Reason: ChatSearchReason.Backfill,
                OccurredAt: chat.UpdatedAt
            ), TimeSpan.Zero, app.Lifetime.ApplicationStopping);
        }

        afterChatId = batch[^1].ChatId;
    }

    return;
}
```

- [ ] **Step 5: Build worker**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.SearchWorker/Chat.SearchWorker.csproj
```

Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Search src/services/Chat/Chat.Infrastructure/Search src/services/Chat/Chat.Infrastructure/DependencyInjection.cs src/services/Chat/Chat.SearchWorker/Program.cs
git commit -m "feat(chat): add manual search backfill"
```

---

## Task 13: Wire Elasticsearch In Aspire

**Files:**
- Modify: `Nova.AppHost/AppHost.cs`
- Modify: `Nova.AppHost/Nova.AppHost.csproj`
- Modify: `src/services/Chat/Chat.Api/appsettings.json`
- Modify: `src/services/Chat/Chat.SearchWorker/appsettings.json`

- [ ] **Step 1: Add Elasticsearch resource**

Add an explicit container resource in `Nova.AppHost/AppHost.cs`. Do not add `Aspire.Hosting.Elasticsearch`; Task 1 intentionally keeps Aspire package versions aligned by using the existing AppHost container support.

```csharp
IResourceBuilder<ContainerResource> elasticsearch = builder.AddContainer("elasticsearch", "docker.elastic.co/elasticsearch/elasticsearch", "9.2.2")
    .WithEnvironment("discovery.type", "single-node")
    .WithEnvironment("xpack.security.enabled", "false")
    .WithHttpEndpoint(port: 9200, targetPort: 9200, name: "http");
```

If `WithHttpEndpoint` is not available for container resources in Aspire 13.4.6, use the equivalent endpoint method exposed by `Aspire.Hosting.AppHost` and keep the endpoint name `http`.

- [ ] **Step 2: Reference Elasticsearch from Chat.Api and Chat.SearchWorker**

Modify `chatApi` in `Nova.AppHost/AppHost.cs` to pass Elasticsearch config:

```csharp
.WithEnvironment("Elasticsearch__Endpoint", elasticsearch.GetEndpoint("http"))
```

Modify `chat-search-worker` similarly:

```csharp
.WithEnvironment("Elasticsearch__Endpoint", elasticsearch.GetEndpoint("http"))
.WaitFor(elasticsearch)
```

If `elasticsearch.GetEndpoint("http")` does not convert directly to an environment value, use the endpoint expression API used elsewhere by Aspire 13.4.6 and keep the resulting `Elasticsearch__Endpoint` value as an HTTP URI.

- [ ] **Step 3: Build AppHost**

Ask for elevated permissions, then run:

```bash
dotnet build Nova.AppHost/Nova.AppHost.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add Nova.AppHost
git commit -m "feat(apphost): add elasticsearch resource"
```

---

## Task 14: Final Verification

**Files:**
- No new files unless fixes are required.

- [ ] **Step 1: Run application tests**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 2: Build solution**

Ask for elevated permissions, then run:

```bash
dotnet build Nova.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Optional local Aspire smoke test**

Ask for elevated permissions, then run:

```bash
dotnet run --project Nova.AppHost/Nova.AppHost.csproj
```

Expected:

- AppHost starts.
- Elasticsearch resource starts.
- Chat.Api starts.
- Chat.SearchWorker starts.
- No missing configuration errors for `Elasticsearch`, `chat-db`, or `rabbitmq`.

Stop the AppHost after verifying startup.

- [ ] **Step 4: Manual search behavior check**

Using local API tooling:

```http
GET /v1/me/chats/search?query=memory&isArchived=false&limit=20&offset=0
```

Expected:

- Blank query returns `400`.
- Elasticsearch unavailable returns service unavailable.
- Empty result returns `200` with `items: []`.
- Non-empty result returns chat-level items with no more than 3 snippets each.

- [ ] **Step 5: Commit final fixes**

If final verification required fixes:

```bash
git status --short
git add Directory.Packages.props Nova.AppHost Nova.slnx src/services/Chat tests/Chat
git commit -m "fix(chat): stabilize search implementation"
```

If no fixes were required, do not create an empty commit.

---

## Self-Review Checklist

- [ ] Spec coverage: search endpoint, archive filter, no temporary chats, snippets, Elasticsearch, SearchWorker, MassTransit outbox request, Postgres debounce jobs, whole-chat reindex, manual backfill, Postgres validation.
- [ ] Placeholder scan: no committed code may contain `TODO`, `TBD`, `NotImplementedException`, or pseudocode from this plan.
- [ ] Type consistency: `SearchChatsQuery`, `IChatSearchReader`, `IChatSearchEngine`, `IChatSearchIndexer`, `ChatSearchIndexer`, and job store signatures match across tasks.
- [ ] Project constraints: `Mediator` package remains, MassTransit is not upgraded, FastEndpoints style is used.
- [ ] Test permission: tests are included because the user explicitly approved them.
