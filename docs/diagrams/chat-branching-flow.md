# Chat Branching Flow

How Nova's chat aggregate supports both branches inside one conversation and **Branch in new
chat**, which turns one selected conversation path into a new, independent `ChatThread`.
Companion to [the `ChatThread` aggregate diagrams](chat-thread-aggregate.md),
[the branching design](../superpowers/specs/2026-06-20-chat-branching-design.md), and
[the implementation plan](../superpowers/plans/2026-06-20-chat-branching.md).

## Two kinds of branching

Editing a user message or regenerating an assistant response creates a sibling inside the same
`ChatThread`. **Branch in new chat** crosses an aggregate boundary: it selects a terminal
assistant message, copies only that message's ancestor path, and gives the copy a new chat ID and
new message IDs.

The branch request also carries the first new user message. After the snapshot is built, that
message and a generating assistant message are appended to the new aggregate through the same
domain operations used by an ordinary conversation.

```mermaid
flowchart LR
    subgraph source["Source ChatThread S — unchanged"]
        direction TB
        SU0["U0 user (root)"]
        SA1["A1 assistant"]
        SU2["U2 user"]
        SA3["A3 assistant<br/>selected branch point"]
        SA3R["A3r regenerated assistant<br/>excluded sibling"]
        SU4["U4 later user<br/>excluded descendant"]

        SU0 --> SA1 --> SU2
        SU2 --> SA3
        SU2 --> SA3R
        SA3 --> SU4
    end

    subgraph branch["New ChatThread B — independent snapshot"]
        direction TB
        BU0["U0′ copied user<br/>new ID"]
        BA1["A1′ copied assistant<br/>new ID"]
        BU2["U2′ copied user<br/>new ID"]
        BA3["A3′ copied assistant<br/>new ID"]
        BUN["UN new user<br/>submitted with request"]
        BAN["AN new assistant<br/>Generating"]

        BU0 --> BA1 --> BU2 --> BA3 --> BUN --> BAN
    end

    SA3 -. "snapshot selected path" .-> BA3
    HEAD(("HEAD")) -.-> BAN

    classDef user fill:#1f6feb,color:#fff,stroke:#58a6ff
    classDef assistant fill:#238636,color:#fff,stroke:#3fb950
    classDef selected fill:#d29922,color:#000,stroke:#f2cc60,stroke-width:3px
    classDef excluded fill:#30363d,color:#8b949e,stroke:#6e7681,stroke-dasharray:5 5
    classDef head fill:#d29922,color:#000,stroke:#f2cc60

    class SU0,SU2,BU0,BU2,BUN user
    class SA1,BA1,BA3,BAN assistant
    class SA3 selected
    class SA3R,SU4 excluded
    class HEAD head
```

The source aggregate is only read. `SA3R`, `SU4`, and every other node outside the selected
root-to-`SA3` path stay in the source and are not copied. Once created, the new thread contains
ordinary messages and can branch internally, be edited, regenerated, or itself become the source
of another independent branch.

## Aggregate-owned snapshot model

```mermaid
classDiagram
    class ChatThread {
        ChatId Id
        UserId UserId
        ChatTitle Title
        ChatMessageId CurrentMessageId
        ChatBranchOrigin? BranchOrigin
        IReadOnlyCollection~ChatMessage~ Messages
        BranchFrom(source, branchPointId, createdAt)$ ErrorOr~ChatThread~
        AddUserMessage(parentMessageId, content, createdAt) ErrorOr~ChatMessage~
        BeginAssistantMessage(parentMessageId, modelId, createdAt) ErrorOr~ChatMessage~
    }

    class ChatMessage {
        ChatMessageId Id
        ChatId ChatId
        ChatMessageId? ParentMessageId
        MessageRole Role
        MessageContent? Content
        LlmModelId? LlmModelId
        MessageStatus Status
        FailureReason? FailureReason
        DateTimeOffset CreatedAt
        DateTimeOffset? CompletedAt
        SiblingIndex SiblingIndex
        CopyForBranch(id, chatId, parentMessageId) ChatMessage
    }

    class ChatBranchOrigin {
        ChatId SourceChatId
        ChatMessageId SourceMessageId
        Create(sourceChatId, sourceMessageId)$ ChatBranchOrigin
    }

    ChatThread "1" *-- "1..*" ChatMessage : owns
    ChatThread "1" *-- "0..1" ChatBranchOrigin : immediate provenance
    ChatMessage --> "0..1" ChatMessage : ParentMessageId
```

