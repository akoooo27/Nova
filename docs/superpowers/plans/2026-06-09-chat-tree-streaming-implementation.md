# Chat Tree + Streaming Generation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a ChatGPT-style branching chat with persisted message-tree storage and SSE token streaming backed by Redis Streams, in the Chat service.

**Architecture:** A `ChatThread` aggregate root (DB table `chats`, routes `/chats`) owns a tree of `ChatMessage` entities, each pointing at its `ParentMessageId`, with `CurrentMessageId` as the active-branch head pointer. Messages strictly alternate user/assistant along every root-to-leaf path; branching (edit a user message, regenerate an assistant message) creates sibling subtrees and never mutates existing messages. Assistant messages carry a `MessageStatus` state machine (`Generating → Completed | Failed`) with guarded transition methods. Generation is **decoupled from the HTTP connection**: a command persists the user message plus a `Generating` assistant message, then enqueues a job onto an in-process channel; a hosted worker runs the LLM call and publishes token deltas to a **Redis Stream** (`chat:stream:{messageId}`); a separate SSE endpoint replays that stream to the client and supports mid-stream resume via `Last-Event-ID`. Durable content is written to Postgres at generation **start** (empty, `Generating`) and **finish** (final text, `Completed`/`Failed`); the ephemeral token buffer lives only in Redis.

**Tech Stack:** .NET 10, `Mediator.SourceGenerator` / `Mediator.Abstractions`, FastEndpoints, EF Core + Npgsql, Dapper, StackExchange.Redis (Redis Streams), ErrorOr, FluentValidation, PostgreSQL.

---

## Ground Rules

- Use the existing `Mediator` package family. Do not introduce MediatR.
- Use FastEndpoints. Do not introduce controllers.
- Do not upgrade MassTransit.
- The aggregate C# type is **`ChatThread`** (a bare `Chat` type collides with the `Chat.*` root namespaces). DB tables and routes read as `chats` / `chat_messages` / `/chats`.
- A tree node **is** a `ChatMessage` (each message has a nullable `ParentMessageId`). There is no separate "node" type.
- Assistant message content is **null while `Generating`** and set on `Complete`. User messages always have content and are stored `Completed`.
- **Domain rules are owned by the domain plan** (`docs/superpowers/plans/2026-06-09-chat-domain.md`): strict user/assistant alternation (`AddUserMessage` requires an assistant parent → `Chat.UserParentMustBeAssistant`), one-way `Generating → Completed | Failed` transitions, and terminal-only regeneration (`RegenerateAssistant` rejects a `Generating` target → `Chat.CannotRegenerateWhileGenerating`). The application/infrastructure tasks here build on those exact signatures and error codes.
- Store `current_message_id`, `parent_message_id`, and `model_id` **without** database foreign keys in this pass (except the `chat_messages.chat_id → chats.id` cascade and the `parent_message_id` self-reference restrict).
- The real LLM provider call is the one pluggable piece: this pass ships a deterministic `StubChatCompletionClient` behind `IChatCompletionClient` so the full streaming pipeline is real and observable. Swapping in a real provider is an interface implementation only.
- **No test code in this pass** (explicit scope decision). Each task ends with a build + commit instead of a test run.
- Any `dotnet` command requires elevated permission first.

## Working Order

**Domain first:** Tasks 1–2 (the domain layer — value objects, `ChatMessage`, `ChatThread`, errors, repository interface) are the dedicated domain plan `docs/superpowers/plans/2026-06-09-chat-domain.md`. Implement it before this plan. This plan covers Tasks 3–11:

3. Application abstractions (stream store, completion client, generation queue) + results + errors.
4. Application generation commands (create / send / edit / regenerate / select).
5. Application queries + read models (list / active path / tree / stream-state).
6. EF mappings + repository + DbContext + DI wiring.
7. Dapper readers.
8. Redis streaming infrastructure (stream store, stub completion client, channel queue, generation worker, stale-generation sweeper).
9. FastEndpoints (CRUD + SSE stream endpoint).
10. EF migration.
11. Build verification.

Each task compiles before moving to the next. Commit after each task.

---

## Tasks 1–2: Domain Layer — see the dedicated domain plan

The domain layer (value objects, `ChatMessage`, `ChatThread`, `ChatErrors`, `IChatRepository`, invariants, and EF persistence boundary notes) is split into its own guide and is the **single source of truth** for the domain:

**→ `docs/superpowers/plans/2026-06-09-chat-domain.md`**

Implement that plan first. Two domain decisions finalized there are relied on by the application tasks below:

- **Strict alternation:** `AddUserMessage` requires the parent to be an **assistant** message (`Chat.UserParentMustBeAssistant`). User messages are roots or children of assistants.
- **Terminal regenerate target:** `RegenerateAssistant` rejects a `Generating` target (`Chat.CannotRegenerateWhileGenerating`); only `Completed`/`Failed` assistants can be regenerated.

The application/infrastructure tasks below assume the domain types and method signatures exactly as defined in that plan.

---

## Task 3: Application Abstractions, Results, Errors

**Files**
- Create: `src/services/Chat/Chat.Application/Chats/ChatLimits.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Errors/ChatOperationErrors.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Results/ChatGenerationResult.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Results/ChatGenerationResultMapper.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Chats/ChatGenerationJob.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Chats/IChatGenerationQueue.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Chats/ChatStreamEvent.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Chats/IChatStreamStore.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Chats/ChatCompletionContracts.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/Chats/IChatCompletionClient.cs`

- [ ] **Step 1: Create `ChatLimits`**

```csharp
namespace Chat.Application.Chats;

public static class ChatLimits
{
    public const int TitleMaxLength = 200;
    public const int MessageContentMaxLength = 32768;
}
```

- [ ] **Step 2: Create `ChatOperationErrors`**

```csharp
using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

namespace Chat.Application.Chats.Errors;

public static class ChatOperationErrors
{
    public static Error ChatNotFound(ChatId chatId) =>
        Error.NotFound
        (
            code: "Chat.NotFound",
            description: $"No chat found with id '{chatId.Value}'."
        );
}
```

- [ ] **Step 3: Create `ChatGenerationResult`**

Returned by every generation-triggering command. It gives the client exactly what it needs to open the SSE stream: the chat id and the assistant message id.

```csharp
namespace Chat.Application.Chats.Results;

public sealed record ChatGenerationResult
(
    Guid ChatId,
    Guid AssistantMessageId,
    Guid? ParentMessageId,
    Guid? ModelId,
    DateTimeOffset CreatedAt
);
```

- [ ] **Step 4: Create `ChatGenerationResultMapper`**

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;

namespace Chat.Application.Chats.Results;

internal static class ChatGenerationResultMapper
{
    public static ChatGenerationResult ToGenerationResult(this ChatThread chat, ChatMessage assistantMessage) => new
    (
        ChatId: chat.Id.Value,
        AssistantMessageId: assistantMessage.Id.Value,
        ParentMessageId: assistantMessage.ParentMessageId?.Value,
        ModelId: assistantMessage.ModelId,
        CreatedAt: assistantMessage.CreatedAt
    );
}
```

- [ ] **Step 5: Create `ChatGenerationJob`**

```csharp
namespace Chat.Application.Abstractions.Chats;

public sealed record ChatGenerationJob
(
    Guid ChatId,
    Guid AssistantMessageId,
    Guid ModelId,
    string UserId
);
```

- [ ] **Step 6: Create `IChatGenerationQueue`**

```csharp
namespace Chat.Application.Abstractions.Chats;

