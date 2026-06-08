# Conversation Tree Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add backend support for ChatGPT-style conversation trees in the Chat service.

**Architecture:** `Conversation` is a Chat-domain aggregate root that owns `ConversationNode` child entities and stores `CurrentNodeId` as the active branch pointer. Command handlers mutate the aggregate through repositories and `IUnitOfWork`; Dapper readers project conversation lists, active paths, and full trees. FastEndpoints expose authenticated user operations while future assistant generation can reuse the same application commands.

**Tech Stack:** .NET 10, `Mediator.SourceGenerator` / `Mediator.Abstractions`, FastEndpoints, EF Core, Npgsql, Dapper, ErrorOr, PostgreSQL.

---

## Constraints

- Do not replace the existing `Mediator` package family with MediatR.
- Use FastEndpoints for HTTP endpoints.
- Do not upgrade MassTransit.
- Domain and application tests are approved for this feature.
- Do not create or modify infrastructure or API endpoint tests in this pass.
- Ask for elevated permission before running `dotnet restore`, `dotnet build`, `dotnet test`, `dotnet run`, `dotnet ef`, or similar .NET commands.
- Keep actual LLM generation and streaming out of this implementation pass.
- Keep attachment, tool-call, sharing, and search support out of this implementation pass.

## File Structure

### Domain

- Create: `src/services/Chat/Chat.Domain/Conversations/Conversation.cs`
  - Aggregate root, node collection, current-node pointer, tree mutation methods.
- Create: `src/services/Chat/Chat.Domain/Conversations/Entities/ConversationNode.cs`
  - Child entity for user and assistant messages.
- Create: `src/services/Chat/Chat.Domain/Conversations/ConversationErrors.cs`
  - Domain errors for missing nodes, invalid parent roles, invalid edit/regenerate targets.
- Create: `src/services/Chat/Chat.Domain/Conversations/IConversationRepository.cs`
  - Aggregate persistence boundary.
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/ConversationId.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/ConversationNodeId.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/ConversationTitle.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/MessageContent.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/MessageRole.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/MessageStatus.cs`

### Application

- Create: `src/services/Chat/Chat.Application/Conversations/ConversationLimits.cs`
  - Shared max lengths for title/content.
- Create: `src/services/Chat/Chat.Application/Conversations/Errors/ConversationOperationErrors.cs`
  - Application-level missing/ownership/conflict errors.
- Create: `src/services/Chat/Chat.Application/Conversations/Results/ConversationResult.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Results/ConversationNodeResult.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Results/ConversationResultMapper.cs`
- Create command folders under `src/services/Chat/Chat.Application/Conversations/Commands/`:
  - `CreateConversation`
  - `AddUserMessage`
  - `AddAssistantMessage`
  - `SelectConversationNode`
  - `EditUserMessage`
  - `RegenerateAssistantMessage`
- Create query folders under `src/services/Chat/Chat.Application/Conversations/Queries/`:
  - `GetConversations`
  - `GetConversation`
  - `GetConversationTree`

### Infrastructure

- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Configurations/ConversationConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Configurations/ConversationNodeConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Repositories/ConversationRepository.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Readers/ConversationsReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Readers/ConversationReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Readers/ConversationTreeReader.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`
  - Add `DbSet<Conversation>`.
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
  - Register repository and readers.
- Create: EF-generated migration files with the `ConversationTree` suffix under `src/services/Chat/Chat.Infrastructure/Database/Migrations/`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

### API

- Modify: `src/services/Chat/Chat.Api/Endpoints/CustomTags.cs`
  - Add a `Conversations` tag.
- Create FastEndpoints under `src/services/Chat/Chat.Api/Endpoints/Conversations/`:
  - `CreateConversation`
  - `GetConversations`
  - `GetConversation`
  - `GetConversationTree`
  - `AddUserMessage`
  - `SelectConversationNode`
  - `EditUserMessage`
  - Do not create a public regeneration endpoint until the SSE/server-sent streaming generation design exists.
- Create or modify domain test files under `tests/Chat/Chat.Domain.Tests/Conversations/`.
- Create or modify application test files under `tests/Chat/Chat.Application.Tests/Conversations/`.

## Task 1: Add Conversation Domain Model

**Files:**

- Create: `src/services/Chat/Chat.Domain/Conversations/Conversation.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/Entities/ConversationNode.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ConversationErrors.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/IConversationRepository.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/ConversationId.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/ConversationNodeId.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/ConversationTitle.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/MessageContent.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/MessageRole.cs`
- Create: `src/services/Chat/Chat.Domain/Conversations/ValueObjects/MessageStatus.cs`

