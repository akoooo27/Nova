# Conversation Tree Manual Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or implement this manually task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement backend conversation-tree storage and navigation for ChatGPT-style branching in the Chat service.

**Architecture:** Add a `Conversation` aggregate root that owns `ConversationNode` entities and stores `CurrentNodeId` as the active branch pointer. Application command handlers mutate the aggregate through `IConversationRepository` and `IUnitOfWork`; Dapper readers return list, active-path, and full-tree projections. FastEndpoints expose user-facing conversation operations, while regeneration remains internal until the SSE/server-sent streaming design exists.

**Tech Stack:** .NET 10, `Mediator.SourceGenerator` / `Mediator.Abstractions`, FastEndpoints, EF Core, Npgsql, Dapper, ErrorOr, PostgreSQL, xUnit.

---

## Ground Rules

- Use the existing `Mediator` package family. Do not introduce MediatR.
- Use FastEndpoints. Do not introduce controllers.
- Do not upgrade MassTransit.
- Add domain and application tests. These are approved.
- Do not add infrastructure tests or API endpoint tests in this pass.
- Do not expose a public regeneration endpoint until the SSE/server-sent streaming design exists.
- Store `current_node_id` as non-null and without a database foreign key in this pass.
- Store `model_id` without a database foreign key in this pass.
- Any `dotnet` command run by Codex requires elevated permission first.

## Working Order

1. Domain value objects and aggregate tests.
2. Domain aggregate implementation.
3. Application contracts, fakes, and handler tests.
4. Application handlers and result mapping.
5. EF mappings and repository.
6. Dapper read models and readers.
7. FastEndpoints contracts.
8. EF migration.
9. Build and approved tests.

Each task should compile before moving to the next major layer. Commit boundaries are optional, but the safest manual flow is one commit after each task group.

---

## Task 1: Add Domain Value Objects

**Files**

- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/ConversationId.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/ConversationNodeId.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/ConversationTitle.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/MessageContent.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/MessageRole.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/MessageStatus.cs`
- Create: `tests/Chat/Chat.Domain.Tests/Conversations/ValueObjects/ConversationIdTests.cs`
- Create: `tests/Chat/Chat.Domain.Tests/Conversations/ValueObjects/ConversationNodeIdTests.cs`
- Create: `tests/Chat/Chat.Domain.Tests/Conversations/ValueObjects/ConversationTextValueObjectTests.cs`

### Step 1.1: Create ID value objects

Follow the existing `LlmProviderId`, `LlmModelId`, and `FavoriteModelId` style.

Required shape:

```csharp
namespace Chat.Domain.Conversations.ValueObjects;

public sealed record ConversationId
{
    public Guid Value { get; }

    private ConversationId(Guid value)
    {
        Value = value;
    }

    public static ConversationId New() => new(Guid.CreateVersion7());

    public static ErrorOr<ConversationId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "ConversationId.Empty",
                description: "Conversation id cannot be empty."
            );
        }

        return new ConversationId(value);
    }

    public static ConversationId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty conversation id.");

        return new ConversationId(value);
    }

    public override string ToString() => Value.ToString();
}
```

Create `ConversationNodeId` with the same structure and error code `ConversationNodeId.Empty`.

### Step 1.2: Add ID tests

Tests to write:

- `ConversationIdNewReturnsNonEmptyId`
- `ConversationIdCreateReturnsValueWhenGuidIsNotEmpty`
- `ConversationIdCreateReturnsEmptyValidationWhenGuidIsEmpty`
- `ConversationNodeIdNewReturnsNonEmptyId`
- `ConversationNodeIdCreateReturnsValueWhenGuidIsNotEmpty`
- `ConversationNodeIdCreateReturnsEmptyValidationWhenGuidIsEmpty`

Assertions should match existing identifier tests: `Assert.False(result.IsError)`, `Assert.Equal(value, result.Value.Value)`, and error code checks.

### Step 1.3: Create title and content value objects

Create `ConversationTitle`:

- `MaxLength = 200`
- trims input
- required error code: `ConversationTitle.Required`
- too-long error code: `ConversationTitle.TooLong`
- `FromDatabase` throws `DomainException` for invalid persisted values

Create `MessageContent`:

- `MaxLength = 32768`
- trims input
- required error code: `MessageContent.Required`
- too-long error code: `MessageContent.TooLong`
- `FromDatabase` throws `DomainException` for invalid persisted values

Use local constants inside the value objects. Do not reference application constants from the domain project.

### Step 1.4: Add title/content tests

Tests to write:

- `ConversationTitleCreateReturnsRequiredValidationWhenValueIsBlank`
- `ConversationTitleCreateReturnsTooLongValidationWhenValueExceedsMaxLength`
- `ConversationTitleCreateTrimsAndReturnsValue`
- `MessageContentCreateReturnsRequiredValidationWhenValueIsBlank`
- `MessageContentCreateReturnsTooLongValidationWhenValueExceedsMaxLength`
- `MessageContentCreateTrimsAndReturnsValue`

### Step 1.5: Create role and status value objects

Create `MessageRole` as an enum:

```csharp
namespace Chat.Domain.Conversations.ValueObjects;

