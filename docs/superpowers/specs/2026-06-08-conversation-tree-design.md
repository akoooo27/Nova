# Conversation Tree Design

## Goal

Introduce backend conversation history that can represent ChatGPT-style branching. A user can continue from the active message, select an earlier branch, edit a prior user message, or regenerate an assistant response without destroying previous history.

This design is backend-first. It defines the domain model, persistence shape, application commands, read projections, and FastEndpoints contracts needed to store and navigate a conversation tree. Actual LLM generation, streaming transport, tool calls, attachments, sharing, search, and frontend UI are outside this scope.

## Current Architecture Fit

The current Chat service does not contain a product conversation aggregate or message table. `ChatDbContext` currently persists model catalog, favorite models, users, and MassTransit outbox/inbox state. The `conversation_id` column visible in MassTransit tables is transport metadata and must not be reused for chat history.

The conversation feature should follow existing Chat-service patterns:

- Domain entities live under `Chat.Domain`.
- Aggregate roots own child entities through private collections.
- Application requests use `Mediator.SourceGenerator` / `Mediator.Abstractions`.
- FastEndpoints expose HTTP routes and map `ErrorOr` failures through existing API infrastructure.
- EF Core maps aggregates for writes.
- Dapper readers project query shapes that are awkward or inefficient to load through aggregates.
- `IUserContext.UserId` establishes user ownership.

## Selected Approach

Use a `Conversation` aggregate root with owned `ConversationNode` child entities.

```text
Conversation
- ConversationId
- UserId
- Title
- CurrentNodeId
- CreatedAt
- UpdatedAt
- IReadOnlyCollection<ConversationNode>

ConversationNode
- ConversationNodeId
- ConversationId
- ParentNodeId
- Role
- Content
- ModelId
- Status
- CreatedAt
- CompletedAt
- SiblingIndex
```

`CurrentNodeId` identifies the selected node for the active branch. The active transcript is not stored with a global sort order. It is computed by walking from `CurrentNodeId` through `ParentNodeId` to the root, then reversing that path.

## Domain Boundary

`Conversation` is the aggregate root because tree mutations must preserve collection-wide invariants:

- A conversation belongs to exactly one `UserId`.
- A node's parent may be null only for root-level user nodes.
- A conversation starts with one root-level user node.
- A conversation may later contain multiple root-level user nodes when the first prompt is edited.
- A node's non-null parent must exist in the same conversation.
- `CurrentNodeId` must reference a node in the same conversation.
- Appending a node updates `CurrentNodeId` to the appended node.
- Selecting a branch updates `CurrentNodeId` without mutating nodes.
- Editing and regenerating create siblings; they do not overwrite old nodes.

`ConversationNode` is an entity, not an aggregate root. It has identity and lifecycle, but it is only valid inside its owning conversation.

The first implementation should support `User` and `Assistant` roles. `System` and `Tool` roles can be added later without changing the parent-child model.

## Node Semantics

A root-level node is a user message with no parent. The first user message in a conversation is the initial root-level node.

Continuing a conversation creates a user node as a child of the current node, then a later assistant node as a child of that user node. If the user has selected an earlier node, the new user node becomes a new branch from that earlier point.

Editing a user message creates a new user sibling:

- If the edited user node has no parent, the edited copy is another root-level user node with no parent.
- If the edited user node has a parent, the edited copy uses the same parent.
- Existing descendants remain unchanged.
- `CurrentNodeId` moves to the edited user node until an assistant response is added.

Regenerating an assistant response creates a new assistant sibling under the same user parent. The previous assistant node and its descendants remain available.

`SiblingIndex` is only for stable sibling ordering and branch navigation. It is not a transcript order and must not replace parent traversal.

## Persistence

Add two product tables:

```text
conversations
- id uuid primary key
- user_id text not null
- title text not null
- current_node_id uuid not null
- created_at timestamptz not null
- updated_at timestamptz not null

conversation_nodes
- id uuid primary key
- conversation_id uuid not null
- parent_node_id uuid null
- role text not null
- content text not null
- model_id uuid null
- status text not null
- created_at timestamptz not null
- completed_at timestamptz null
- sibling_index integer not null
```

Indexes:

- `conversations(user_id, updated_at desc, id)` for conversation lists.
- `conversations(user_id, id)` for owner-scoped lookups.
- `conversation_nodes(conversation_id, parent_node_id, sibling_index, id)` for branch navigation.
- `conversation_nodes(conversation_id, id)` for owner-scoped node lookup.

Foreign keys:

- `conversation_nodes.conversation_id -> conversations.id` with cascade delete.
- `conversation_nodes.parent_node_id -> conversation_nodes.id` with restricted delete.
- `conversations.current_node_id` is domain-enforced in the first implementation and does not get a database FK. This avoids cyclic insert and migration complexity.
- `conversation_nodes.model_id` is stored without a database FK in the first implementation. Conversation history should survive catalog changes and model deletion.