`ChatThread.BranchFrom` owns path validation, ID remapping, metadata initialization, and head
selection. `ChatMessage.CopyForBranch` is `internal`, and `ChatBranchOrigin.Create` is also
domain-internal. Callers therefore cannot construct a half-remapped message tree or set only one
half of the source chat/message lineage pair.

The snapshot is linear at creation even when the source is a tree. That makes each copied message
the first child in its parent group, while preserving all content and lifecycle facts from the
selected source path.

## Fresh identities and remapped parents

The copy never reuses an entity identity or leaves a parent reference pointing across aggregate
boundaries. `BranchFrom` first creates the complete source-to-copy ID map, then reconstructs the
path with those IDs.

```mermaid
flowchart LR
    subgraph ids["Identity map"]
        direction TB
        CS["source ChatId S"] --> CB["new ChatId B"]
        IU0["U0"] --> IBU0["U0′"]
        IA1["A1"] --> IBA1["A1′"]
        IU2["U2"] --> IBU2["U2′"]
        IA3["A3"] --> IBA3["A3′"]
    end

    subgraph parents["Copied parent chain in B"]
        direction TB
        PU0["U0′<br/>Parent = null"]
        PA1["A1′<br/>Parent = U0′"]
        PU2["U2′<br/>Parent = A1′"]
        PA3["A3′<br/>Parent = U2′"]
        PU0 --> PA1 --> PU2 --> PA3
    end

    CB -. "owns every copy" .-> PU0
    IBU0 -.-> PU0
    IBA1 -.-> PA1
    IBU2 -.-> PU2
    IBA3 -.-> PA3

    classDef source fill:#30363d,color:#c9d1d9,stroke:#6e7681
    classDef copy fill:#8957e5,color:#fff,stroke:#a371f7
    classDef user fill:#1f6feb,color:#fff,stroke:#58a6ff
    classDef assistant fill:#238636,color:#fff,stroke:#3fb950

    class CS,IU0,IA1,IU2,IA3 source
    class CB,IBU0,IBA1,IBU2,IBA3 copy
    class PU0,PU2 user
    class PA1,PA3 assistant
```

Every copied message preserves its role, content, model ID, status, failure reason, `CreatedAt`,
and `CompletedAt`. Its `ChatId` becomes `B`, its `ParentMessageId` is remapped to the copied
parent, and its `SiblingIndex` becomes `SiblingIndex.First()`. The copied `A3′` becomes the new
thread's initial `CurrentMessageId`; the handler then advances the head as it appends the new turn.

## `BranchFrom` guards and snapshot algorithm

Only a non-temporary chat and a terminal assistant message can produce an independent branch.
The ancestry checks also protect the operation from malformed persisted trees before any new
aggregate is returned.