public enum MessageRole
{
    User = 1,
    Assistant = 2
}
```

Create `MessageStatus` as an enum:

```csharp
namespace Chat.Domain.Conversations.ValueObjects;

public enum MessageStatus
{
    Completed = 1
}
```

Only `Completed` is included in this pass. Future pending/failed states belong with the SSE generation design.

### Step 1.6: Run domain value-object tests

Run after implementation:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter Conversations
```

Expected result: tests compile and pass. Codex must request elevated permission before running this command.

---

## Task 2: Add Conversation Aggregate

**Files**

- Create: `src/services/Chat/Chat.Domain/Conversations/Entities/ConversationNode.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ConversationErrors.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/Conversation.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/IConversationRepository.cs`
- Create: `tests/Chat/Chat.Domain.Tests/Conversations/ConversationTests.cs`
- Create: `tests/Chat/Chat.Domain.Tests/Conversations/TestConversationFactory.cs`

### Step 2.1: Create `ConversationNode`

Required properties:

```csharp
public sealed class ConversationNode : Entity<ConversationNodeId>
{
    public ConversationId ConversationId { get; private set; } = default!;
    public ConversationNodeId? ParentNodeId { get; private set; }
    public MessageRole Role { get; private set; }
    public MessageContent Content { get; private set; } = default!;
    public Guid? ModelId { get; private set; }
    public MessageStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int SiblingIndex { get; private set; }
}
```

Required factory:

```csharp
private ConversationNode()
{
    // EF Core materialization only
}

private ConversationNode
(
    ConversationNodeId id,
    ConversationId conversationId,
    ConversationNodeId? parentNodeId,
    MessageRole role,
    MessageContent content,
    Guid? modelId,
    MessageStatus status,
    DateTimeOffset createdAt,
    DateTimeOffset? completedAt,
    int siblingIndex
) : base(id)
{
    ConversationId = conversationId;
    ParentNodeId = parentNodeId;
    Role = role;
    Content = content;
    ModelId = modelId;
    Status = status;
    CreatedAt = createdAt;
    CompletedAt = completedAt;
    SiblingIndex = siblingIndex;
}

internal static ConversationNode Create
(
    ConversationId conversationId,
    ConversationNodeId? parentNodeId,
    MessageRole role,
    MessageContent content,
    Guid? modelId,
    DateTimeOffset createdAt,
    DateTimeOffset? completedAt,
    int siblingIndex
)
```

Rules:

- `Status` is always `MessageStatus.Completed` in this pass.
- `CompletedAt` is required for assistant nodes and can equal `createdAt` for stored completed responses.
- `CompletedAt` is null for user nodes.
- Constructor should be private plus a private parameterless EF constructor.

### Step 2.2: Create domain errors

Create `ConversationErrors` static methods:

- `NodeNotFound(ConversationNodeId nodeId)` returns `Error.NotFound("Conversation.NodeNotFound", ...)`
- `ParentNodeNotFound(ConversationNodeId nodeId)` returns `Error.NotFound("Conversation.ParentNodeNotFound", ...)`
- `AssistantParentMustBeUser(ConversationNodeId parentNodeId)` returns `Error.Conflict("Conversation.AssistantParentMustBeUser", ...)`
- `UserMessageEditTargetRequired(ConversationNodeId nodeId)` returns `Error.Conflict("Conversation.EditTargetMustBeUser", ...)`
- `AssistantRegenerationTargetRequired(ConversationNodeId nodeId)` returns `Error.Conflict("Conversation.RegenerationTargetMustBeAssistant", ...)`

### Step 2.3: Create `Conversation`

Required properties:

```csharp
public sealed class Conversation : AggregateRoot<ConversationId>
{
    private readonly List<ConversationNode> _nodes = [];

    public UserId UserId { get; private set; } = default!;
    public ConversationTitle Title { get; private set; } = default!;
    public ConversationNodeId CurrentNodeId { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<ConversationNode> Nodes => _nodes;
}
```

Required constructors:

```csharp
private Conversation()
    : base(default!)
{
    // EF Core materialization only
}

private Conversation
(
    ConversationId id,
    UserId userId,
    ConversationTitle title,
    ConversationNodeId currentNodeId,
    DateTimeOffset createdAt,
    DateTimeOffset updatedAt
) : base(id)
{
    UserId = userId;
    Title = title;
    CurrentNodeId = currentNodeId;
    CreatedAt = createdAt;
    UpdatedAt = updatedAt;
}
```

Required methods:

- `Create(UserId userId, ConversationTitle title, MessageContent initialMessage, DateTimeOffset createdAt)`
- `AddUserMessage(ConversationNodeId parentNodeId, MessageContent content, DateTimeOffset createdAt)`
- `AddAssistantMessage(ConversationNodeId parentNodeId, MessageContent content, Guid? modelId, DateTimeOffset createdAt, DateTimeOffset completedAt)`
- `SelectNode(ConversationNodeId nodeId, DateTimeOffset updatedAt)`
- `EditUserMessage(ConversationNodeId nodeId, MessageContent content, DateTimeOffset createdAt)`
- `RegenerateAssistantMessage(ConversationNodeId nodeId, MessageContent content, Guid? modelId, DateTimeOffset createdAt, DateTimeOffset completedAt)`
- `FindNode(ConversationNodeId nodeId)`