public interface IChatGenerationQueue
{
    ValueTask EnqueueAsync(ChatGenerationJob job, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatGenerationJob> DequeueAllAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 7: Create `ChatStreamEvent`**

```csharp
namespace Chat.Application.Abstractions.Chats;

public sealed record ChatStreamEvent
(
    string Id,
    string Type,
    string? Data
);

public static class ChatStreamEventTypes
{
    public const string Delta = "delta";
    public const string Done = "done";
    public const string Error = "error";
}
```

- [ ] **Step 8: Create `IChatStreamStore`**

```csharp
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Application.Abstractions.Chats;

public interface IChatStreamStore
{
    Task AppendDeltaAsync(ChatMessageId messageId, string delta, CancellationToken cancellationToken = default);

    Task CompleteAsync(ChatMessageId messageId, CancellationToken cancellationToken = default);

    Task FailAsync(ChatMessageId messageId, string reason, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ChatStreamEvent> ReadAsync
    (
        ChatMessageId messageId,
        string? lastEventId,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 9: Create `ChatCompletionContracts`**

```csharp
namespace Chat.Application.Abstractions.Chats;

public sealed record ChatCompletionMessage
(
    string Role,
    string Content
);

public sealed record ChatCompletionRequest
(
    Guid ModelId,
    IReadOnlyList<ChatCompletionMessage> Messages
);
```

- [ ] **Step 10: Create `IChatCompletionClient`**

```csharp
namespace Chat.Application.Abstractions.Chats;

public interface IChatCompletionClient
{
    IAsyncEnumerable<string> StreamAsync(ChatCompletionRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 11: Build**

Run: `dotnet build src/services/Chat/Chat.Application/Chat.Application.csproj`
Expected: build succeeds.

- [ ] **Step 12: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats src/services/Chat/Chat.Application/Abstractions/Chats
git commit -m "feat(chat): add chat application abstractions, results, and errors"
```

---

## Task 4: Generation Commands

All generation-triggering handlers share the same dependencies and shape: validate inputs → load/create aggregate → mutate → begin assistant message → `SaveChangesAsync` → **enqueue the generation job after the save commits** → return `ChatGenerationResult`.

**Files**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/EditUserMessage/EditUserMessageCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/EditUserMessage/EditUserMessageCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/EditUserMessage/EditUserMessageHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/RegenerateAssistantMessage/RegenerateAssistantMessageCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/RegenerateAssistantMessage/RegenerateAssistantMessageCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/RegenerateAssistantMessage/RegenerateAssistantMessageHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/SelectChatMessage/SelectChatMessageCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/SelectChatMessage/SelectChatMessageHandler.cs`

### Step 4.1: CreateChat

- [ ] **Step 1: Command**

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.CreateChat;

public sealed record CreateChatCommand
(
    string Title,
    string Message,
    Guid ModelId
) : ICommand<ErrorOr<ChatGenerationResult>>;
```

- [ ] **Step 2: Validator**

```csharp
using Chat.Application.Chats;

using FluentValidation;

namespace Chat.Application.Chats.Commands.CreateChat;

internal sealed class CreateChatCommandValidator : AbstractValidator<CreateChatCommand>
{
    public CreateChatCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(ChatLimits.TitleMaxLength);

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(ChatLimits.MessageContentMaxLength);

        RuleFor(x => x.ModelId)
            .NotEmpty();
    }
}
```

- [ ] **Step 3: Handler**

```csharp
using Chat.Application.Abstractions.Chats;
using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Results;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Chats.Commands.CreateChat;

internal sealed class CreateChatHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IChatGenerationQueue generationQueue) : ICommandHandler<CreateChatCommand, ErrorOr<ChatGenerationResult>>
{
    public async ValueTask<ErrorOr<ChatGenerationResult>> Handle(CreateChatCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatTitle> titleResult = ChatTitle.Create(command.Title);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Message);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.ModelId);

        List<Error> errors = [];

        if (userIdResult.IsError) errors.AddRange(userIdResult.Errors);
        if (titleResult.IsError) errors.AddRange(titleResult.Errors);
        if (contentResult.IsError) errors.AddRange(contentResult.Errors);
        if (modelIdResult.IsError) errors.AddRange(modelIdResult.Errors);

        if (errors.Count > 0)
            return errors;

        UserId userId = userIdResult.Value;
        Guid modelId = modelIdResult.Value.Value;

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ChatThread chat = ChatThread.Create
        (
            userId: userId,
            title: titleResult.Value,
            firstUserMessage: contentResult.Value,
            createdAt: now
        );

        ErrorOr<ChatMessage> assistantResult = chat.BeginAssistantMessage
        (
            parentMessageId: chat.CurrentMessageId,
            modelId: modelId,
            createdAt: now
        );

        if (assistantResult.IsError)
            return assistantResult.Errors;

        chats.Add(chat);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        ChatMessage assistant = assistantResult.Value;

        await generationQueue.EnqueueAsync
        (
            new ChatGenerationJob(chat.Id.Value, assistant.Id.Value, modelId, userId.Value),
            cancellationToken
        );

        return chat.ToGenerationResult(assistant);
    }
}
```

### Step 4.2: SendMessage

- [ ] **Step 1: Command**

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.SendMessage;

public sealed record SendMessageCommand
(
    Guid ChatId,
    Guid? ParentMessageId,
    string Message,
    Guid ModelId
) : ICommand<ErrorOr<ChatGenerationResult>>;
```

- [ ] **Step 2: Validator**

```csharp
using Chat.Application.Chats;

using FluentValidation;

namespace Chat.Application.Chats.Commands.SendMessage;

internal sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(ChatLimits.MessageContentMaxLength);

        RuleFor(x => x.ModelId)
            .NotEmpty();
    }
}
```

- [ ] **Step 3: Handler**

```csharp
using Chat.Application.Abstractions.Chats;
using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Chats.Commands.SendMessage;

internal sealed class SendMessageHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IChatGenerationQueue generationQueue) : ICommandHandler<SendMessageCommand, ErrorOr<ChatGenerationResult>>
{
    public async ValueTask<ErrorOr<ChatGenerationResult>> Handle(SendMessageCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Message);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.ModelId);

        List<Error> errors = [];

        if (userIdResult.IsError) errors.AddRange(userIdResult.Errors);
        if (chatIdResult.IsError) errors.AddRange(chatIdResult.Errors);
        if (contentResult.IsError) errors.AddRange(contentResult.Errors);
        if (modelIdResult.IsError) errors.AddRange(modelIdResult.Errors);

        ChatMessageId? explicitParentId = null;

        if (command.ParentMessageId is { } parentGuid)
        {
            ErrorOr<ChatMessageId> parentResult = ChatMessageId.Create(parentGuid);

            if (parentResult.IsError)
                errors.AddRange(parentResult.Errors);
            else
                explicitParentId = parentResult.Value;
        }

        if (errors.Count > 0)
            return errors;

        UserId userId = userIdResult.Value;
        Guid modelId = modelIdResult.Value.Value;

        ChatThread? chat = await chats.GetByIdAsync(chatIdResult.Value, userId, cancellationToken);

        if (chat is null)
            return ChatOperationErrors.ChatNotFound(chatIdResult.Value);

        DateTimeOffset now = dateTimeProvider.UtcNow;
        ChatMessageId effectiveParentId = explicitParentId ?? chat.CurrentMessageId;

        ErrorOr<ChatMessage> userMessageResult = chat.AddUserMessage(effectiveParentId, contentResult.Value, now);

        if (userMessageResult.IsError)
            return userMessageResult.Errors;

        ErrorOr<ChatMessage> assistantResult = chat.BeginAssistantMessage(userMessageResult.Value.Id, modelId, now);

        if (assistantResult.IsError)
            return assistantResult.Errors;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        ChatMessage assistant = assistantResult.Value;

        await generationQueue.EnqueueAsync
        (
            new ChatGenerationJob(chat.Id.Value, assistant.Id.Value, modelId, userId.Value),
            cancellationToken
        );

        return chat.ToGenerationResult(assistant);
    }
}
```

### Step 4.3: EditUserMessage

- [ ] **Step 1: Command**

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.EditUserMessage;

public sealed record EditUserMessageCommand
(
    Guid ChatId,
    Guid MessageId,
    string Message,
    Guid ModelId
) : ICommand<ErrorOr<ChatGenerationResult>>;
```

- [ ] **Step 2: Validator**

```csharp
using Chat.Application.Chats;

using FluentValidation;

namespace Chat.Application.Chats.Commands.EditUserMessage;

internal sealed class EditUserMessageCommandValidator : AbstractValidator<EditUserMessageCommand>
{
    public EditUserMessageCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.MessageId)
            .NotEmpty();

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(ChatLimits.MessageContentMaxLength);

        RuleFor(x => x.ModelId)
            .NotEmpty();
    }
}
```

- [ ] **Step 3: Handler**

```csharp
using Chat.Application.Abstractions.Chats;
using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Chats.Commands.EditUserMessage;

internal sealed class EditUserMessageHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IChatGenerationQueue generationQueue) : ICommandHandler<EditUserMessageCommand, ErrorOr<ChatGenerationResult>>
{
    public async ValueTask<ErrorOr<ChatGenerationResult>> Handle(EditUserMessageCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(command.MessageId);
        ErrorOr<MessageContent> contentResult = MessageContent.Create(command.Message);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.ModelId);

        List<Error> errors = [];

        if (userIdResult.IsError) errors.AddRange(userIdResult.Errors);
        if (chatIdResult.IsError) errors.AddRange(chatIdResult.Errors);
        if (messageIdResult.IsError) errors.AddRange(messageIdResult.Errors);
        if (contentResult.IsError) errors.AddRange(contentResult.Errors);
        if (modelIdResult.IsError) errors.AddRange(modelIdResult.Errors);

        if (errors.Count > 0)
            return errors;

        UserId userId = userIdResult.Value;
        Guid modelId = modelIdResult.Value.Value;

        ChatThread? chat = await chats.GetByIdAsync(chatIdResult.Value, userId, cancellationToken);

        if (chat is null)
            return ChatOperationErrors.ChatNotFound(chatIdResult.Value);

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ErrorOr<ChatMessage> editedResult = chat.EditUserMessage(messageIdResult.Value, contentResult.Value, now);

        if (editedResult.IsError)
            return editedResult.Errors;

        ErrorOr<ChatMessage> assistantResult = chat.BeginAssistantMessage(editedResult.Value.Id, modelId, now);

        if (assistantResult.IsError)
            return assistantResult.Errors;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        ChatMessage assistant = assistantResult.Value;

        await generationQueue.EnqueueAsync
        (
            new ChatGenerationJob(chat.Id.Value, assistant.Id.Value, modelId, userId.Value),
            cancellationToken
        );

        return chat.ToGenerationResult(assistant);
    }
}
```

### Step 4.4: RegenerateAssistantMessage

- [ ] **Step 1: Command**

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.RegenerateAssistantMessage;

public sealed record RegenerateAssistantMessageCommand
(
    Guid ChatId,
    Guid MessageId,
    Guid ModelId
) : ICommand<ErrorOr<ChatGenerationResult>>;
```

- [ ] **Step 2: Validator**

```csharp
using FluentValidation;

namespace Chat.Application.Chats.Commands.RegenerateAssistantMessage;

internal sealed class RegenerateAssistantMessageCommandValidator : AbstractValidator<RegenerateAssistantMessageCommand>
{
    public RegenerateAssistantMessageCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.MessageId)
            .NotEmpty();

        RuleFor(x => x.ModelId)
            .NotEmpty();
    }
}
```

- [ ] **Step 3: Handler**