```mermaid
flowchart TD
    START(["BranchFrom(source, branchPointId, createdAt)"])
    TEMP{"source.IsTemporary?"}
    FIND["Find selected message"]
    FOUND{"message exists?"}
    ROLE{"role is Assistant?"}
    GEN{"status is Generating?"}
    WALK["Walk ParentMessageId toward root<br/>record each visited ID"]
    PATH{"cycle or missing parent?"}
    ROOT{"root is a User<br/>with ParentMessageId = null?"}
    REVERSE["Reverse into root-to-branch-point order"]
    IDS["Create new ChatId and<br/>source-to-new message ID map"]
    COPY["Copy messages with new ownership,<br/>remapped parents, sibling index 0"]
    META["Initialize title, timestamps, defaults,<br/>and immediate BranchOrigin"]
    HEAD["Set head to copied branch point"]
    SUCCESS(["Return independent ChatThread"])

    E_TEMP["Chat.CannotBranchTemporaryChat"]
    E_MISSING["Chat.MessageNotFound"]
    E_ROLE["Chat.BranchPointMustBeAssistant"]
    E_GEN["Chat.CannotBranchWhileGenerating"]
    E_PATH["Chat.InvalidBranchPath"]

    START --> TEMP
    TEMP -- yes --> E_TEMP
    TEMP -- no --> FIND --> FOUND
    FOUND -- no --> E_MISSING
    FOUND -- yes --> ROLE
    ROLE -- no --> E_ROLE
    ROLE -- yes --> GEN
    GEN -- yes --> E_GEN
    GEN -- no --> WALK --> PATH
    PATH -- yes --> E_PATH
    PATH -- no --> ROOT
    ROOT -- no --> E_PATH
    ROOT -- yes --> REVERSE --> IDS --> COPY --> META --> HEAD --> SUCCESS

    classDef decision fill:#d29922,color:#000,stroke:#f2cc60
    classDef error fill:#da3633,color:#fff,stroke:#f85149
    classDef success fill:#238636,color:#fff,stroke:#3fb950

    class TEMP,FOUND,ROLE,GEN,PATH,ROOT decision
    class E_TEMP,E_MISSING,E_ROLE,E_GEN,E_PATH error
    class SUCCESS success
```

`Completed` and `Failed` assistant messages are both terminal and copyable. A failed assistant
keeps its failure reason and completion timestamp. A generating assistant is rejected because its
state is still changing and cannot be a stable snapshot boundary.

The new thread receives the source owner, a bounded `Branch: {source title}` title, branch-time
`CreatedAt`/`UpdatedAt`, no pin, and a non-archived state. The current implementation forbids a
temporary source, so a successfully created branch is non-temporary.

## End-to-end branch request

The final implementation uses a dedicated FastEndpoints route rather than optional branch fields
on normal chat creation:

```http
POST /v1/chats/{sourceChatId}/messages/{sourceMessageId}/branches
```

The body supplies the first new message, model ID, and generation options. The response is the
same turn-start shape used elsewhere, but every returned ID belongs to the new chat.

```mermaid
sequenceDiagram
    actor Browser
    participant API as BranchChat FastEndpoint
    participant H as BranchChatHandler
    participant MC as Model catalog
    participant R as Chat repository
    participant CT as ChatThread
    participant BUS as MassTransit EF outbox
    participant UOW as Unit of work

    Browser->>API: POST /v1/chats/{sourceChatId}/messages/{sourceMessageId}/branches<br/>{ message, modelId, forceUseSearch }
    API->>H: Send BranchChatCommand through Mediator
    H->>H: Validate user and value objects

    alt invalid user or value objects
        H-->>API: ErrorOr errors
        API-->>Browser: 400 Problem Details
    else values are valid
        H->>MC: Ensure model is usable<br/>(and tool-capable when search is forced)

        alt model is unavailable or unsuitable
            MC-->>H: model usability errors
            H-->>API: ErrorOr errors
            API-->>Browser: mapped Problem Details
        else model is usable
            H->>R: GetSnapshotByIdAsync(sourceChatId, userId)<br/>owner-scoped, no tracking

            alt source missing or owned by someone else
                R-->>H: null
                H-->>API: Chat.NotFound
                API-->>Browser: 404 Problem Details
            else source snapshot found
                R-->>H: source ChatThread snapshot
                H->>CT: BranchFrom(source, sourceMessageId, now)

                alt domain guard fails
                    CT-->>H: MessageNotFound / conflict / invalid path
                    H-->>API: ErrorOr errors
                    API-->>Browser: 404 / 409 / 500 Problem Details
                else independent snapshot created
                    CT-->>H: new ChatThread with copied path
                    H->>CT: AddUserMessage(copied head, submitted content, now)
                    H->>CT: BeginAssistantMessage(new user ID, model ID, now)
                    H->>R: Add(new thread only)
                    H->>BUS: Publish TurnRequested(new chat ID, new assistant ID, options)
                    Note over BUS,UOW: Publish occurs first; the EF outbox buffers<br/>the job in the unit-of-work transaction.
                    H->>UOW: SaveChangesAsync

                    alt transaction fails
                        UOW-->>H: exception
                        Note over R,BUS: No branch rows or deliverable generation job commit.
                    else commit succeeds
                        UOW-->>H: aggregate + outbox entry committed atomically
                        H-->>API: TurnStartedResult(new IDs)
                        API-->>Browser: 201 Created<br/>Location: /v1/chats/{newChatId}
                    end
                end
            end
        end
    end
```