Return `ErrorOr<ConversationNode>` for methods that create a node. Return `ErrorOr<Success>` for `SelectNode`.

Sibling index rule:

```csharp
private int GetNextSiblingIndex(ConversationNodeId? parentNodeId) =>
    _nodes.Count(node => node.ParentNodeId == parentNodeId);
```

Role rules:

- Root-level nodes have `ParentNodeId == null` and `Role == MessageRole.User`.
- `AddUserMessage` never creates root-level nodes. It receives a resolved parent node id from the application layer.
- Assistant nodes must have a user parent.
- Editing a user node creates a user node with the same parent as the original.
- Regenerating an assistant node creates an assistant node with the same parent as the original.

State update rules:

- Creating or adding a node updates `CurrentNodeId` to the new node.
- Creating or adding a node updates `UpdatedAt` to the supplied timestamp.
- Selecting a node updates only `CurrentNodeId` and `UpdatedAt`.

### Step 2.4: Create repository interface

Required shape:

```csharp
public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync
    (
        ConversationId id,
        UserId userId,
        CancellationToken cancellationToken = default
    );

    void Add(Conversation conversation);
}
```

### Step 2.5: Add domain tests

Use `TestConversationFactory` helpers for user id, title, content, and fixed timestamps.

Tests to write:

- `CreateInitializesConversationWithRootUserNode`
- `AddUserMessageAddsChildUnderSelectedParent`
- `AddUserMessageAssignsNextSiblingIndex`
- `AddUserMessageReturnsNotFoundWhenParentDoesNotExist`
- `AddAssistantMessageAddsAssistantUnderUserNode`
- `AddAssistantMessageReturnsConflictWhenParentIsAssistant`
- `AddAssistantMessageReturnsNotFoundWhenParentDoesNotExist`
- `SelectNodeUpdatesCurrentNode`
- `SelectNodeReturnsNotFoundWhenNodeDoesNotExist`
- `EditUserMessageCreatesSiblingWithoutMutatingOriginal`
- `EditRootUserMessageCreatesRootLevelSibling`
- `EditUserMessageReturnsConflictWhenTargetIsAssistant`
- `RegenerateAssistantMessageCreatesAssistantSiblingWithoutMutatingOriginal`
- `RegenerateAssistantMessageReturnsConflictWhenTargetIsUser`

### Step 2.6: Run domain aggregate tests

Run:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter Conversations
```

Expected result: all conversation domain tests pass. Codex must request elevated permission before running this command.

---

## Task 3: Add Application Results, Errors, and Fakes

**Files**

- Create: `src/services/Chat/Chat.Application/Conversations/ConversationLimits.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Errors/ConversationOperationErrors.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Results/ConversationNodeResult.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Results/ConversationResult.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Results/ConversationResultMapper.cs`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/FakeConversationRepository.cs`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/FakeUnitOfWork.cs`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/FakeUserContext.cs`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/FakeDateTimeProvider.cs`

### Step 3.1: Add limits

Create:

```csharp
namespace Chat.Application.Conversations;

public static class ConversationLimits
{
    public const int TitleMaxLength = 200;
    public const int MessageContentMaxLength = 32768;
}
```

Keep domain constants in domain value objects. These application constants are for validators and API metadata.

### Step 3.2: Add operation errors

Create `ConversationOperationErrors` with:

- `ConversationNotFound(ConversationId conversationId)` as `Error.NotFound("Conversation.NotFound", ...)`
- `NodeNotFound(ConversationNodeId nodeId)` as `Error.NotFound("Conversation.NodeNotFound", ...)`
- `InvalidUser()` as `Error.Validation("Conversation.UserInvalid", ...)`

Domain conflict errors from `ConversationErrors` can pass through handlers unchanged.

### Step 3.3: Add result records and mapper

Required records:

```csharp
public sealed record ConversationNodeResult
(
    Guid Id,
    Guid ConversationId,
    Guid? ParentNodeId,
    string Role,
    string Content,
    Guid? ModelId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int SiblingIndex
);

public sealed record ConversationResult
(
    Guid Id,
    string UserId,
    string Title,
    Guid CurrentNodeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<ConversationNodeResult> Nodes
);
```

Mapper rules:

- Map `Role` with `node.Role.ToString()`.
- Map `Status` with `node.Status.ToString()`.
- Order `Nodes` by `CreatedAt`, then `SiblingIndex`, then `Id.Value` for deterministic command result output.

### Step 3.4: Add application fakes

`FakeConversationRepository`:

- stores conversations in a list
- implements owner-scoped `GetByIdAsync`
- exposes `AddExistingConversation`
- exposes `AddedConversations`

`FakeUnitOfWork`:

- implements `IUnitOfWork`
- exposes `SaveChangesCallCount`

`FakeUserContext`:

- implements `IUserContext`
- accepts nullable constructor values when useful for invalid-user tests

`FakeDateTimeProvider`:

- implements `IDateTimeProvider`
- returns a fixed `UtcNow`

---

## Task 4: Add Create Conversation Command

**Files**

- Create: `src/services/Chat/Chat.Application/Conversations/Commands/CreateConversation/CreateConversationCommand.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Commands/CreateConversation/CreateConversationCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Commands/CreateConversation/CreateConversationHandler.cs`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/Commands/CreateConversationHandlerTests.cs`