- [ ] Add UUID v7 IDs for `ConversationId` and `ConversationNodeId`, matching the existing strongly typed ID pattern.
- [ ] Add domain tests for `ConversationId` and `ConversationNodeId` validation.
- [ ] Add `ConversationTitle` with a max length defined later in `ConversationLimits`.
- [ ] Add domain tests for `ConversationTitle` required and max-length validation.
- [ ] Add `MessageContent` with trimming, required validation, and max length.
- [ ] Add domain tests for `MessageContent` trimming, required validation, and max-length validation.
- [ ] Add `MessageRole` as a domain value object or enum-like type with `User` and `Assistant`.
- [ ] Add `MessageStatus` with `Completed` only in this pass; reserve `Pending` and `Failed` for the later server-side generation flow.
- [ ] Implement `Conversation.Create(userId, title, initialMessage, createdAt)` so the aggregate starts with one root-level user node and `CurrentNodeId` points to it.
- [ ] Add a domain test proving creation creates one root-level user node and sets `CurrentNodeId` to it.
- [ ] Implement domain `AddUserMessage(parentNodeId, content, createdAt)` with a required resolved parent node id. The application handler resolves an omitted request parent to `conversation.CurrentNodeId` before calling the aggregate.
- [ ] Add domain tests proving user messages can branch from an explicit parent and receive the next sibling index.
- [ ] Implement `AddAssistantMessage(parentNodeId, content, modelId, createdAt, completedAt)` and require the parent node to be a user node.
- [ ] Add domain tests proving assistant messages can only be added under user nodes.
- [ ] Implement `SelectNode(nodeId)` and require the node to exist in the aggregate.
- [ ] Add domain tests proving selection updates `CurrentNodeId` and missing-node selection fails.
- [ ] Implement `EditUserMessage(nodeId, content, createdAt)` and require the target node to be a user node.
- [ ] Add domain tests proving editing a user node creates a sibling and does not mutate the original node.
- [ ] Add a domain test proving editing the first/root user message creates a root-level sibling.
- [ ] Implement `RegenerateAssistantMessage(nodeId, content, modelId, createdAt, completedAt)` and require the target node to be an assistant node.
- [ ] Add domain tests proving regenerating an assistant node creates an assistant sibling and does not mutate the original node.
- [ ] Assign `SiblingIndex` by counting existing children under the same parent; root-level user nodes share the same null-parent sibling group.
- [ ] Keep all node mutation methods internal to `Conversation` so parent-child invariants cannot be bypassed.

## Task 2: Add Application Commands

**Files:**

- Create: `src/services/Chat/Chat.Application/Conversations/ConversationLimits.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Errors/ConversationOperationErrors.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Results/ConversationResult.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Results/ConversationNodeResult.cs`
- Create: `src/services/Chat/Chat.Application/Conversations/Results/ConversationResultMapper.cs`
- Create command files under `src/services/Chat/Chat.Application/Conversations/Commands/*`

- [ ] Add command records using `ICommand<ErrorOr<...>>`.
- [ ] Add FluentValidation validators for request-level fields.
- [ ] Add application tests for validator failures on blank content, empty identifiers, and oversized text.
- [ ] In each handler, create `UserId` from `IUserContext.UserId`.
- [ ] Add application tests proving invalid or missing authenticated user ids return validation failures.
- [ ] Load conversations through `IConversationRepository` with both `ConversationId` and `UserId`.
- [ ] Return not found when the conversation does not exist or belongs to another user.
- [ ] Add application tests proving missing and cross-user conversations both return not found.
- [ ] Delegate tree invariants to aggregate methods.
- [ ] Add application tests proving aggregate conflict errors are propagated for invalid edit/regenerate/add-assistant targets.
- [ ] Save through `IUnitOfWork.SaveChangesAsync`.
- [ ] Add application tests proving successful commands call the unit of work exactly once and failing commands do not save.
- [ ] Map aggregate results with `ConversationResultMapper`.
- [ ] Keep `AddAssistantMessageCommand` available to application code but do not expose a public endpoint for arbitrary assistant content unless needed for local development.

## Task 3: Add Persistence Mapping and Repository

**Files:**

- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Configurations/ConversationConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Configurations/ConversationNodeConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Repositories/ConversationRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] Map `conversations` with strongly typed ID conversions.
- [ ] Map `conversation_nodes` with strongly typed ID conversions.
- [ ] Configure `Conversation.Nodes` using field access, following the `LlmProvider.Models` pattern.
- [ ] Configure cascade delete from conversation to nodes.
- [ ] Configure restricted self-reference from node to parent node.
- [ ] Add indexes for owner-scoped conversation lookup and parent-child traversal.
- [ ] Store `current_node_id` without a database FK and enforce it through the aggregate and repository.
- [ ] Store `model_id` without a database FK so conversation history survives catalog model deletion.
- [ ] Add `DbSet<Conversation>` to `ChatDbContext`.
- [ ] Register `IConversationRepository` and conversation readers in `Chat.Infrastructure.DependencyInjection`.

