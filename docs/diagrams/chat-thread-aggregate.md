# ChatThread Aggregate

How the `ChatThread` aggregate (`src/services/Chat/Chat.Domain/Chats/ChatThread.cs`) models a
ChatGPT-style branching conversation tree. Companion to the invariants in
[the chat domain plan](../superpowers/plans/2026-06-09-chat-domain.md).

## Structure

`ChatThread` is the only aggregate root. Messages form a tree through `ParentMessageId`
(`null` = root); `CurrentMessageId` is the "head" — the active leaf the UI renders up from.

```mermaid
classDiagram
    class ChatThread {
        ChatId Id
        UserId UserId
        ChatTitle Title
        ChatMessageId CurrentMessageId
        DateTimeOffset CreatedAt
        DateTimeOffset UpdatedAt
        IReadOnlyCollection~ChatMessage~ Messages
        Create(userId, title, firstUserMessage, createdAt)$ ChatThread
        AddUserMessage(parentMessageId, content, createdAt) ErrorOr~ChatMessage~
        BeginAssistantMessage(parentMessageId, llmModelId, createdAt) ErrorOr~ChatMessage~
        CompleteAssistantMessage(messageId, content, completedAt) ErrorOr~ChatMessage~
        FailAssistantMessage(messageId, reason, failedAt) ErrorOr~ChatMessage~
        EditUserMessage(messageId, content, createdAt) ErrorOr~ChatMessage~
        RegenerateAssistant(messageId, llmModelId, createdAt) ErrorOr~ChatMessage~
        SelectMessage(messageId, updatedAt) ErrorOr~Success~
        FindMessage(messageId) ChatMessage?
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
    }

    class MessageRole {
        <<enumeration>>
        User
        Assistant
    }

    class MessageStatus {
        <<enumeration>>
        Generating
        Completed
        Failed
    }

    ChatThread "1" *-- "1..*" ChatMessage : _messages
    ChatMessage --> "0..1" ChatMessage : ParentMessageId
    ChatMessage --> MessageRole
    ChatMessage --> MessageStatus
```

`ChatMessage` factories and state transitions are `internal`: every creation and mutation
goes through `ChatThread`, which is what makes the invariants below enforceable.

## Strict alternation and branching

Every root-to-leaf path alternates `User → Assistant → User → …`. Editing a user message or
regenerating an assistant never mutates the tree — it adds a **sibling** under the same parent
and moves the head there. `SiblingIndex` is the creation order within one parent group, which
is what the UI's `< 2/3 >` branch switcher walks.

```mermaid
flowchart TD
    U0["U0 user (root)<br/>sibling 0"]
    A1["A1 assistant<br/>sibling 0"]
    U2["U2 user<br/>sibling 0"]
    U2e["U2' user (edited)<br/>sibling 1"]
    A3["A3 assistant<br/>sibling 0"]
    A4["A4 assistant (regenerated)<br/>sibling 1"]
    A5["A5 assistant<br/>sibling 0"]

    U0 --> A1
    A1 --> U2
    A1 --> U2e
    U2 --> A3
    U2 --> A4
    U2e --> A5

    HEAD(("HEAD")) -.-> A4

    style U0 fill:#1f6feb,color:#fff
    style U2 fill:#1f6feb,color:#fff
    style U2e fill:#1f6feb,color:#fff
    style A1 fill:#238636,color:#fff
    style A3 fill:#238636,color:#fff
    style A4 fill:#238636,color:#fff
    style A5 fill:#238636,color:#fff
    style HEAD fill:#d29922,color:#000
```

The rendered conversation is the walk from `CurrentMessageId` up the parent links to the
root, reversed: here `U0 → A1 → U2 → A4`. `SelectMessage` moves the head to any existing
message (status-agnostic by design — during a live turn the head sits on a `Generating`
assistant).

## Guards per method

```mermaid
flowchart LR
    subgraph adds["Adds a node + moves head"]
        AUM["AddUserMessage<br/>parent must be Assistant"]
        BAM["BeginAssistantMessage<br/>parent must be User"]
        EUM["EditUserMessage<br/>target must be an active-path User;<br/>active path must not be Generating;<br/>sibling under target's parent"]
        RA["RegenerateAssistant<br/>target must be Assistant<br/>and Completed/Failed"]
    end
    subgraph mutates["Mutates a node (head unchanged)"]
        CAM["CompleteAssistantMessage<br/>target must be Generating"]
        FAM["FailAssistantMessage<br/>target must be Generating"]
    end
    subgraph moves["Moves head only"]
        SM["SelectMessage<br/>target must exist"]
    end
```

## Assistant message lifecycle

User messages are born `Completed` (`CompletedAt = CreatedAt`) and never transition.
Assistant messages stream through `Generating`; the two terminal states are one-way.
Editing is restricted to user nodes on the current root-to-head path. If that path contains a
`Generating` assistant, editing is rejected until the turn reaches `Completed` or `Failed`.
Temporary chats use the same edit behavior because they share the same aggregate model.

```mermaid
stateDiagram-v2
    [*] --> Generating : BeginAssistantMessage /<br/>RegenerateAssistant
    Generating --> Completed : CompleteAssistantMessage<br/>(sets Content, CompletedAt)
    Generating --> Failed : FailAssistantMessage<br/>(sets FailureReason, CompletedAt)
    Completed --> [*]
    Failed --> [*]

    note right of Generating
        Content is null while generating.
        Regenerating a Generating target is
        rejected (CannotRegenerateWhileGenerating)
        to avoid racing two generations
        under one parent.
    end note
```

## A full turn

```mermaid
sequenceDiagram
    participant App as Application layer
    participant CT as ChatThread
    participant CM as ChatMessage (internal)

    App->>CT: AddUserMessage(parent: A1, "how?")
    CT->>CT: guard: parent is Assistant
    CT->>CM: CreateUserMessage (Completed)
    CT->>CT: head = new user message
    App->>CT: BeginAssistantMessage(parent: U2, model)
    CT->>CT: guard: parent is User
    CT->>CM: CreateAssistantMessage (Generating, Content = null)
    CT->>CT: head = generating assistant
    Note over App: tokens stream outside the domain
    alt generation succeeds
        App->>CT: CompleteAssistantMessage(A3, content)
        CT->>CM: Complete → Completed
    else generation fails
        App->>CT: FailAssistantMessage(A3, reason)
        CT->>CM: Fail → Failed
    end
```