### Step 4.1: Add command and validator

Command:

```csharp
public sealed record CreateConversationCommand
(
    string Title,
    string Message
) : ICommand<ErrorOr<ConversationResult>>;
```

Validator:

- `Title` not empty and max length 200
- `Message` not empty and max length 32768

### Step 4.2: Add handler

Handler dependencies:

- `IUserContext`
- `IConversationRepository`
- `IUnitOfWork`
- `IDateTimeProvider`

Handler flow:

1. Create `UserId` from `userContext.UserId`.
2. Create `ConversationTitle` from `command.Title`.
3. Create `MessageContent` from `command.Message`.
4. Return collected validation errors when any creation fails.
5. Create `Conversation` using `dateTimeProvider.UtcNow`.
6. Add it to the repository.
7. Save once.
8. Return mapped `ConversationResult`.

### Step 4.3: Add tests

Tests:

- `HandleCreatesConversationAndSavesChanges`
- `HandleReturnsValidationErrorsWithoutSaving`
- `HandleReturnsValidationErrorWhenUserIdIsUnavailable`

Run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter CreateConversation
```

Expected result: create conversation tests pass. Codex must request elevated permission before running this command.

---

## Task 5: Add Message Mutation Commands

**Files**

- Create command, validator, and handler files for:
  - `AddUserMessage`
  - `AddAssistantMessage`
  - `SelectConversationNode`
  - `EditUserMessage`
  - `RegenerateAssistantMessage`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/Commands/AddUserMessageHandlerTests.cs`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/Commands/AddAssistantMessageHandlerTests.cs`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/Commands/SelectConversationNodeHandlerTests.cs`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/Commands/EditUserMessageHandlerTests.cs`
- Create: `tests/Chat/Chat.Application.Tests/Conversations/Commands/RegenerateAssistantMessageHandlerTests.cs`

### Step 5.1: Add `AddUserMessage`

Command:

```csharp
public sealed record AddUserMessageCommand
(
    Guid ConversationId,
    Guid? ParentNodeId,
    string Message
) : ICommand<ErrorOr<ConversationNodeResult>>;
```

Handler flow:

1. Create `UserId`, `ConversationId`, optional `ConversationNodeId`, and `MessageContent`.
2. Load conversation by id and user id.
3. Return `ConversationOperationErrors.ConversationNotFound` when missing.
4. Resolve the effective parent id to `command.ParentNodeId` when supplied; otherwise use `conversation.CurrentNodeId`.
5. Call `conversation.AddUserMessage(effectiveParentNodeId, content, createdAt)`.
6. Save once when successful.
7. Return mapped node result.

Tests:

- `HandleAddsUserMessageUnderCurrentNodeWhenParentIsOmitted`
- `HandleAddsUserMessageUnderExplicitParent`
- `HandleReturnsNotFoundWhenConversationDoesNotExist`
- `HandleReturnsValidationErrorsWithoutSaving`
- `HandlePropagatesDomainErrorsWithoutSaving`

### Step 5.2: Add `AddAssistantMessage`

Command:

```csharp
public sealed record AddAssistantMessageCommand
(
    Guid ConversationId,
    Guid ParentNodeId,
    string Message,
    Guid? ModelId
) : ICommand<ErrorOr<ConversationNodeResult>>;
```

This command is application-facing for future generation flow. Do not expose it as a public API endpoint in this pass.

Handler flow:

1. Validate user, conversation id, parent node id, message, and optional model id.
2. Load owner-scoped conversation.
3. Call `conversation.AddAssistantMessage` with `createdAt` and `completedAt` both set from `dateTimeProvider.UtcNow`.
4. Save once when successful.
5. Return mapped node result.

Tests:

- `HandleAddsAssistantMessageUnderUserNode`
- `HandleReturnsConflictWhenParentIsAssistant`
- `HandleReturnsNotFoundWhenConversationDoesNotExist`
- `HandleReturnsValidationErrorsWithoutSaving`

### Step 5.3: Add `SelectConversationNode`

Command:

```csharp
public sealed record SelectConversationNodeCommand
(
    Guid ConversationId,
    Guid NodeId
) : ICommand<ErrorOr<ConversationResult>>;
```

Tests:

- `HandleSelectsNodeAndSavesChanges`
- `HandleReturnsNotFoundWhenConversationDoesNotExist`
- `HandlePropagatesNodeNotFoundWithoutSaving`

### Step 5.4: Add `EditUserMessage`

Command:

```csharp
public sealed record EditUserMessageCommand
(
    Guid ConversationId,
    Guid NodeId,
    string Message
) : ICommand<ErrorOr<ConversationNodeResult>>;
```

Tests:

- `HandleCreatesUserSiblingAndSavesChanges`
- `HandleCreatesRootLevelSiblingWhenEditingRootUserMessage`
- `HandleReturnsConflictWhenTargetIsAssistant`
- `HandleReturnsNotFoundWhenConversationDoesNotExist`
- `HandleReturnsValidationErrorsWithoutSaving`

### Step 5.5: Add `RegenerateAssistantMessage`

Command:

```csharp
public sealed record RegenerateAssistantMessageCommand
(
    Guid ConversationId,
    Guid NodeId,
    string Message,
    Guid? ModelId
) : ICommand<ErrorOr<ConversationNodeResult>>;
```

This command remains internal to application code until SSE generation is designed. Do not create a public FastEndpoint for it.

Tests:

- `HandleCreatesAssistantSiblingAndSavesChanges`
- `HandleReturnsConflictWhenTargetIsUser`
- `HandleReturnsNotFoundWhenConversationDoesNotExist`
- `HandleReturnsValidationErrorsWithoutSaving`

### Step 5.6: Run application command tests

Run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter Conversations
```