```csharp
using Chat.Application.Abstractions.Chats;
using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Chats.Commands.RegenerateAssistantMessage;

internal sealed class RegenerateAssistantMessageHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IChatGenerationQueue generationQueue) : ICommandHandler<RegenerateAssistantMessageCommand, ErrorOr<ChatGenerationResult>>
{
    public async ValueTask<ErrorOr<ChatGenerationResult>> Handle(RegenerateAssistantMessageCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(command.MessageId);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.ModelId);

        List<Error> errors = [];

        if (userIdResult.IsError) errors.AddRange(userIdResult.Errors);
        if (chatIdResult.IsError) errors.AddRange(chatIdResult.Errors);
        if (messageIdResult.IsError) errors.AddRange(messageIdResult.Errors);
        if (modelIdResult.IsError) errors.AddRange(modelIdResult.Errors);

        if (errors.Count > 0)
            return errors;

        UserId userId = userIdResult.Value;
        Guid modelId = modelIdResult.Value.Value;

        ChatThread? chat = await chats.GetByIdAsync(chatIdResult.Value, userId, cancellationToken);

        if (chat is null)
            return ChatOperationErrors.ChatNotFound(chatIdResult.Value);

        DateTimeOffset now = dateTimeProvider.UtcNow;

        ErrorOr<ChatMessage> assistantResult = chat.RegenerateAssistant(messageIdResult.Value, modelId, now);

        if (assistantResult.IsError)
            return assistantResult.Errors;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        ChatMessage assistant = assistantResult.Value;

        await generationQueue.EnqueueAsync
        (
            new ChatGenerationJob(chat.Id.Value, assistant.Id.Value, modelId, userId.Value),
            cancellationToken
        );

        return chat.ToGenerationResult(assistant);
    }
}
```

### Step 4.5: SelectChatMessage

- [ ] **Step 1: Command**

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.SelectChatMessage;

public sealed record SelectChatMessageCommand
(
    Guid ChatId,
    Guid MessageId
) : ICommand<ErrorOr<Success>>;
```

- [ ] **Step 2: Handler**

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Chats.Commands.SelectChatMessage;

internal sealed class SelectChatMessageHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : ICommandHandler<SelectChatMessageCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(SelectChatMessageCommand command, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(command.MessageId);

        List<Error> errors = [];

        if (userIdResult.IsError) errors.AddRange(userIdResult.Errors);
        if (chatIdResult.IsError) errors.AddRange(chatIdResult.Errors);
        if (messageIdResult.IsError) errors.AddRange(messageIdResult.Errors);

        if (errors.Count > 0)
            return errors;

        ChatThread? chat = await chats.GetByIdAsync(chatIdResult.Value, userIdResult.Value, cancellationToken);

        if (chat is null)
            return ChatOperationErrors.ChatNotFound(chatIdResult.Value);

        ErrorOr<Success> selectResult = chat.SelectMessage(messageIdResult.Value, dateTimeProvider.UtcNow);

        if (selectResult.IsError)
            return selectResult.Errors;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/services/Chat/Chat.Application/Chat.Application.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Commands
git commit -m "feat(chat): add chat generation commands"
```

---

## Task 5: Queries and Read Models

**Files**
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/GetChatsQuery.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/ChatListItemReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/ChatsReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/IChatsReader.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/GetChatsHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/GetChatQuery.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatPathMessageReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatSiblingGroupReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/ChatSiblingReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/IChatReader.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChat/GetChatHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChatTree/GetChatTreeQuery.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChatTree/ChatTreeReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChatTree/ChatTreeMessageReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChatTree/IChatTreeReader.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChatTree/GetChatTreeHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChatStreamState/GetChatStreamStateQuery.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChatStreamState/ChatStreamStateReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChatStreamState/IChatStreamReader.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChatStreamState/GetChatStreamStateHandler.cs`

### Step 5.1: GetChats

- [ ] **Step 1: Query + read models**

`GetChatsQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.GetChats;

public sealed record GetChatsQuery : IQuery<ErrorOr<ChatsReadModel>>;
```

`ChatListItemReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChats;

public sealed record ChatListItemReadModel
(
    Guid Id,
    string Title,
    Guid CurrentMessageId,
    DateTimeOffset UpdatedAt,
    string? Preview
);
```

`ChatsReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChats;

public sealed record ChatsReadModel
(
    IReadOnlyCollection<ChatListItemReadModel> Chats
);
```

`IChatsReader.cs`:

```csharp
using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.GetChats;

