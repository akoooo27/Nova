# Chat Branching Diagrams Design

## Goal

Create `docs/diagrams/chat-branching-flow.md` as a companion to
`docs/diagrams/chat-thread-aggregate.md`. The document will explain how Nova's
aggregate model supports flexible branching and how **Branch in new chat** turns one selected
message path into a new, independent `ChatThread`.

The document is explanatory only. It will not change application behavior, contracts,
persistence, or tests.

## Audience and Source of Truth

The primary audience is developers learning or maintaining Nova's chat domain. The diagrams
must be understandable without reading the implementation first, while remaining precise
enough to guide readers to the relevant domain, application, API, and persistence code.

Current implementation is the source of truth. In particular, the document will show the
dedicated FastEndpoints route:

```http
POST /v1/chats/{sourceChatId}/messages/{sourceMessageId}/branches
```

This supersedes the earlier design proposal to extend `POST /v1/chats` with optional branch
identifiers. The rationale from the approved branching design remains useful where it matches
the final implementation.

## Narrative Structure

The document will use several focused Mermaid diagrams rather than one oversized diagram.
Its sections will be:

1. **Two kinds of branching** — distinguish sibling branches inside one `ChatThread` from
   creating a separate chat that snapshots one selected path.
2. **Aggregate model** — show `ChatThread`, `ChatMessage`, and `ChatBranchOrigin`, emphasizing
   aggregate ownership, parent links, the active head, and immediate provenance.
3. **Snapshot transformation** — show a branched source tree beside the new linear snapshot.
   Only the root-to-selected-assistant path is copied; siblings, alternate branches, and
   descendants are excluded.
4. **Identity and independence** — explain that the new chat and every copied message receive
   new IDs, parent links are remapped, copied sibling indexes become first-in-group, and the
   copied branch point becomes the new head.
5. **Domain guards and algorithm** — flowchart the `ChatThread.BranchFrom` validation and copy
   process, including temporary-chat rejection, assistant/terminal branch-point requirements,
   cycle and missing-parent detection, and root validation.
6. **End-to-end request flow** — sequence the FastEndpoint, `Mediator` handler, repositories,
   aggregate, MassTransit outbox, and unit of work. The submitted user message and generating
   assistant message are appended only to the new aggregate.
7. **Persistence and lineage** — show `ChatBranchOrigin` as an atomic immediate-source value
   stored without foreign keys. The lineage is historical metadata, not a live dependency.
8. **Atomicity and failure behavior** — state that copied messages, the new turn, and the outbox
   entry commit together; failures persist and publish nothing.

## Visual Conventions

The diagrams will follow the existing aggregate document's conventions:

- user messages use blue nodes;
- assistant messages use green nodes;
- selected heads or branch points use amber accents;
- copied nodes are labeled with source-to-new identity mappings;
- excluded source nodes remain visible but muted where that makes the copy boundary clear;
- prose directly below each diagram explains the invariant it demonstrates.

Diagram size will be kept readable in Markdown previews. Separate class, flowchart, sequence,
and persistence diagrams will be used where each notation communicates the relationship more
clearly than prose.

## Accuracy Boundaries

The document will explicitly preserve these implementation decisions:

- the source chat is owner-scoped and loaded as a no-tracking snapshot;
- only non-temporary chats may be branched;
- only terminal assistant messages may be selected;
- `BranchFrom` owns validation and message reconstruction;
- `ChatMessage.CopyForBranch` remains internal;
- copied message state and timestamps are preserved;
- the branch title is derived through `ChatTitle.CreateBranch`;
- `BranchOrigin` records only the immediate source chat/message pair;
- the source aggregate is never mutated;
- branching an already branched chat forms a lineage chain across independent aggregates;
- deletion or later mutation of a source does not alter the copied chat;
- the handler publishes `TurnRequested` before `SaveChangesAsync` so the MassTransit EF outbox
  stores it in the same transaction.

The document will not describe frontend implementation details beyond the fact that the branch
request carries the first new user message and begins the existing asynchronous assistant turn.
It will not imply descendant-branch queries, foreign-key lineage, source synchronization, or
new idempotency behavior.

## Verification

Because this is documentation-only work, verification will consist of:

- checking every named type, method, route, guard, and transaction step against current code;
- scanning Mermaid blocks for balanced syntax and readable labels;
- confirming all local links resolve;
- reviewing the finished document for contradictions with
  `chat-thread-aggregate.md`, the approved branching design, and the final implementation.

No tests will be added or changed, in accordance with the project instructions.