Expected result: all approved conversation application tests pass. Codex must request elevated permission before running this command.

---

## Task 6: Add Persistence Mapping and Repository

**Files**

- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Configurations/ConversationConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Configurations/ConversationNodeConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Repositories/ConversationRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

### Step 6.1: Add `DbSet`

In `ChatDbContext`, add:

```csharp
public DbSet<Conversation> Conversations => Set<Conversation>();
```

Keep `ApplyConfigurationsFromAssembly`.

### Step 6.2: Map `Conversation`

Mapping requirements:

- table `conversations`
- primary key `Id`
- `Id` conversion to/from `ConversationId`
- `UserId` conversion to/from `Chat.Domain.Shared.UserId`
- `Title` conversion to/from `ConversationTitle`
- `CurrentNodeId` conversion to/from `ConversationNodeId`
- `CurrentNodeId` must be configured as required so `current_node_id` is generated as `uuid not null`
- required `CreatedAt`
- required `UpdatedAt`
- index on `{ UserId, UpdatedAt, Id }` with `UpdatedAt` descending when supported
- index on `{ UserId, Id }`
- field access for `Nodes`
- ignore `DomainEvents`

### Step 6.3: Map `ConversationNode`

Mapping requirements:

- table `conversation_nodes`
- primary key `Id`
- `ConversationId` conversion
- nullable `ParentNodeId` conversion
- `Role` stored as string
- `Status` stored as string
- `Content` conversion to/from `MessageContent`
- nullable `ModelId` as raw uuid
- required `CreatedAt`
- nullable `CompletedAt`
- required `SiblingIndex`
- index on `{ ConversationId, ParentNodeId, SiblingIndex, Id }`
- index on `{ ConversationId, Id }`
- cascade delete from conversation to nodes
- restricted delete for parent self-reference

Do not add database FKs for `current_node_id` or `model_id`.

### Step 6.4: Add repository

Repository shape:

```csharp
internal sealed class ConversationRepository(ChatDbContext db) : IConversationRepository
{
    public async Task<Conversation?> GetByIdAsync
    (
        ConversationId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.Conversations
            .Include(conversation => conversation.Nodes)
            .FirstOrDefaultAsync
            (
                conversation => conversation.Id == id && conversation.UserId == userId,
                cancellationToken
            );
    }

    public void Add(Conversation conversation)
    {
        db.Conversations.Add(conversation);
    }
}
```

### Step 6.5: Register repository

In `Chat.Infrastructure.DependencyInjection`, register:

```csharp
services.AddScoped<IConversationRepository, ConversationRepository>();
```

No infra tests are added in this pass.

---

## Task 7: Add Conversation Query Models and Readers

**Files**

- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversations/GetConversationsQuery.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversations/ConversationListItemReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversations/ConversationsReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversations/IConversationsReader.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversations/GetConversationsHandler.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversation/GetConversationQuery.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversation/ConversationReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversation/ConversationPathNodeReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversation/ConversationSiblingGroupReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversation/ConversationSiblingReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversation/IConversationReader.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversation/GetConversationHandler.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversationTree/GetConversationTreeQuery.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversationTree/ConversationTreeReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversationTree/ConversationTreeNodeReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversationTree/IConversationTreeReader.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Queries/GetConversationTree/GetConversationTreeHandler.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Readers/ConversationsReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Readers/ConversationReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Readers/ConversationTreeReader.cs`

### Step 7.1: Add list query models

Query:

```csharp
public sealed record GetConversationsQuery : IQuery<ErrorOr<ConversationsReadModel>>;
```

Read models:

```csharp
public sealed record ConversationListItemReadModel
(
    Guid Id,
    string Title,
    Guid CurrentNodeId,
    DateTimeOffset UpdatedAt,
    string Preview
);

public sealed record ConversationsReadModel
(
    IReadOnlyCollection<ConversationListItemReadModel> Conversations
);
```

Handler:

- creates `UserId` from `IUserContext.UserId`
- returns validation errors for invalid user
- calls `IConversationsReader.GetAsync(userId, cancellationToken)`

Reader SQL:

```sql
select
    c.id as "Id",
    c.title as "Title",
    c.current_node_id as "CurrentNodeId",
    c.updated_at as "UpdatedAt",
    n.content as "Preview"