## Application Commands

Use `Mediator` commands and handlers:

- `CreateConversationCommand`
  - Creates a conversation for the authenticated user with an initial user node.
  - Sets `CurrentNodeId` to the initial user node.
- `AddUserMessageCommand`
  - Adds a user node under an explicit parent node or the current node.
  - Sets `CurrentNodeId` to the new user node.
- `AddAssistantMessageCommand`
  - Adds an assistant node under a user node.
  - Stores optional model metadata as an opaque identifier.
  - Sets `CurrentNodeId` to the new assistant node.
  - This command is application-facing for future generation flow and should not be a public client endpoint until assistant generation exists.
- `SelectConversationNodeCommand`
  - Moves `CurrentNodeId` to an existing node in the user's conversation.
- `EditUserMessageCommand`
  - Creates a sibling user node with replacement content.
  - Sets `CurrentNodeId` to the new user node.
- `RegenerateAssistantMessageCommand`
  - Creates a sibling assistant node under the same parent as an existing assistant node.
  - Sets `CurrentNodeId` to the new assistant node.

Handlers must verify authenticated ownership before loading or mutating a conversation.

## Read Models

Use Dapper readers for query projections:

- Conversation list:
  - `Id`, `Title`, `CurrentNodeId`, `UpdatedAt`, and a short preview from the active node.
- Active conversation:
  - Conversation metadata.
  - Active transcript path from root to `CurrentNodeId`.
  - Sibling summaries for each node in the active path so the UI can show branch counts and selected sibling position.
- Conversation tree:
  - Full node tree for a conversation. This can be a separate endpoint because it may grow large.

The active transcript query should use a PostgreSQL recursive CTE from `current_node_id` to the root-level node, then sort by computed depth ascending.

## FastEndpoints Contracts

Expose authenticated API operations:

```http
POST   /conversations
GET    /conversations
GET    /conversations/{conversationId}
GET    /conversations/{conversationId}/tree
POST   /conversations/{conversationId}/messages
POST   /conversations/{conversationId}/nodes/{nodeId}/select
POST   /conversations/{conversationId}/nodes/{nodeId}/edits
```

`POST /conversations` accepts an initial user message.

`POST /conversations/{conversationId}/messages` accepts a user message and optional `ParentNodeId`. If `ParentNodeId` is omitted, the application handler resolves it to the conversation's current node before calling the aggregate. Only `EditUserMessage` can create an additional root-level user node.

The future `POST /conversations/{conversationId}/nodes/{nodeId}/regenerations` operation will create a storage record for a generated assistant response only when a server-side generation flow exists. Until then, this endpoint is deferred while the domain command remains available for the future generation pipeline.

The future generation pipeline is expected to use SSE/server-sent streaming. That streaming design is not part of this spec, so this implementation must not publish a regeneration endpoint or streaming response contract yet.

## Error Handling

- Invalid identifiers or blank content return validation errors.
- Missing conversation returns not found.
- Conversation owned by a different user returns not found, not forbidden, to avoid leaking identifiers.
- Parent node missing from the conversation returns validation or conflict depending on where it is detected.
- Editing a non-user node returns conflict.
- Regenerating a non-assistant node returns conflict.
- Adding an assistant response under a non-user parent returns conflict.

## Migration Strategy

The checked-out branch has no existing product conversation data, so the preferred migration is a clean schema addition.

If another branch already contains a linear message table, migrate each conversation into a single chain:

1. Create one `conversations` row per old conversation.
2. Insert old messages as `conversation_nodes`.
3. Set each node's `parent_node_id` to the previous message in chronological order.
4. Set `sibling_index` to `0`.
5. Set `current_node_id` to the last migrated message.

This preserves existing transcripts but cannot recover historical branches unless the old schema already stored edits or regenerations separately.

## Testing Scope

The user approved domain and application test work for this feature.

Approved tests:

- Domain tests for tree invariants, sibling behavior, role rules, root edits, branch selection, and current-node updates.
- Application tests for command handling, authenticated ownership, not-found behavior, conflict behavior, and persistence-boundary interactions through fakes.

Not approved in this pass:

- Infrastructure tests.
- API endpoint tests.

Infra and API tests are intentionally left unimplemented because those testing approaches have not been designed yet. The implementation should leave a note in the final handoff calling out this residual coverage gap.

## Recommendation

Proceed with the conversation tree. The model is a better long-term fit than linear ordering if Nova will support edit, regenerate, and branch selection. Implement it before publishing a linear conversation API or storing production chat history, because migration becomes more expensive once clients depend on global message order.