public interface IChatsReader
{
    Task<ChatsReadModel> GetAsync(UserId userId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Handler**

```csharp
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.GetChats;

internal sealed class GetChatsHandler(IUserContext userContext, IChatsReader reader)
    : IQueryHandler<GetChatsQuery, ErrorOr<ChatsReadModel>>
{
    public async ValueTask<ErrorOr<ChatsReadModel>> Handle(GetChatsQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
            return userIdResult.Errors;

        return await reader.GetAsync(userIdResult.Value, cancellationToken);
    }
}
```

### Step 5.2: GetChat (active path + sibling groups)

- [ ] **Step 1: Query + read models**

`GetChatQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.GetChat;

public sealed record GetChatQuery(Guid ChatId) : IQuery<ErrorOr<ChatReadModel>>;
```

`ChatPathMessageReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatPathMessageReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    string Role,
    string? Content,
    Guid? ModelId,
    string Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int SiblingIndex
);
```

`ChatSiblingReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatSiblingReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    string Role,
    string? Preview,
    DateTimeOffset CreatedAt,
    int SiblingIndex,
    bool IsSelected
);
```

`ChatSiblingGroupReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatSiblingGroupReadModel
(
    Guid? ParentMessageId,
    Guid SelectedMessageId,
    int SelectedSiblingIndex,
    int SiblingCount,
    IReadOnlyCollection<ChatSiblingReadModel> Siblings
);
```

`ChatReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChat;

public sealed record ChatReadModel
(
    Guid Id,
    string Title,
    Guid CurrentMessageId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<ChatPathMessageReadModel> Messages,
    IReadOnlyCollection<ChatSiblingGroupReadModel> SiblingGroups
);
```

`IChatReader.cs`:

```csharp
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Chats.Queries.GetChat;

public interface IChatReader
{
    Task<ErrorOr<ChatReadModel>> GetAsync(UserId userId, ChatId chatId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Handler**

```csharp
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.GetChat;

internal sealed class GetChatHandler(IUserContext userContext, IChatReader reader)
    : IQueryHandler<GetChatQuery, ErrorOr<ChatReadModel>>
{
    public async ValueTask<ErrorOr<ChatReadModel>> Handle(GetChatQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(query.ChatId);

        List<Error> errors = [];

        if (userIdResult.IsError) errors.AddRange(userIdResult.Errors);
        if (chatIdResult.IsError) errors.AddRange(chatIdResult.Errors);

        if (errors.Count > 0)
            return errors;

        return await reader.GetAsync(userIdResult.Value, chatIdResult.Value, cancellationToken);
    }
}
```

### Step 5.3: GetChatTree

- [ ] **Step 1: Query + read models**

`GetChatTreeQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.GetChatTree;

public sealed record GetChatTreeQuery(Guid ChatId) : IQuery<ErrorOr<ChatTreeReadModel>>;
```

`ChatTreeMessageReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChatTree;

public sealed record ChatTreeMessageReadModel
(
    Guid Id,
    Guid? ParentMessageId,
    string Role,
    string? Content,
    Guid? ModelId,
    string Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int SiblingIndex
);
```

`ChatTreeReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChatTree;

public sealed record ChatTreeReadModel
(
    Guid Id,
    string Title,
    Guid CurrentMessageId,
    IReadOnlyCollection<ChatTreeMessageReadModel> Messages
);
```

`IChatTreeReader.cs`:

```csharp
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Chats.Queries.GetChatTree;

public interface IChatTreeReader
{
    Task<ErrorOr<ChatTreeReadModel>> GetAsync(UserId userId, ChatId chatId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Handler**

```csharp
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.GetChatTree;

internal sealed class GetChatTreeHandler(IUserContext userContext, IChatTreeReader reader)
    : IQueryHandler<GetChatTreeQuery, ErrorOr<ChatTreeReadModel>>
{
    public async ValueTask<ErrorOr<ChatTreeReadModel>> Handle(GetChatTreeQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(query.ChatId);

        List<Error> errors = [];

        if (userIdResult.IsError) errors.AddRange(userIdResult.Errors);
        if (chatIdResult.IsError) errors.AddRange(chatIdResult.Errors);

        if (errors.Count > 0)
            return errors;

        return await reader.GetAsync(userIdResult.Value, chatIdResult.Value, cancellationToken);
    }
}
```

### Step 5.4: GetChatStreamState (ownership + status check for the SSE endpoint)

- [ ] **Step 1: Query + read model**

`GetChatStreamStateQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.GetChatStreamState;

public sealed record GetChatStreamStateQuery(Guid ChatId, Guid MessageId)
    : IQuery<ErrorOr<ChatStreamStateReadModel>>;
```

`ChatStreamStateReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChatStreamState;

public sealed record ChatStreamStateReadModel
(
    string Status,
    string? Content,
    string? FailureReason
);
```

`IChatStreamReader.cs`:

```csharp
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.GetChatStreamState;

public interface IChatStreamReader
{
    Task<ChatStreamStateReadModel?> GetStateAsync
    (
        UserId userId,
        ChatId chatId,
        ChatMessageId messageId,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 2: Handler**

```csharp
using Chat.Application.Chats.Errors;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.GetChatStreamState;

internal sealed class GetChatStreamStateHandler(IUserContext userContext, IChatStreamReader reader)
    : IQueryHandler<GetChatStreamStateQuery, ErrorOr<ChatStreamStateReadModel>>
{
    public async ValueTask<ErrorOr<ChatStreamStateReadModel>> Handle(GetChatStreamStateQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(query.ChatId);
        ErrorOr<ChatMessageId> messageIdResult = ChatMessageId.Create(query.MessageId);

        List<Error> errors = [];

        if (userIdResult.IsError) errors.AddRange(userIdResult.Errors);
        if (chatIdResult.IsError) errors.AddRange(chatIdResult.Errors);
        if (messageIdResult.IsError) errors.AddRange(messageIdResult.Errors);

        if (errors.Count > 0)
            return errors;

        ChatStreamStateReadModel? state = await reader.GetStateAsync
        (
            userIdResult.Value,
            chatIdResult.Value,
            messageIdResult.Value,
            cancellationToken
        );

        if (state is null)
            return ChatErrors.MessageNotFound(messageIdResult.Value);

        return state;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/services/Chat/Chat.Application/Chat.Application.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Queries
git commit -m "feat(chat): add chat queries and read models"
```

---

## Task 6: Persistence Mapping, Repository, DbContext, DI

**Files**
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatThreadConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatMessageConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add the `DbSet` to `ChatDbContext`**

Add this property next to the existing `DbSet` declarations in `ChatDbContext`:

```csharp
public DbSet<ChatThread> Chats => Set<ChatThread>();
```

And add the using at the top:

```csharp
using Chat.Domain.Chats;
```

- [ ] **Step 2: Create `ChatThreadConfiguration`**

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Chats.Configurations;

internal sealed class ChatThreadConfiguration : IEntityTypeConfiguration<ChatThread>
{
    public void Configure(EntityTypeBuilder<ChatThread> builder)
    {
        builder.ToTable("chats");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => ChatId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.UserId)
            .HasConversion
            (
                id => id.Value,
                value => UserId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.Title)
            .HasConversion
            (
                title => title.Value,
                value => ChatTitle.FromDatabase(value)
            )
            .HasMaxLength(ChatTitle.MaxLength)
            .IsRequired();

        builder.Property(x => x.CurrentMessageId)
            .HasConversion
            (
                id => id.Value,
                value => ChatMessageId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .IsRequired();

        builder.HasMany(x => x.Messages)
            .WithOne()
            .HasForeignKey(message => message.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => new { x.UserId, x.UpdatedAt, x.Id })
            .IsDescending(false, true, false);

        builder.HasIndex(x => new { x.UserId, x.Id });

        builder.Ignore(x => x.DomainEvents);
    }
}
```

- [ ] **Step 3: Create `ChatMessageConfiguration`**

```csharp
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.Chats.Configurations;

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => ChatMessageId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.ChatId)
            .HasConversion
            (
                id => id.Value,
                value => ChatId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.ParentMessageId)
            .HasConversion
            (
                id => id!.Value,
                value => ChatMessageId.FromDatabase(value)
            );

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Content)
            .HasConversion
            (
                content => content!.Value,
                value => MessageContent.FromDatabase(value)
            )
            .HasMaxLength(MessageContent.MaxLength);

        builder.Property(x => x.ModelId);

        builder.Property(x => x.FailureReason)
            .HasMaxLength(ChatMessage.FailureReasonMaxLength);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.CompletedAt);

        builder.Property(x => x.SiblingIndex)
            .IsRequired();

        builder.HasIndex(x => new { x.ChatId, x.ParentMessageId, x.SiblingIndex, x.Id });

        builder.HasIndex(x => new { x.ChatId, x.Id });

        builder.HasIndex(x => x.Status);

        builder.HasOne<ChatMessage>()
            .WithMany()
            .HasForeignKey(x => x.ParentMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
```

- [ ] **Step 4: Create `ChatRepository`**

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.Chats.Repositories;

internal sealed class ChatRepository(ChatDbContext db) : IChatRepository
{
    public async Task<ChatThread?> GetByIdAsync
    (
        ChatId id,
        UserId userId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.Chats
            .Include(chat => chat.Messages)
            .FirstOrDefaultAsync
            (
                chat => chat.Id == id && chat.UserId == userId,
                cancellationToken
            );
    }

    public void Add(ChatThread chat)
    {
        db.Chats.Add(chat);
    }
}
```

- [ ] **Step 5: Register the repository**

In `Chat.Infrastructure.DependencyInjection`, inside `AddDatabaseServices`, add after the `IFavoriteModelRepository` registration:

```csharp
services.AddScoped<IChatRepository, ChatRepository>();
```

Add the usings at the top of `DependencyInjection.cs`:

```csharp
using Chat.Domain.Chats;
using Chat.Infrastructure.Chats.Repositories;
```

- [ ] **Step 6: Build**

Run: `dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Chats/Configurations src/services/Chat/Chat.Infrastructure/Chats/Repositories src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): add chat EF mappings and repository"
```

---

## Task 7: Dapper Readers

The active-path and tree readers use a recursive CTE over `parent_message_id`. `content` may be null for in-flight assistant messages; previews coalesce to an empty string.

**Files**
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatsReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatTreeReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatStreamReader.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Create `ChatsReader`**

```csharp
using Chat.Application.Chats.Queries.GetChats;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatsReader(NpgsqlDataSource dataSource) : IChatsReader
{
    private const string Sql = """
                               select
                                   c.id as "Id",
                                   c.title as "Title",
                                   c.current_message_id as "CurrentMessageId",
                                   c.updated_at as "UpdatedAt",
                                   m.content as "Preview"
                               from chats c
                               left join chat_messages m
                                   on m.id = c.current_message_id
                                  and m.chat_id = c.id
                               where c.user_id = @UserId
                               order by c.updated_at desc, c.id;
                               """;

    public async Task<ChatsReadModel> GetAsync(UserId userId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            Sql,
            new { UserId = userId.Value },
            cancellationToken: cancellationToken
        );

        ChatListItemReadModel[] chats = (await connection.QueryAsync<ChatListItemReadModel>(command)).ToArray();

        return new ChatsReadModel(chats);
    }
}
```

- [ ] **Step 2: Create `ChatReader`**

```csharp
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Queries.GetChat;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using Dapper;

using ErrorOr;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatReader(NpgsqlDataSource dataSource) : IChatReader
{
    private const string MetadataSql = """
                                       select
                                           c.id as "Id",
                                           c.title as "Title",
                                           c.current_message_id as "CurrentMessageId",
                                           c.created_at as "CreatedAt",
                                           c.updated_at as "UpdatedAt"
                                       from chats c
                                       where c.id = @ChatId and c.user_id = @UserId;
                                       """;

    private const string ActivePathSql = """
                                          with recursive active_path as (
                                              select n.*, 0 as depth
                                              from chat_messages n
                                              join chats c on c.current_message_id = n.id
                                              where c.id = @ChatId and c.user_id = @UserId

                                              union all

                                              select parent.*, active_path.depth + 1
                                              from chat_messages parent
                                              join active_path on active_path.parent_message_id = parent.id
                                          )
                                          select
                                              id as "Id",
                                              parent_message_id as "ParentMessageId",
                                              role as "Role",
                                              content as "Content",
                                              model_id as "ModelId",
                                              status as "Status",
                                              failure_reason as "FailureReason",
                                              created_at as "CreatedAt",
                                              completed_at as "CompletedAt",
                                              sibling_index as "SiblingIndex"
                                          from active_path
                                          order by depth desc;
                                          """;

    private const string SiblingsSql = """
                                       with recursive active_path as (
                                           select n.*, 0 as depth
                                           from chat_messages n
                                           join chats c on c.current_message_id = n.id
                                           where c.id = @ChatId and c.user_id = @UserId

                                           union all

                                           select parent.*, active_path.depth + 1
                                           from chat_messages parent
                                           join active_path on active_path.parent_message_id = parent.id
                                       ),
                                       selected_groups as (
                                           select
                                               parent_message_id,
                                               id as selected_message_id,
                                               sibling_index as selected_sibling_index
                                           from active_path
                                       )
                                       select
                                           siblings.parent_message_id as "ParentMessageId",
                                           selected_groups.selected_message_id as "SelectedMessageId",
                                           selected_groups.selected_sibling_index as "SelectedSiblingIndex",
                                           count(*) over (partition by siblings.parent_message_id) as "SiblingCount",
                                           siblings.id as "Id",
                                           siblings.role as "Role",
                                           left(coalesce(siblings.content, ''), 160) as "Preview",
                                           siblings.created_at as "CreatedAt",
                                           siblings.sibling_index as "SiblingIndex",
                                           siblings.id = selected_groups.selected_message_id as "IsSelected"
                                       from selected_groups
                                       join chat_messages siblings
                                           on siblings.chat_id = @ChatId
                                          and siblings.parent_message_id is not distinct from selected_groups.parent_message_id
                                       order by siblings.parent_message_id nulls first, siblings.sibling_index, siblings.id;
                                       """;

    public async Task<ErrorOr<ChatReadModel>> GetAsync(UserId userId, ChatId chatId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        object parameters = new { ChatId = chatId.Value, UserId = userId.Value };

        MetadataRow? metadata = await connection.QuerySingleOrDefaultAsync<MetadataRow>
        (
            new CommandDefinition(MetadataSql, parameters, cancellationToken: cancellationToken)
        );

        if (metadata is null)
            return ChatOperationErrors.ChatNotFound(chatId);

        ChatPathMessageReadModel[] messages =
        (
            await connection.QueryAsync<ChatPathMessageReadModel>
            (
                new CommandDefinition(ActivePathSql, parameters, cancellationToken: cancellationToken)
            )
        ).ToArray();

        SiblingRow[] siblingRows =
        (
            await connection.QueryAsync<SiblingRow>
            (
                new CommandDefinition(SiblingsSql, parameters, cancellationToken: cancellationToken)
            )
        ).ToArray();

        ChatSiblingGroupReadModel[] groups = siblingRows
            .GroupBy(row => row.ParentMessageId)
            .Select(group =>
            {
                SiblingRow first = group.First();

                return new ChatSiblingGroupReadModel
                (
                    ParentMessageId: first.ParentMessageId,
                    SelectedMessageId: first.SelectedMessageId,
                    SelectedSiblingIndex: first.SelectedSiblingIndex,
                    SiblingCount: first.SiblingCount,
                    Siblings: group
                        .Select(row => new ChatSiblingReadModel
                        (
                            Id: row.Id,
                            ParentMessageId: row.ParentMessageId,
                            Role: row.Role,
                            Preview: row.Preview,
                            CreatedAt: row.CreatedAt,
                            SiblingIndex: row.SiblingIndex,
                            IsSelected: row.IsSelected
                        ))
                        .ToArray()
                );
            })
            .ToArray();

        return new ChatReadModel
        (
            Id: metadata.Id,
            Title: metadata.Title,
            CurrentMessageId: metadata.CurrentMessageId,
            CreatedAt: metadata.CreatedAt,
            UpdatedAt: metadata.UpdatedAt,
            Messages: messages,
            SiblingGroups: groups
        );
    }

    private sealed record MetadataRow
    (
        Guid Id,
        string Title,
        Guid CurrentMessageId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt
    );

    private sealed record SiblingRow
    (
        Guid? ParentMessageId,
        Guid SelectedMessageId,
        int SelectedSiblingIndex,
        int SiblingCount,
        Guid Id,
        string Role,
        string? Preview,
        DateTimeOffset CreatedAt,
        int SiblingIndex,
        bool IsSelected
    );
}
```

- [ ] **Step 3: Create `ChatTreeReader`**

```csharp
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Queries.GetChatTree;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using Dapper;

using ErrorOr;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatTreeReader(NpgsqlDataSource dataSource) : IChatTreeReader
{
    private const string MetadataSql = """
                                       select
                                           c.id as "Id",
                                           c.title as "Title",
                                           c.current_message_id as "CurrentMessageId"
                                       from chats c
                                       where c.id = @ChatId and c.user_id = @UserId;
                                       """;

    private const string MessagesSql = """
                                       select
                                           n.id as "Id",
                                           n.parent_message_id as "ParentMessageId",
                                           n.role as "Role",
                                           n.content as "Content",
                                           n.model_id as "ModelId",
                                           n.status as "Status",
                                           n.failure_reason as "FailureReason",
                                           n.created_at as "CreatedAt",
                                           n.completed_at as "CompletedAt",
                                           n.sibling_index as "SiblingIndex"
                                       from chat_messages n
                                       join chats c on c.id = n.chat_id
                                       where c.id = @ChatId and c.user_id = @UserId
                                       order by n.parent_message_id nulls first, n.sibling_index, n.created_at, n.id;
                                       """;

    public async Task<ErrorOr<ChatTreeReadModel>> GetAsync(UserId userId, ChatId chatId, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        object parameters = new { ChatId = chatId.Value, UserId = userId.Value };

        MetadataRow? metadata = await connection.QuerySingleOrDefaultAsync<MetadataRow>
        (
            new CommandDefinition(MetadataSql, parameters, cancellationToken: cancellationToken)
        );

        if (metadata is null)
            return ChatOperationErrors.ChatNotFound(chatId);

        ChatTreeMessageReadModel[] messages =
        (
            await connection.QueryAsync<ChatTreeMessageReadModel>
            (
                new CommandDefinition(MessagesSql, parameters, cancellationToken: cancellationToken)
            )
        ).ToArray();

        return new ChatTreeReadModel
        (
            Id: metadata.Id,
            Title: metadata.Title,
            CurrentMessageId: metadata.CurrentMessageId,
            Messages: messages
        );
    }

    private sealed record MetadataRow
    (
        Guid Id,
        string Title,
        Guid CurrentMessageId
    );
}
```

- [ ] **Step 4: Create `ChatStreamReader`**

```csharp
using Chat.Application.Chats.Queries.GetChatStreamState;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatStreamReader(NpgsqlDataSource dataSource) : IChatStreamReader
{
    private const string Sql = """
                               select
                                   n.status as "Status",
                                   n.content as "Content",
                                   n.failure_reason as "FailureReason"
                               from chat_messages n
                               join chats c on c.id = n.chat_id
                               where c.id = @ChatId and c.user_id = @UserId and n.id = @MessageId;
                               """;

    public async Task<ChatStreamStateReadModel?> GetStateAsync
    (
        UserId userId,
        ChatId chatId,
        ChatMessageId messageId,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            Sql,
            new { ChatId = chatId.Value, UserId = userId.Value, MessageId = messageId.Value },
            cancellationToken: cancellationToken
        );

        return await connection.QuerySingleOrDefaultAsync<ChatStreamStateReadModel>(command);
    }
}
```

- [ ] **Step 5: Register the readers**

In `Chat.Infrastructure.DependencyInjection`, inside `AddReaders`, add:

```csharp
services.AddScoped<IChatsReader, ChatsReader>();
services.AddScoped<IChatReader, ChatReader>();
services.AddScoped<IChatTreeReader, ChatTreeReader>();
services.AddScoped<IChatStreamReader, ChatStreamReader>();
```

Add the usings at the top of `DependencyInjection.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChat;
using Chat.Application.Chats.Queries.GetChats;
using Chat.Application.Chats.Queries.GetChatStreamState;
using Chat.Application.Chats.Queries.GetChatTree;
using Chat.Infrastructure.Chats.Readers;
```

- [ ] **Step 6: Build**

Run: `dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Chats/Readers src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): add chat dapper readers"
```

---

## Task 8: Redis Streaming Infrastructure

This task adds the four infrastructure pieces behind the application abstractions: the Redis Stream store, the stub completion client, the in-process channel queue, and the background worker that runs generations. It also adds a sweeper that fails messages stuck in `Generating`.

**Files**
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Streaming/RedisChatStreamStore.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Streaming/StubChatCompletionClient.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Streaming/ChannelChatGenerationQueue.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Streaming/ChatGenerationProcessor.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Streaming/ChatGenerationWorker.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Streaming/StaleGenerationSweeper.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Modify: `src/services/Chat/Chat.Api/Chat.Api.csproj`
- Modify: `src/services/Chat/Chat.Api/Program.cs`

- [ ] **Step 1: Create `RedisChatStreamStore`**

Each generation owns the Redis Stream key `chat:stream:{messageId}`. Deltas are appended with `XADD`; readers replay with `XRANGE` from the last seen entry id and stop on a terminal `done`/`error` entry. A 30-minute TTL covers in-flight generations; terminal entries shorten it so late reconnects still observe completion.

```csharp
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.Chats;
using Chat.Domain.Chats.ValueObjects;

using StackExchange.Redis;

namespace Chat.Infrastructure.Chats.Streaming;

internal sealed class RedisChatStreamStore(IConnectionMultiplexer connection) : IChatStreamStore
{
    private static readonly TimeSpan ActiveTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TerminalTtl = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private const int MaxIdlePolls = 150; // ~30s with no new entries closes the stream read

    private const string TypeField = "type";
    private const string DataField = "data";

    private static string KeyFor(ChatMessageId messageId) => $"chat:stream:{messageId.Value}";

    public async Task AppendDeltaAsync(ChatMessageId messageId, string delta, CancellationToken cancellationToken = default)
    {
        IDatabase db = connection.GetDatabase();
        RedisKey key = KeyFor(messageId);

        await db.StreamAddAsync
        (
            key,
            [
                new NameValueEntry(TypeField, ChatStreamEventTypes.Delta),
                new NameValueEntry(DataField, delta)
            ]
        );

        await db.KeyExpireAsync(key, ActiveTtl);
    }

    public async Task CompleteAsync(ChatMessageId messageId, CancellationToken cancellationToken = default)
    {
        await AppendTerminalAsync(messageId, ChatStreamEventTypes.Done, string.Empty);
    }

    public async Task FailAsync(ChatMessageId messageId, string reason, CancellationToken cancellationToken = default)
    {
        await AppendTerminalAsync(messageId, ChatStreamEventTypes.Error, reason);
    }

    private async Task AppendTerminalAsync(ChatMessageId messageId, string type, string data)
    {
        IDatabase db = connection.GetDatabase();
        RedisKey key = KeyFor(messageId);

        await db.StreamAddAsync
        (
            key,
            [
                new NameValueEntry(TypeField, type),
                new NameValueEntry(DataField, data)
            ]
        );

        await db.KeyExpireAsync(key, TerminalTtl);
    }

    public async IAsyncEnumerable<ChatStreamEvent> ReadAsync
    (
        ChatMessageId messageId,
        string? lastEventId,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        IDatabase db = connection.GetDatabase();
        RedisKey key = KeyFor(messageId);

        string last = string.IsNullOrWhiteSpace(lastEventId) ? "0-0" : lastEventId;
        int idlePolls = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            StreamEntry[] entries = await db.StreamRangeAsync(key, last, "+", count: 100);

            bool advanced = false;

            foreach (StreamEntry entry in entries)
            {
                string id = entry.Id.ToString();

                if (id == last)
                    continue; // XRANGE min is inclusive; skip the already-seen entry

                advanced = true;
                last = id;

                string type = entry[TypeField].ToString();
                string? data = entry[DataField].IsNull ? null : entry[DataField].ToString();

                yield return new ChatStreamEvent(id, type, data);

                if (type is ChatStreamEventTypes.Done or ChatStreamEventTypes.Error)
                    yield break;
            }

            if (advanced)
            {
                idlePolls = 0;
                continue;
            }

            if (++idlePolls > MaxIdlePolls)
                yield break;

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
```

- [ ] **Step 2: Create `StubChatCompletionClient`**

Deterministic placeholder so the pipeline streams visibly. Swap this single class for a real provider client later; nothing else changes.

```csharp
using System.Runtime.CompilerServices;

using Chat.Application.Abstractions.Chats;
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Infrastructure.Chats.Streaming;

internal sealed class StubChatCompletionClient : IChatCompletionClient
{
    private static readonly TimeSpan TokenDelay = TimeSpan.FromMilliseconds(40);

    public async IAsyncEnumerable<string> StreamAsync
    (
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        string lastUserMessage = request.Messages
            .LastOrDefault(message => message.Role == nameof(MessageRole.User))?
            .Content ?? string.Empty;

        string reply = $"You said: \"{lastUserMessage}\". This is a streamed stub response from the chat service.";

        foreach (string word in reply.Split(' '))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TokenDelay, cancellationToken);
            yield return word + " ";
        }
    }
}
```

- [ ] **Step 3: Create `ChannelChatGenerationQueue`**

```csharp
using System.Threading.Channels;

using Chat.Application.Abstractions.Chats;

namespace Chat.Infrastructure.Chats.Streaming;

internal sealed class ChannelChatGenerationQueue : IChatGenerationQueue
{
    private readonly Channel<ChatGenerationJob> _channel =
        Channel.CreateUnbounded<ChatGenerationJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public ValueTask EnqueueAsync(ChatGenerationJob job, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(job, cancellationToken);

    public IAsyncEnumerable<ChatGenerationJob> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
```

- [ ] **Step 4: Create `ChatGenerationProcessor`**

Runs one job: loads the chat, builds the prompt history by walking the parent chain from the assistant message, streams deltas to Redis, then writes the durable result back to Postgres (`Complete` on success, `Fail` on exception).

```csharp
using System.Text;

using Chat.Application.Abstractions.Chats;
using Chat.Application.Abstractions.Database;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Microsoft.Extensions.Logging;

using SharedKernel;

namespace Chat.Infrastructure.Chats.Streaming;

internal sealed class ChatGenerationProcessor(
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IChatStreamStore streamStore,
    IChatCompletionClient completionClient,
    IDateTimeProvider dateTimeProvider,
    ILogger<ChatGenerationProcessor> logger)
{
    public async Task ProcessAsync(ChatGenerationJob job, CancellationToken cancellationToken)
    {
        ChatId chatId = ChatId.FromDatabase(job.ChatId);
        ChatMessageId assistantMessageId = ChatMessageId.FromDatabase(job.AssistantMessageId);
        UserId userId = UserId.FromDatabase(job.UserId);

        ChatThread? chat = await chats.GetByIdAsync(chatId, userId, cancellationToken);

        if (chat is null)
        {
            logger.LogWarning("Generation job skipped: chat {ChatId} not found.", job.ChatId);
            return;
        }

        ChatMessage? assistant = chat.FindMessage(assistantMessageId);

        if (assistant is null || assistant.Status != MessageStatus.Generating)
        {
            logger.LogWarning("Generation job skipped: assistant message {MessageId} is not generating.", job.AssistantMessageId);
            return;
        }

        ChatCompletionRequest request = new(job.ModelId, BuildHistory(chat, assistant));

        StringBuilder builder = new();

        try
        {
            await foreach (string delta in completionClient.StreamAsync(request, cancellationToken))
            {
                builder.Append(delta);
                await streamStore.AppendDeltaAsync(assistantMessageId, delta, cancellationToken);
            }

            ErrorOr<MessageContent> contentResult = MessageContent.Create(builder.ToString());

            if (contentResult.IsError)
            {
                await FailAsync(chat, assistantMessageId, "Model produced empty output.", cancellationToken);
                return;
            }

            ErrorOr<ChatMessage> completeResult = chat.CompleteAssistantMessage
            (
                assistantMessageId,
                contentResult.Value,
                dateTimeProvider.UtcNow
            );

            if (completeResult.IsError)
            {
                logger.LogError("Failed to complete message {MessageId}: {Error}.", job.AssistantMessageId, completeResult.Errors[0].Description);
                return;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await streamStore.CompleteAsync(assistantMessageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Generation failed for message {MessageId}.", job.AssistantMessageId);
            await FailAsync(chat, assistantMessageId, exception.Message, cancellationToken);
        }
    }

    private async Task FailAsync(ChatThread chat, ChatMessageId messageId, string reason, CancellationToken cancellationToken)
    {
        ErrorOr<ChatMessage> failResult = chat.FailAssistantMessage(messageId, reason, dateTimeProvider.UtcNow);

        if (!failResult.IsError)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        await streamStore.FailAsync(messageId, reason, cancellationToken);
    }

    private static IReadOnlyList<ChatCompletionMessage> BuildHistory(ChatThread chat, ChatMessage assistant)
    {
        Dictionary<ChatMessageId, ChatMessage> byId = chat.Messages.ToDictionary(message => message.Id);

        List<ChatCompletionMessage> history = [];
        ChatMessageId? cursor = assistant.ParentMessageId;

        while (cursor is not null && byId.TryGetValue(cursor, out ChatMessage? message))
        {
            if (message.Content is not null)
            {
                history.Add(new ChatCompletionMessage(message.Role.ToString(), message.Content.Value));
            }

            cursor = message.ParentMessageId;
        }

        history.Reverse();

        return history;
    }
}
```

- [ ] **Step 5: Create `ChatGenerationWorker`**

```csharp
using Chat.Application.Abstractions.Chats;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.Chats.Streaming;

internal sealed class ChatGenerationWorker(
    IChatGenerationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ChatGenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (ChatGenerationJob job in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

                ChatGenerationProcessor processor =
                    scope.ServiceProvider.GetRequiredService<ChatGenerationProcessor>();

                await processor.ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unhandled error processing generation job for message {MessageId}.", job.AssistantMessageId);
            }
        }
    }
}
```

- [ ] **Step 6: Create `StaleGenerationSweeper`**

Reconciles orphaned `Generating` messages (e.g. process crash mid-generation) by failing any that have been generating longer than the timeout.

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SharedKernel;

namespace Chat.Infrastructure.Chats.Streaming;

internal sealed class StaleGenerationSweeper(
    IServiceScopeFactory scopeFactory,
    ILogger<StaleGenerationSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Stale generation sweep failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        ChatDbContext db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
        IDateTimeProvider clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        DateTimeOffset cutoff = clock.UtcNow - StaleAfter;

        List<ChatId> staleChatIds = await db.Set<ChatMessage>()
            .Where(message => message.Status == MessageStatus.Generating && message.CreatedAt < cutoff)
            .Select(message => message.ChatId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (staleChatIds.Count == 0)
            return;

        foreach (ChatId chatId in staleChatIds)
        {
            ChatThread? chat = await db.Chats
                .Include(c => c.Messages)
                .FirstOrDefaultAsync(c => c.Id == chatId, cancellationToken);

            if (chat is null)
                continue;

            List<ChatMessage> stale = chat.Messages
                .Where(message => message.Status == MessageStatus.Generating && message.CreatedAt < cutoff)
                .ToList();

            foreach (ChatMessage message in stale)
            {
                chat.FailAssistantMessage(message.Id, "Generation timed out.", clock.UtcNow);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Stale generation sweep failed {Count} chat(s) worth of stuck messages.", staleChatIds.Count);
    }
}
```

- [ ] **Step 7: Register streaming services**

In `Chat.Infrastructure.DependencyInjection`, add a new private method and call it from `AddInfrastructure`:

Update the `AddInfrastructure` chain to add `.AddChatStreaming()`:

```csharp
public static IServiceCollection
    AddInfrastructure(this IServiceCollection services, IConfiguration configuration) =>
    services
        .AddSharedInfrastructure()
        .AddAuth0JwtAuthentication(configuration)
        .AddDatabaseServices()
        .AddCacheServices(configuration)
        .AddReaders()
        .AddChatStreaming()
        .AddMessagingServices(configuration);
```

Add the new method:

```csharp
private static IServiceCollection AddChatStreaming(this IServiceCollection services)
{
    services.AddSingleton<IChatGenerationQueue, ChannelChatGenerationQueue>();
    services.AddSingleton<IChatStreamStore, RedisChatStreamStore>();
    services.AddSingleton<IChatCompletionClient, StubChatCompletionClient>();

    services.AddScoped<ChatGenerationProcessor>();

    services.AddHostedService<ChatGenerationWorker>();
    services.AddHostedService<StaleGenerationSweeper>();

    return services;
}
```

Add the usings at the top of `DependencyInjection.cs`:

```csharp
using Chat.Application.Abstractions.Chats;
using Chat.Infrastructure.Chats.Streaming;
```

- [ ] **Step 8: Add the Redis client package to `Chat.Api.csproj`**

Add to the first `<ItemGroup>` of package references in `src/services/Chat/Chat.Api/Chat.Api.csproj`:

```xml
<PackageReference Include="Aspire.StackExchange.Redis" />
```

(The version is already pinned in `Directory.Packages.props`.)

- [ ] **Step 9: Register `IConnectionMultiplexer` in `Program.cs`**

In `src/services/Chat/Chat.Api/Program.cs`, add right after `builder.AddRedisDistributedCache("redis");`:

```csharp
builder.AddRedisClient("redis");
```

This registers `IConnectionMultiplexer` from the same `redis` connection string that the SSE stream store consumes.

- [ ] **Step 10: Build**

Run: `dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj`
Expected: build succeeds.

- [ ] **Step 11: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Chats/Streaming src/services/Chat/Chat.Infrastructure/DependencyInjection.cs src/services/Chat/Chat.Api/Chat.Api.csproj src/services/Chat/Chat.Api/Program.cs
git commit -m "feat(chat): add redis streaming generation pipeline"
```

---

## Task 9: FastEndpoints (CRUD + SSE)

**Files**
- Modify: `src/services/Chat/Chat.Api/Endpoints/CustomTags.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatGenerationResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatListResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatListItemResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatPathMessageResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatSiblingGroupResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatSiblingResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatTreeResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatTreeMessageResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatResponseMapper.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChats/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChat/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChatTree/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SendMessage/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SendMessage/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/EditUserMessage/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/EditUserMessage/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/RegenerateAssistantMessage/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/RegenerateAssistantMessage/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SelectChatMessage/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/StreamChatMessage/Endpoint.cs`

- [ ] **Step 1: Add the tag**

Add to `CustomTags`:

```csharp
public const string Chats = "Chats";
```

- [ ] **Step 2: Response DTOs**

`ChatGenerationResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed class ChatGenerationResponse
{
    public required Guid ChatId { get; init; }
    public required Guid AssistantMessageId { get; init; }
    public required Guid? ParentMessageId { get; init; }
    public required Guid? ModelId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
```

`ChatListItemResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed class ChatListItemResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required Guid CurrentMessageId { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string? Preview { get; init; }
}
```

`ChatListResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed class ChatListResponse
{
    public required IReadOnlyCollection<ChatListItemResponse> Chats { get; init; }
}
```

`ChatPathMessageResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed class ChatPathMessageResponse
{
    public required Guid Id { get; init; }
    public required Guid? ParentMessageId { get; init; }
    public required string Role { get; init; }
    public required string? Content { get; init; }
    public required Guid? ModelId { get; init; }
    public required string Status { get; init; }
    public required string? FailureReason { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset? CompletedAt { get; init; }
    public required int SiblingIndex { get; init; }
}
```

`ChatSiblingResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed class ChatSiblingResponse
{
    public required Guid Id { get; init; }
    public required Guid? ParentMessageId { get; init; }
    public required string Role { get; init; }
    public required string? Preview { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required int SiblingIndex { get; init; }
    public required bool IsSelected { get; init; }
}
```

`ChatSiblingGroupResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed class ChatSiblingGroupResponse
{
    public required Guid? ParentMessageId { get; init; }
    public required Guid SelectedMessageId { get; init; }
    public required int SelectedSiblingIndex { get; init; }
    public required int SiblingCount { get; init; }
    public required IReadOnlyCollection<ChatSiblingResponse> Siblings { get; init; }
}
```

`ChatResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed class ChatResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required Guid CurrentMessageId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required IReadOnlyCollection<ChatPathMessageResponse> Messages { get; init; }
    public required IReadOnlyCollection<ChatSiblingGroupResponse> SiblingGroups { get; init; }
}
```

`ChatTreeMessageResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed class ChatTreeMessageResponse
{
    public required Guid Id { get; init; }
    public required Guid? ParentMessageId { get; init; }
    public required string Role { get; init; }
    public required string? Content { get; init; }
    public required Guid? ModelId { get; init; }
    public required string Status { get; init; }
    public required string? FailureReason { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset? CompletedAt { get; init; }
    public required int SiblingIndex { get; init; }
}
```

`ChatTreeResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed class ChatTreeResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required Guid CurrentMessageId { get; init; }
    public required IReadOnlyCollection<ChatTreeMessageResponse> Messages { get; init; }
}
```

- [ ] **Step 3: Response mapper**

`ChatResponseMapper.cs`:

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Queries.GetChat;
using Chat.Application.Chats.Queries.GetChats;
using Chat.Application.Chats.Queries.GetChatTree;
using Chat.Application.Chats.Results;

namespace Chat.Api.Endpoints.Chats.Responses;

internal static class ChatResponseMapper
{
    public static ChatGenerationResponse ToResponse(this ChatGenerationResult result) => new()
    {
        ChatId = result.ChatId,
        AssistantMessageId = result.AssistantMessageId,
        ParentMessageId = result.ParentMessageId,
        ModelId = result.ModelId,
        CreatedAt = result.CreatedAt
    };

    public static ChatListResponse ToResponse(this ChatsReadModel model) => new()
    {
        Chats = model.Chats
            .Select(chat => new ChatListItemResponse
            {
                Id = chat.Id,
                Title = chat.Title,
                CurrentMessageId = chat.CurrentMessageId,
                UpdatedAt = chat.UpdatedAt,
                Preview = chat.Preview
            })
            .ToArray()
    };

    public static ChatResponse ToResponse(this ChatReadModel model) => new()
    {
        Id = model.Id,
        Title = model.Title,
        CurrentMessageId = model.CurrentMessageId,
        CreatedAt = model.CreatedAt,
        UpdatedAt = model.UpdatedAt,
        Messages = model.Messages
            .Select(message => new ChatPathMessageResponse
            {
                Id = message.Id,
                ParentMessageId = message.ParentMessageId,
                Role = message.Role,
                Content = message.Content,
                ModelId = message.ModelId,
                Status = message.Status,
                FailureReason = message.FailureReason,
                CreatedAt = message.CreatedAt,
                CompletedAt = message.CompletedAt,
                SiblingIndex = message.SiblingIndex
            })
            .ToArray(),
        SiblingGroups = model.SiblingGroups
            .Select(group => new ChatSiblingGroupResponse
            {
                ParentMessageId = group.ParentMessageId,
                SelectedMessageId = group.SelectedMessageId,
                SelectedSiblingIndex = group.SelectedSiblingIndex,
                SiblingCount = group.SiblingCount,
                Siblings = group.Siblings
                    .Select(sibling => new ChatSiblingResponse
                    {
                        Id = sibling.Id,
                        ParentMessageId = sibling.ParentMessageId,
                        Role = sibling.Role,
                        Preview = sibling.Preview,
                        CreatedAt = sibling.CreatedAt,
                        SiblingIndex = sibling.SiblingIndex,
                        IsSelected = sibling.IsSelected
                    })
                    .ToArray()
            })
            .ToArray()
    };

    public static ChatTreeResponse ToResponse(this ChatTreeReadModel model) => new()
    {
        Id = model.Id,
        Title = model.Title,
        CurrentMessageId = model.CurrentMessageId,
        Messages = model.Messages
            .Select(message => new ChatTreeMessageResponse
            {
                Id = message.Id,
                ParentMessageId = message.ParentMessageId,
                Role = message.Role,
                Content = message.Content,
                ModelId = message.ModelId,
                Status = message.Status,
                FailureReason = message.FailureReason,
                CreatedAt = message.CreatedAt,
                CompletedAt = message.CompletedAt,
                SiblingIndex = message.SiblingIndex
            })
            .ToArray()
    };
}
```

- [ ] **Step 4: CreateChat endpoint**

`CreateChat/Request.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.CreateChat;

internal sealed class Request
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required Guid ModelId { get; init; }
}
```

`CreateChat/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.CreateChat;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.CreateChat;

internal sealed class Endpoint(ISender sender) : Endpoint<Request, ChatGenerationResponse>
{
    public const string RouteName = "Chat.Chats.Create";

    public override void Configure()
    {
        Post("/chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Create Chat")
                .WithDescription("Creates a chat with a first user message and begins assistant generation.")
                .Produces<ChatGenerationResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        CreateChatCommand command = new(request.Title, request.Message, request.ModelId);

        ErrorOr<ChatGenerationResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(result.Value.ToResponse(), cancellation: ct);
    }
}
```

- [ ] **Step 5: GetChats endpoint**

`GetChats/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Queries.GetChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.GetChats;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<ChatListResponse>
{
    public const string RouteName = "Chat.Chats.GetAll";

    public override void Configure()
    {
        Get("/chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Chats")
                .WithDescription("Gets the authenticated user's chats ordered by most recently updated.")
                .Produces<ChatListResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<ChatsReadModel> result = await sender.Send(new GetChatsQuery(), ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(result.Value.ToResponse(), cancellation: ct);
    }
}
```

- [ ] **Step 6: GetChat endpoint**

`GetChat/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Queries.GetChat;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.GetChat;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<ChatResponse>
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
                .WithDescription("Gets a chat's active message path and sibling groups.")
                .Produces<ChatResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<ChatReadModel> result = await sender.Send(new GetChatQuery(Route<Guid>("chatId")), ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(result.Value.ToResponse(), cancellation: ct);
    }
}
```

- [ ] **Step 7: GetChatTree endpoint**

`GetChatTree/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Queries.GetChatTree;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.GetChatTree;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<ChatTreeResponse>
{
    public const string RouteName = "Chat.Chats.GetTree";

    public override void Configure()
    {
        Get("/chats/{chatId}/tree");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Chat Tree")
                .WithDescription("Gets the full message tree for a chat.")
                .Produces<ChatTreeResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<ChatTreeReadModel> result = await sender.Send(new GetChatTreeQuery(Route<Guid>("chatId")), ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(result.Value.ToResponse(), cancellation: ct);
    }
}
```

- [ ] **Step 8: SendMessage endpoint**

`SendMessage/Request.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.SendMessage;

internal sealed class Request
{
    public Guid? ParentMessageId { get; init; }
    public required string Message { get; init; }
    public required Guid ModelId { get; init; }
}
```

`SendMessage/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.SendMessage;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.SendMessage;

internal sealed class Endpoint(ISender sender) : Endpoint<Request, ChatGenerationResponse>
{
    public const string RouteName = "Chat.Chats.SendMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Send Message")
                .WithDescription("Adds a user message under the active (or specified) parent and begins assistant generation.")
                .Produces<ChatGenerationResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        SendMessageCommand command = new
        (
            Route<Guid>("chatId"),
            request.ParentMessageId,
            request.Message,
            request.ModelId
        );

        ErrorOr<ChatGenerationResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(result.Value.ToResponse(), cancellation: ct);
    }
}
```

- [ ] **Step 9: EditUserMessage endpoint**

`EditUserMessage/Request.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.EditUserMessage;

internal sealed class Request
{
    public required string Message { get; init; }
    public required Guid ModelId { get; init; }
}
```

`EditUserMessage/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.EditUserMessage;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.EditUserMessage;

internal sealed class Endpoint(ISender sender) : Endpoint<Request, ChatGenerationResponse>
{
    public const string RouteName = "Chat.Chats.EditUserMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages/{messageId}/edits");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Edit User Message")
                .WithDescription("Creates an edited sibling of a user message and begins assistant generation on the new branch.")
                .Produces<ChatGenerationResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        EditUserMessageCommand command = new
        (
            Route<Guid>("chatId"),
            Route<Guid>("messageId"),
            request.Message,
            request.ModelId
        );

        ErrorOr<ChatGenerationResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(result.Value.ToResponse(), cancellation: ct);
    }
}
```

- [ ] **Step 10: RegenerateAssistantMessage endpoint**

`RegenerateAssistantMessage/Request.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.RegenerateAssistantMessage;

internal sealed class Request
{
    public required Guid ModelId { get; init; }
}
```

`RegenerateAssistantMessage/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.RegenerateAssistantMessage;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.RegenerateAssistantMessage;

internal sealed class Endpoint(ISender sender) : Endpoint<Request, ChatGenerationResponse>
{
    public const string RouteName = "Chat.Chats.RegenerateAssistantMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages/{messageId}/regenerations");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Regenerate Assistant Message")
                .WithDescription("Creates a new assistant sibling for the same parent and begins generation.")
                .Produces<ChatGenerationResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        RegenerateAssistantMessageCommand command = new
        (
            Route<Guid>("chatId"),
            Route<Guid>("messageId"),
            request.ModelId
        );

        ErrorOr<ChatGenerationResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(result.Value.ToResponse(), cancellation: ct);
    }
}
```

- [ ] **Step 11: SelectChatMessage endpoint**

`SelectChatMessage/Endpoint.cs`:

```csharp
using Chat.Application.Chats.Commands.SelectChatMessage;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.SelectChatMessage;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.SelectMessage";

    public override void Configure()
    {
        Post("/chats/{chatId}/messages/{messageId}/select");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Select Chat Message")
                .WithDescription("Moves the active branch head to the specified message.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        SelectChatMessageCommand command = new(Route<Guid>("chatId"), Route<Guid>("messageId"));

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
```

- [ ] **Step 12: SSE stream endpoint**

Writes raw Server-Sent Events. On an already-finished message it replays the persisted content as one event plus a terminal event; while generating it replays the Redis Stream from `Last-Event-ID` (header) or the `lastEventId` query value.

`StreamChatMessage/Endpoint.cs`:

```csharp
using System.Text;

using Chat.Application.Abstractions.Chats;
using Chat.Application.Chats.Queries.GetChatStreamState;
using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Microsoft.AspNetCore.Http;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.StreamChatMessage;

internal sealed class Endpoint(ISender sender, IChatStreamStore streamStore) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.Chats.StreamMessage";

    public override void Configure()
    {
        Get("/chats/{chatId}/messages/{messageId}/stream");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Stream Chat Message")
                .WithDescription("Streams assistant generation deltas as Server-Sent Events; supports resume via Last-Event-ID.")
                .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Guid chatId = Route<Guid>("chatId");
        Guid messageId = Route<Guid>("messageId");

        ErrorOr<ChatStreamStateReadModel> stateResult =
            await sender.Send(new GetChatStreamStateQuery(chatId, messageId), ct);

        if (stateResult.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(stateResult));
            return;
        }

        HttpResponse response = HttpContext.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";

        ChatStreamStateReadModel state = stateResult.Value;

        if (state.Status != nameof(MessageStatus.Generating))
        {
            await WriteEventAsync(response, "0-0", ChatStreamEventTypes.Delta, state.Content ?? string.Empty, ct);

            string terminalType = state.Status == nameof(MessageStatus.Failed)
                ? ChatStreamEventTypes.Error
                : ChatStreamEventTypes.Done;

            await WriteEventAsync(response, "0-0", terminalType, state.FailureReason ?? string.Empty, ct);
            return;
        }

        string? lastEventId = HttpContext.Request.Headers["Last-Event-ID"].FirstOrDefault()
                              ?? Query<string?>("lastEventId", isRequired: false);

        ChatMessageId messageIdVo = ChatMessageId.FromDatabase(messageId);

        await foreach (ChatStreamEvent streamEvent in streamStore.ReadAsync(messageIdVo, lastEventId, ct))
        {
            await WriteEventAsync(response, streamEvent.Id, streamEvent.Type, streamEvent.Data ?? string.Empty, ct);
        }
    }

    private static async Task WriteEventAsync
    (
        HttpResponse response,
        string id,
        string eventType,
        string data,
        CancellationToken ct
    )
    {
        StringBuilder builder = new();
        builder.Append("id: ").Append(id).Append('\n');
        builder.Append("event: ").Append(eventType).Append('\n');

        foreach (string line in data.Split('\n'))
        {
            builder.Append("data: ").Append(line).Append('\n');
        }

        builder.Append('\n');

        await response.WriteAsync(builder.ToString(), ct);
        await response.Body.FlushAsync(ct);
    }
}
```

- [ ] **Step 13: Build**

Run: `dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj`
Expected: build succeeds.

- [ ] **Step 14: Commit**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats src/services/Chat/Chat.Api/Endpoints/CustomTags.cs
git commit -m "feat(chat): add chat crud and sse streaming endpoints"
```

---

## Task 10: EF Migration

**Files**
- Create: EF-generated migration ending in `_ChatTree.cs`
- Create: EF-generated designer ending in `_ChatTree.Designer.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

- [ ] **Step 1: Generate the migration**

Run (request elevated permission first):

```bash
dotnet ef migrations add ChatTree \
  --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj \
  --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj \
  --context ChatDbContext \
  --output-dir Database/Migrations
```

- [ ] **Step 2: Inspect the generated migration**

Verify:
- `chats` table exists with `current_message_id uuid not null` and **no** FK from `current_message_id`.
- `chat_messages` table exists; `content` is nullable; `failure_reason` is nullable; `model_id uuid` nullable with **no** FK.
- `chat_messages.chat_id` has cascade delete to `chats.id`.
- `chat_messages.parent_message_id` self-reference uses `OnDelete: Restrict`.
- `role` and `status` are stored as text.
- indexes match the configurations (`chats`: `{user_id, updated_at desc, id}` and `{user_id, id}`; `chat_messages`: `{chat_id, parent_message_id, sibling_index, id}`, `{chat_id, id}`, `{status}`).
- MassTransit outbox/inbox schema is untouched beyond incidental snapshot ordering.

- [ ] **Step 3: Build**

Run: `dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Database/Migrations
git commit -m "feat(chat): add chat tree ef migration"
```

---

## Task 11: Verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj`
Expected: build succeeds with no errors.

- [ ] **Step 2: Manual smoke checklist (run via Aspire/local once deployed)**

- `POST /chats` returns `200` with `chatId` + `assistantMessageId`.
- `GET /chats/{chatId}/messages/{assistantMessageId}/stream` streams `delta` events then a `done` event.
- Disconnecting and reconnecting the SSE call with `Last-Event-ID` resumes without replaying already-seen deltas.
- After completion, `GET /chats/{chatId}` shows the assistant message `Completed` with the full content.
- `POST /chats/{chatId}/messages/{messageId}/edits` and `/regenerations` create sibling branches; `GET /chats/{chatId}` sibling groups reflect the selected branch.
- `POST /chats/{chatId}/messages/{messageId}/select` moves the head and changes the active path returned by `GET /chats/{chatId}`.

- [ ] **Step 3: Final implementation note**

Report explicitly:
- Domain, application, infrastructure, and API layers implemented; no tests added in this pass (explicit scope decision).
- Real LLM provider integration is stubbed behind `IChatCompletionClient` (`StubChatCompletionClient`); swapping in a real provider is an interface implementation only.
- Generation is decoupled from the HTTP connection via an in-process channel + hosted worker; SSE resume works across reconnects because deltas live in Redis Streams.
- Orphaned `Generating` messages are reconciled by `StaleGenerationSweeper`.

---

## Manual Checklist Summary

- [ ] Domain value objects implemented.
- [ ] `ChatThread` aggregate + `ChatMessage` state machine implemented.
- [ ] Application abstractions, results, errors implemented.
- [ ] Generation commands implemented.
- [ ] Queries and read models implemented.
- [ ] EF mappings, repository, DbContext, DI implemented.
- [ ] Dapper readers implemented.
- [ ] Redis streaming pipeline (stream store, stub client, queue, worker, sweeper) implemented.
- [ ] FastEndpoints (CRUD + SSE) implemented.
- [ ] EF migration generated and inspected.
- [ ] Chat API build passes.
- [ ] Final note documents the stubbed LLM client and no-tests scope decision.