from conversations c
left join conversation_nodes n
  on n.id = c.current_node_id
 and n.conversation_id = c.id
where c.user_id = @UserId
order by c.updated_at desc, c.id;
```

### Step 7.2: Add active conversation query models

Query:

```csharp
public sealed record GetConversationQuery(Guid ConversationId) : IQuery<ErrorOr<ConversationReadModel>>;
```

Read models:

```csharp
public sealed record ConversationReadModel
(
    Guid Id,
    string Title,
    Guid CurrentNodeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<ConversationPathNodeReadModel> Nodes,
    IReadOnlyCollection<ConversationSiblingGroupReadModel> SiblingGroups
);

public sealed record ConversationPathNodeReadModel
(
    Guid Id,
    Guid? ParentNodeId,
    string Role,
    string Content,
    Guid? ModelId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int SiblingIndex
);

public sealed record ConversationSiblingGroupReadModel
(
    Guid? ParentNodeId,
    Guid SelectedNodeId,
    int SelectedSiblingIndex,
    int SiblingCount,
    IReadOnlyCollection<ConversationSiblingReadModel> Siblings
);

public sealed record ConversationSiblingReadModel
(
    Guid Id,
    Guid? ParentNodeId,
    string Role,
    string Preview,
    DateTimeOffset CreatedAt,
    int SiblingIndex,
    bool IsSelected
);
```

Reader must:

- filter by `conversation_id` and `user_id`
- return not found when no conversation row exists
- return active path from root-level node to `CurrentNodeId`
- include one sibling group for each node in the active path, including root-level siblings where `ParentNodeId` is null

Use `QueryMultipleAsync` with three result sets:

1. Conversation metadata.
2. Active path nodes.
3. Sibling rows for every active-path parent group.

Metadata SQL shape:

```sql
select
    c.id as "Id",
    c.title as "Title",
    c.current_node_id as "CurrentNodeId",
    c.created_at as "CreatedAt",
    c.updated_at as "UpdatedAt"
from conversations c
where c.id = @ConversationId and c.user_id = @UserId;
```

Active-path SQL shape:

```sql
with recursive active_path as (
    select n.*, 0 as depth
    from conversation_nodes n
    join conversations c on c.current_node_id = n.id
    where c.id = @ConversationId and c.user_id = @UserId

    union all

    select parent.*, active_path.depth + 1
    from conversation_nodes parent
    join active_path on active_path.parent_node_id = parent.id
)
select
    id as "Id",
    parent_node_id as "ParentNodeId",
    role as "Role",
    content as "Content",
    model_id as "ModelId",
    status as "Status",
    created_at as "CreatedAt",
    completed_at as "CompletedAt",
    sibling_index as "SiblingIndex"
from active_path
order by depth desc;
```

Sibling-summary SQL shape:

```sql
with recursive active_path as (
    select n.*, 0 as depth
    from conversation_nodes n
    join conversations c on c.current_node_id = n.id
    where c.id = @ConversationId and c.user_id = @UserId

    union all

    select parent.*, active_path.depth + 1
    from conversation_nodes parent
    join active_path on active_path.parent_node_id = parent.id
),
selected_groups as (
    select
        parent_node_id,
        id as selected_node_id,
        sibling_index as selected_sibling_index
    from active_path
)
select
    siblings.parent_node_id as "ParentNodeId",
    selected_groups.selected_node_id as "SelectedNodeId",
    selected_groups.selected_sibling_index as "SelectedSiblingIndex",
    count(*) over (partition by siblings.parent_node_id) as "SiblingCount",
    siblings.id as "Id",
    siblings.role as "Role",
    left(siblings.content, 160) as "Preview",
    siblings.created_at as "CreatedAt",
    siblings.sibling_index as "SiblingIndex",
    siblings.id = selected_groups.selected_node_id as "IsSelected"
from selected_groups
join conversation_nodes siblings
  on siblings.conversation_id = @ConversationId
 and siblings.parent_node_id is not distinct from selected_groups.parent_node_id
order by siblings.parent_node_id nulls first, siblings.sibling_index, siblings.id;
```

Reader mapping rules:

- Group sibling rows by `ParentNodeId`.
- Each group uses the row values `SelectedNodeId`, `SelectedSiblingIndex`, and `SiblingCount`.
- `SiblingCount` is the total sibling count for the active-path node's parent group.
- `IsSelected` is true only for the active-path node inside that sibling group.

### Step 7.3: Add full tree query models

Query:

```csharp
public sealed record GetConversationTreeQuery(Guid ConversationId) : IQuery<ErrorOr<ConversationTreeReadModel>>;
```

Read models:

```csharp
public sealed record ConversationTreeReadModel
(
    Guid Id,
    string Title,
    Guid CurrentNodeId,
    IReadOnlyCollection<ConversationTreeNodeReadModel> Nodes
);