The source is loaded as a no-tracking, owner-scoped snapshot and is never added back to the
repository. Authentication therefore does not reveal whether another user's source chat exists,
and successful branching cannot dirty or save the source aggregate.

Publishing `TurnRequested` before `SaveChangesAsync` is intentional. The MassTransit EF outbox
buffers that publication in the same transaction as the new chat and its messages, preventing a
saved branch without a generation job or a deliverable job for a branch that never committed.

## Persistence and historical lineage

Copied messages use the normal `chat_messages` schema. Immediate provenance is the only new
branch-specific state: EF Core maps `ChatBranchOrigin` as two nullable columns on `chats`, while a
database check constraint preserves the value object's both-or-neither invariant.

```mermaid
erDiagram
    CHATS ||--|{ CHAT_MESSAGES : owns

    CHATS {
        uuid id PK
        text user_id
        text title
        uuid current_message_id
        uuid branched_from_chat_id "nullable; no FK"
        uuid branched_from_message_id "nullable; no FK"
        timestamptz created_at
        timestamptz updated_at
        boolean is_temporary
        boolean is_archived
    }

    CHAT_MESSAGES {
        uuid id PK
        uuid chat_id FK
        uuid parent_message_id "nullable; copied parent in same chat"
        text role
        text status
        integer sibling_index
        timestamptz created_at
        timestamptz completed_at "nullable"
    }
```

The database enforces:

```text
(branched_from_chat_id is null) = (branched_from_message_id is null)
```

There is deliberately no foreign key from those provenance columns to the source. They record
historical origin, not aggregate ownership or a live dependency. Deleting, archiving, editing, or
switching branches in the source cannot cascade into or rewrite an already copied chat.

Each branch records only its immediate source. Repeated branching therefore forms a lineage chain
without embedding the entire history in every aggregate:

```mermaid
flowchart LR
    O["Original chat<br/>BranchOrigin = null"]
    B1["Branch 1<br/>Origin = Original / A3"]
    B2["Branch 2<br/>Origin = Branch 1 / A7"]

    O -- "immediate SourceChatId<br/>+ SourceMessageId" --> B1
    B1 -- "immediate SourceChatId<br/>+ SourceMessageId" --> B2

    classDef original fill:#1f6feb,color:#fff,stroke:#58a6ff
    classDef branch fill:#8957e5,color:#fff,stroke:#a371f7
    class O original
    class B1,B2 branch
```

Lineage is stored for traceability and future navigation, but the current read contracts do not
expose it and the current schema does not support querying descendant branches by source. Those
features can be added later without weakening snapshot independence.

## What this model guarantees

- **Aggregate-owned reconstruction:** path validation, identity remapping, copied state, metadata,
  and the initial head are created together by `ChatThread.BranchFrom`.
- **Immutable source operation:** the owner-scoped source snapshot is read but never mutated or
  persisted by the branch flow.
- **Fresh ownership:** the chat and every copied message receive new IDs, and all parent links
  target messages inside the new aggregate.
- **Path-only copying:** siblings, alternate branches, and descendants outside the selected
  root-to-assistant path stay behind.
- **Atomic start:** copied history, the first new turn, and the MassTransit outbox entry commit in
  one unit-of-work transaction.
- **Historical provenance:** `ChatBranchOrigin` records immediate lineage without foreign-key or
  lifecycle coupling to the source.
- **Ordinary behavior afterward:** once created, the branch is a regular `ChatThread` with the
  same edit, regenerate, selection, continuation, and future branching capabilities as any other
  non-temporary chat.