## Task 4: Add Query Read Models and Dapper Readers

**Files:**

- Create query models under `src/services/Chat/Chat.Application/Conversations/Queries/GetConversations/`
- Create query models under `src/services/Chat/Chat.Application/Conversations/Queries/GetConversation/`
- Create query models under `src/services/Chat/Chat.Application/Conversations/Queries/GetConversationTree/`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Readers/ConversationsReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Readers/ConversationReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Conversations/Readers/ConversationTreeReader.cs`

- [ ] Add `IConversationsReader`, `IConversationReader`, and `IConversationTreeReader` contracts near their query folders, following the existing favorite-model reader pattern.
- [ ] Implement conversation list SQL ordered by `updated_at desc, id`.
- [ ] Implement active path SQL with a recursive CTE starting at `current_node_id`.
- [ ] Implement sibling summary SQL grouped by each active-path node's parent.
- [ ] Implement full tree SQL ordered by `parent_node_id nulls first, sibling_index, created_at, id`.
- [ ] Ensure every reader filters by `user_id` so ownership is enforced in read paths.
- [ ] Keep projection models separate from aggregate result models.

## Task 5: Add FastEndpoints Contracts

**Files:**

- Modify: `src/services/Chat/Chat.Api/Endpoints/CustomTags.cs`
- Create FastEndpoints under `src/services/Chat/Chat.Api/Endpoints/Conversations/`

- [ ] Add `CustomTags.Conversations`.
- [ ] Add `POST /conversations` for an initial user message.
- [ ] Add `GET /conversations` for the conversation list.
- [ ] Add `GET /conversations/{conversationId}` for the active path.
- [ ] Add `GET /conversations/{conversationId}/tree` for full branch data.
- [ ] Add `POST /conversations/{conversationId}/messages` for user continuation and branching.
- [ ] Add `POST /conversations/{conversationId}/nodes/{nodeId}/select` for current branch selection.
- [ ] Add `POST /conversations/{conversationId}/nodes/{nodeId}/edits` for user-message edits.
- [ ] Defer `POST /conversations/{conversationId}/nodes/{nodeId}/regenerations` until the SSE/server-sent streaming generation design exists.
- [ ] Map `ErrorOr` failures through existing `CustomResults.Problem`.
- [ ] Use route values for IDs and request bodies for content.

## Task 6: Add EF Migration

**Files:**

- Create: EF-generated `*_ConversationTree.cs`
- Create: EF-generated `*_ConversationTree.Designer.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

- [ ] Request elevated permission before running `dotnet ef migrations add ConversationTree`.
- [ ] Generate the migration from the Chat infrastructure project and startup API project, following existing migration worker conventions.
- [ ] Inspect generated migration for table names, indexes, delete behaviors, and nullable columns.
- [ ] Confirm no MassTransit package or outbox schema changes are introduced except incidental snapshot ordering.

## Task 7: Verification

**Files:**

- No source files beyond the implementation files above.

- [ ] Ask for elevated permission before running any `dotnet` command.
- [ ] Run a targeted build for the Chat API or solution once implementation is complete.
- [ ] Run approved domain and application tests after implementation.
- [ ] Do not add infra/API tests.
- [ ] Document that infrastructure and API endpoint behavior are left untested because those test approaches have not been designed yet.

## Approved and Deferred Test Work

Approved in this pass:

- Domain tests for root creation, append, select, edit, regenerate, sibling ordering, and invalid role transitions.
- Application tests for owner-scoped not found behavior.

Deferred from this pass:

- Infrastructure tests, including Dapper reader tests for active-path ordering.
- API endpoint contract tests.

Leave a final implementation note that infra/API tests are intentionally absent because those testing approaches have not been designed yet.

## Migration From Linear Messages If Needed

The checked-out branch has no product conversation table. If a separate branch introduces linear messages before this work lands, add a migration task before Task 6:

- Create one `conversations` row per legacy conversation.
- Insert each legacy message as a `conversation_nodes` row.
- Set each node's parent to the previous legacy message in chronological order.
- Set every migrated `sibling_index` to `0`.
- Set `current_node_id` to the last migrated node.

## Execution Handoff

The user approved domain and application tests and chose to defer the public regeneration endpoint until the SSE/server-sent streaming generation design exists. Implement task-by-task and verify with the approved commands.