public sealed record ConversationTreeNodeReadModel
(
    Guid Id,
    Guid? ParentNodeId,
    string Role,
    string Content,
    Guid? ModelId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int SiblingIndex
);
```

Reader SQL ordering:

```sql
select
    n.id as "Id",
    n.conversation_id as "ConversationId",
    n.parent_node_id as "ParentNodeId",
    n.role as "Role",
    n.content as "Content",
    n.model_id as "ModelId",
    n.status as "Status",
    n.created_at as "CreatedAt",
    n.completed_at as "CompletedAt",
    n.sibling_index as "SiblingIndex"
from conversation_nodes n
join conversations c on c.id = n.conversation_id
where c.id = @ConversationId and c.user_id = @UserId
order by n.parent_node_id nulls first, n.sibling_index, n.created_at, n.id;
```

No reader tests are added in this pass.

### Step 7.4: Register readers

In `Chat.Infrastructure.DependencyInjection`, register:

```csharp
services.AddScoped<IConversationsReader, ConversationsReader>();
services.AddScoped<IConversationReader, ConversationReader>();
services.AddScoped<IConversationTreeReader, ConversationTreeReader>();
```

---

## Task 8: Add FastEndpoints

**Files**

- Modify: `src/services/Chat/Chat.Api/Endpoints/CustomTags.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/CreateConversation/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/CreateConversation/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/GetConversations/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/GetConversation/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/GetConversationTree/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/AddUserMessage/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/AddUserMessage/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/SelectConversationNode/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/EditUserMessage/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/EditUserMessage/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationNodeResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationListResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationListItemResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ActiveConversationResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationPathNodeResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationSiblingGroupResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationSiblingResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationTreeResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationTreeNodeResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Conversations/Responses/ConversationResponseMapper.cs`

### Step 8.1: Add tag

Add:

```csharp
public const string Conversations = "Conversations";
```

### Step 8.2: Add routes

Routes:

- `POST /conversations`
- `GET /conversations`
- `GET /conversations/{conversationId}`
- `GET /conversations/{conversationId}/tree`
- `POST /conversations/{conversationId}/messages`
- `POST /conversations/{conversationId}/nodes/{nodeId}/select`
- `POST /conversations/{conversationId}/nodes/{nodeId}/edits`

Do not add:

- `POST /conversations/{conversationId}/nodes/{nodeId}/regenerations`

That endpoint is deferred until the SSE/server-sent streaming generation design exists.

### Step 8.3: Add request DTOs

Create request DTOs:

```csharp
internal sealed class CreateConversation.Request
{
    public required string Title { get; init; }
    public required string Message { get; init; }
}

internal sealed class AddUserMessage.Request
{
    public Guid? ParentNodeId { get; init; }
    public required string Message { get; init; }
}

internal sealed class EditUserMessage.Request
{
    public required string Message { get; init; }
}
```

Use actual namespace-per-folder classes named `Request`; the qualified names above describe the folder ownership.

### Step 8.4: Add response DTOs

Required response DTOs:

```csharp
internal sealed class ConversationResponse
{
    public required Guid Id { get; init; }
    public required string UserId { get; init; }
    public required string Title { get; init; }
    public required Guid CurrentNodeId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required IReadOnlyCollection<ConversationNodeResponse> Nodes { get; init; }
}

internal sealed class ConversationNodeResponse
{
    public required Guid Id { get; init; }
    public required Guid ConversationId { get; init; }
    public required Guid? ParentNodeId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required Guid? ModelId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset? CompletedAt { get; init; }
    public required int SiblingIndex { get; init; }
}

internal sealed class ConversationListResponse
{
    public required IReadOnlyCollection<ConversationListItemResponse> Conversations { get; init; }
}

internal sealed class ConversationListItemResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required Guid CurrentNodeId { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string Preview { get; init; }
}

internal sealed class ActiveConversationResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required Guid CurrentNodeId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required IReadOnlyCollection<ConversationPathNodeResponse> Nodes { get; init; }
    public required IReadOnlyCollection<ConversationSiblingGroupResponse> SiblingGroups { get; init; }
}

internal sealed class ConversationPathNodeResponse
{
    public required Guid Id { get; init; }
    public required Guid? ParentNodeId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required Guid? ModelId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset? CompletedAt { get; init; }
    public required int SiblingIndex { get; init; }
}

internal sealed class ConversationSiblingGroupResponse
{
    public required Guid? ParentNodeId { get; init; }
    public required Guid SelectedNodeId { get; init; }
    public required int SelectedSiblingIndex { get; init; }
    public required int SiblingCount { get; init; }
    public required IReadOnlyCollection<ConversationSiblingResponse> Siblings { get; init; }
}

internal sealed class ConversationSiblingResponse
{
    public required Guid Id { get; init; }
    public required Guid? ParentNodeId { get; init; }
    public required string Role { get; init; }
    public required string Preview { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required int SiblingIndex { get; init; }
    public required bool IsSelected { get; init; }
}

internal sealed class ConversationTreeResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required Guid CurrentNodeId { get; init; }
    public required IReadOnlyCollection<ConversationTreeNodeResponse> Nodes { get; init; }
}

internal sealed class ConversationTreeNodeResponse
{
    public required Guid Id { get; init; }
    public required Guid? ParentNodeId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required Guid? ModelId { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset? CompletedAt { get; init; }
    public required int SiblingIndex { get; init; }
}
```

### Step 8.5: Add response mapper

`ConversationResponseMapper` maps:

- `ConversationResult -> ConversationResponse`
- `ConversationNodeResult -> ConversationNodeResponse`
- `ConversationsReadModel -> ConversationListResponse`
- `ConversationReadModel -> ActiveConversationResponse`
- `ConversationTreeReadModel -> ConversationTreeResponse`

All collection mappings should preserve the ordering already supplied by application results/read models.

### Step 8.6: Endpoint behavior

Each endpoint should:

- use `ISender`
- use request DTOs for body fields
- use `Route<Guid>` for route IDs
- call `sender.Send(commandOrQuery, ct)`
- return `CustomResults.Problem(result)` when `ErrorOr` is error
- map application read/result models into API response DTOs

Endpoint mapping:

- `CreateConversation.Endpoint : BaseEndpoint<Request, ConversationResponse>`
  - Sends `CreateConversationCommand(request.Title, request.Message)`.
  - Returns `201 Created` or `200 OK`; use `201 Created` if a stable location header is easy, otherwise `200 OK` with the created conversation.
- `GetConversations.Endpoint : EndpointWithoutRequest<ConversationListResponse>`
  - Sends `GetConversationsQuery`.
- `GetConversation.Endpoint : EndpointWithoutRequest<ActiveConversationResponse>`
  - Reads `conversationId` route value.
  - Sends `GetConversationQuery(conversationId)`.
- `GetConversationTree.Endpoint : EndpointWithoutRequest<ConversationTreeResponse>`
  - Reads `conversationId` route value.
  - Sends `GetConversationTreeQuery(conversationId)`.
- `AddUserMessage.Endpoint : BaseEndpoint<Request, ConversationNodeResponse>`
  - Reads `conversationId` route value.
  - Sends `AddUserMessageCommand(conversationId, request.ParentNodeId, request.Message)`.
- `SelectConversationNode.Endpoint : EndpointWithoutRequest<ConversationResponse>`
  - Reads `conversationId` and `nodeId` route values.
  - Sends `SelectConversationNodeCommand(conversationId, nodeId)`.
- `EditUserMessage.Endpoint : BaseEndpoint<Request, ConversationNodeResponse>`
  - Reads `conversationId` and `nodeId` route values.
  - Sends `EditUserMessageCommand(conversationId, nodeId, request.Message)`.

No API endpoint tests are added in this pass.

---

## Task 9: Add EF Migration

**Files**

- Create: EF-generated migration file ending in `_ConversationTree.cs`
- Create: EF-generated migration designer file ending in `_ConversationTree.Designer.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

### Step 9.1: Generate migration

Use the same project/startup pattern as existing Chat migrations. If using the CLI manually, run:

```bash
dotnet ef migrations add ConversationTree \
  --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj \
  --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj \
  --context ChatDbContext \
  --output-dir Database/Migrations
```

Codex must request elevated permission before running this command.

### Step 9.2: Inspect generated migration

Verify:

- `conversations` table exists.
- `conversation_nodes` table exists.
- `conversation_nodes.conversation_id` has cascade delete to `conversations.id`.
- `conversation_nodes.parent_node_id` self-reference uses restricted delete behavior.
- `conversations.current_node_id` is generated as non-null.
- no FK exists from `conversations.current_node_id`.
- no FK exists from `conversation_nodes.model_id`.
- indexes match this plan.
- MassTransit outbox/inbox schema is not changed beyond incidental snapshot ordering.

---

## Task 10: Verification

### Step 10.1: Run approved tests

Run:

```bash
dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter Conversations
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter Conversations
```

Expected result: all approved domain and application conversation tests pass. Codex must request elevated permission before running these commands.

### Step 10.2: Run targeted build

Run:

```bash
dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj
```

Expected result: build succeeds. Codex must request elevated permission before running this command.

### Step 10.3: Final implementation note

When finishing implementation, explicitly report:

- Domain tests added and run.
- Application tests added and run.
- Infrastructure tests were not added because that test approach is not designed yet.
- API endpoint tests were not added because that test approach is not designed yet.
- Public regeneration endpoint was deferred until SSE/server-sent streaming generation is designed.
- Actual LLM generation and streaming were not implemented.

---

## Manual Checklist Summary

- [ ] Domain value objects implemented.
- [ ] Domain value-object tests implemented.
- [ ] Conversation aggregate implemented.
- [ ] Conversation aggregate tests implemented.
- [ ] Application results, errors, fakes implemented.
- [ ] Create conversation command implemented and tested.
- [ ] Message mutation commands implemented and tested.
- [ ] EF mappings and repository implemented.
- [ ] Dapper read models and readers implemented.
- [ ] FastEndpoints implemented except regeneration.
- [ ] EF migration generated and inspected.
- [ ] Domain conversation tests pass.
- [ ] Application conversation tests pass.
- [ ] Chat API build passes.
- [ ] Final note documents deferred infra/API tests and deferred SSE regeneration endpoint.
