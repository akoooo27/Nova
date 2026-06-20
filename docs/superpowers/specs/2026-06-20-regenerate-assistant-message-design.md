# Regenerate Assistant Message — Design Spec

**Goal:** Let a user regenerate an assistant message they did not like. They click a regenerate icon in the UI and a new assistant answer is produced as a sibling of the original under the same user message. The new answer becomes the active branch head; the previous answer remains in the tree so the client can page between versions. The regenerate may reuse the original model or use a different one.

**Builds on:** The existing `ChatThread` aggregate, which already exposes `RegenerateAssistant`, and the existing turn pipeline (`TurnRequested` → `ChatTurnOrchestrator` → `ContextBuilder` → `IAgentRunner`). This feature is wiring: an application command, a handler, and an API endpoint over machinery that already exists. There is no domain change and no migration.

---

## 1. Scope

**In scope**

- `POST /chats/{chatId}/messages/{messageId}/regenerate` to regenerate a single assistant message.
- Reuse the original model when no model is supplied.
- Regenerate with a different model when a `modelId` is supplied.
- Reuse of the existing async turn pipeline to produce the new answer.
- The new assistant message becomes the active branch head (`CurrentMessageId`).
- `202 Accepted` with the same `TurnStartedResponse` shape that `SendMessage` returns.

**Out of scope**

- Regenerate-with-feedback / prompt steering (the ChatGPT "contextual retry" hidden message). Deferred to a later spec.
- A sibling-selection endpoint (`SelectMessage`). The client pages between siblings using the tree already returned by `GetChat`; persisting a manual switch is a separate spec.
- Editing the user message (`EditUserMessage`). Separate concern.
- Any change to how branches are rendered or how the version pager is computed on the client.

---

## 2. Background: how regenerate maps onto the tree

A user message is the fork point. Each assistant answer under it is a sibling branch. Regenerate adds one more assistant sibling under the same user message and moves the head to it.

```text
user message (parent)
 ├── assistant v1   (the answer the user disliked)
 └── assistant v2   (regenerated — new head, same parent)
```

Both assistants share the same immediate parent (the user message), so strict user/assistant alternation is preserved and `ContextBuilder` rebuilds history correctly by walking up from the new assistant's parent. The client already receives every node and its `children[]` from `GetChat`, plus `CurrentMessageId`, so it can render the `‹ 1/2 ›` version pager with no extra endpoint.

---

## 3. Domain Model

No changes. The aggregate already provides the operation:

```csharp
public ErrorOr<ChatMessage> RegenerateAssistant(
    ChatMessageId messageId,
    LlmModelId llmModelId,
    DateTimeOffset createdAt)
```

Existing behavior this spec relies on:

- The target must be an assistant message with a parent. Otherwise `RegenerationTargetMustBeAssistant`.
- The target must not be `Generating`. Otherwise `CannotRegenerateWhileGenerating`. This prevents racing two live generations under one parent.
- A new assistant sibling is created under the target's `ParentMessageId` with the next `SiblingIndex`, status `Generating`.
- The head moves to the new sibling (`SetHead`), so the regenerated answer is shown by default.

---

## 4. Application Flow

### 4.1 Command

Add `RegenerateMessageCommand` under `Chat.Application/Chats/Commands/RegenerateMessage/`.

```csharp
public sealed record RegenerateMessageCommand(
    Guid ChatId,
    Guid MessageId,
    Guid? ModelId = null,
    bool ForceUseSearch = false
) : ICommand<ErrorOr<TurnStartedResult>>;
```

`MessageId` is the assistant message being regenerated. `ModelId` is the optional model override: when `null`, the new sibling reuses the target message's model; when supplied, it regenerates with that model. `ForceUseSearch` mirrors `SendMessage` so a regenerate can opt into search; it flows through `TurnGenerationOptions` unchanged.

### 4.2 Validator

Add `RegenerateMessageCommandValidator`, mirroring `SendMessageCommandValidator`:

- `ChatId` not empty.
- `MessageId` not empty.
- `ModelId`, when present (non-null), not `Guid.Empty`.

### 4.3 Handler

Add `RegenerateMessageHandler`, mirroring `SendMessageHandler`:

1. Create `UserId` from `IUserContext.UserId` and `ChatId`, `ChatMessageId` from the command. Aggregate validation errors as `SendMessageHandler` does.
2. Load the thread via `IChatRepository.GetByIdAsync(chatId, userId, ct)`. Missing ⇒ `ChatOperationErrors.ChatNotFound(chatId)`.
3. Find the target assistant message via `thread.FindMessage(messageId)`. Missing ⇒ `ChatErrors.MessageNotFound(messageId)`.
4. Resolve the model:
   - If `ModelId` is supplied, create `LlmModelId` from it.
   - Otherwise use the target message's existing `LlmModelId`.
   - If the target has no model and none was supplied, return a model-not-configured error.
5. Run `ModelUsability.EnsureUsableAsync(providers, modelId, ct, requiresToolCalling: generationOptions.ForceUseSearch)`, same guard as `SendMessage`.
6. Call `thread.RegenerateAssistant(messageId, modelId, now)`. Propagate domain errors.
7. Publish `TurnRequested(ChatId, UserId, AssistantMessageId: newSibling.Id, Options: new TurnGenerationOptions(ForceUseSearch))` **before** `SaveChangesAsync`, preserving the outbox ordering documented in `SendMessageHandler`.
8. `SaveChangesAsync`.
9. Return `TurnStartedResult(ChatId, UserMessageId: target.ParentMessageId, AssistantMessageId: newSibling.Id)`.

`now` comes from `IDateTimeProvider.UtcNow`, read once.

### 4.4 Result

Reuse the existing `TurnStartedResult(ChatId, UserMessageId, AssistantMessageId)`. Regenerate creates no new user message, so `UserMessageId` is populated with the target's `ParentMessageId` (the existing user message that owns the branch). This avoids a second near-identical result/response type. The new sibling id is returned as `AssistantMessageId` so the client can subscribe to the turn stream exactly as it does after `SendMessage`.

---

## 5. Turn Pipeline

No changes. After the command commits and the `TurnRequested` event is delivered:

- `ChatTurnOrchestrator` loads the thread, finds the new assistant message (status `Generating`), and runs the turn.
- `ContextBuilder` walks up from the new assistant's `ParentMessageId` (the original user message) and rebuilds history along that branch. Because the regenerated sibling has the same parent as the original answer, the model sees identical prior context — only the answer is re-rolled.
- Redelivery is already idempotent: the orchestrator no-ops when the target message is no longer `Generating`.

---

## 6. API

FastEndpoints, mirroring `SendMessage/Endpoint.cs`:

```csharp
Post("/chats/{chatId}/messages/{messageId}/regenerate");
Version(1);
```

Request body:

```json
{
  "modelId": "0b5d...optional",
  "forceUseSearch": false
}
```

`modelId` omitted ⇒ reuse the original model. The endpoint maps the two route ids plus the body into `RegenerateMessageCommand`, sends it through `ISender`, and on error returns `CustomResults.Problem(result)`.

Advertised responses:

- `202 Accepted` with `TurnStartedResponse`
- `400 Bad Request`
- `401 Unauthorized`
- `404 Not Found`
- `409 Conflict`

Do not use ASP.NET Core controllers. Use the existing `Mediator` package, not MediatR.

---

## 7. Data Flow

```text
POST /chats/{chatId}/messages/{messageId}/regenerate
  -> FastEndpoints endpoint maps route ids + body
  -> RegenerateMessageCommand(chatId, messageId, modelId?, forceUseSearch)
  -> RegenerateMessageHandler
       loads ChatThread for current UserId
       resolves model = modelId ?? target.LlmModelId
       ModelUsability.EnsureUsableAsync
       thread.RegenerateAssistant(messageId, model, now)  // new assistant sibling, new head
       bus.PublishAsync(TurnRequested{ AssistantMessageId = sibling })
       SaveChangesAsync
  -> 202 Accepted + TurnStartedResponse(chatId, parentUserMessageId, newAssistantMessageId)

  ... asynchronously ...
  TurnRequested
    -> ChatTurnOrchestrator.RunTurnAsync
    -> ContextBuilder builds history from sibling.ParentMessageId up
    -> IAgentRunner streams tokens
    -> CompleteAssistantMessage / FailAssistantMessage
```

---

## 8. Error Handling

All errors flow through `ErrorOr` and `CustomResults.Problem`:

- Invalid route ids / empty `modelId`: validation error ⇒ `400`.
- Chat not found for the authenticated user: `ChatOperationErrors.ChatNotFound` ⇒ `404`.
- Message not found in the chat: `ChatErrors.MessageNotFound` ⇒ `404`.
- Target is not a regenerable assistant: `ChatErrors.RegenerationTargetMustBeAssistant` (`Error.Conflict`) ⇒ `409`.
- Target still generating: `ChatErrors.CannotRegenerateWhileGenerating` (`Error.Conflict`) ⇒ `409`.
- Model not found / not usable (including an unusable override model): model-usability errors ⇒ `400`.

---

## 9. Testing

Per project convention, test work is added when requested. If requested, focus on:

- Handler tests reusing the existing `tests/Chat/Chat.Application.Tests/Turns` fakes:
  - Regenerate happy path, no `modelId` ⇒ new sibling reuses the target's model, `TurnRequested` published, head moved.
  - Regenerate with `modelId` override ⇒ new sibling uses the override model.
  - Chat not found ⇒ `ChatNotFound`.
  - Target message not found ⇒ `MessageNotFound`.
  - Target is a user message ⇒ `RegenerationTargetMustBeAssistant`.
  - Target still generating ⇒ `CannotRegenerateWhileGenerating`.
  - Override model not usable ⇒ usability error, nothing published.
- Validator tests for empty ids and empty override `modelId`.

`RegenerateAssistant` domain behavior is already covered by `ChatThreadTests`.

---

## 10. Alternatives Considered

### Recommended: optional model override on a dedicated regenerate endpoint

One endpoint, model optional. Matches the observed ChatGPT regenerate, reuses the entire existing turn pipeline, and requires no domain or schema change.

### Always require a model on regenerate

Explicit but forces the client to always know and send the current model just to retry. Rejected as unnecessary friction for the common "try again" case.

### Reuse `SendMessage` with a flag

Overloading `SendMessage` to also mean "regenerate" would blur a clear command boundary and complicate validation (regenerate has no new message text). Rejected in favor of a separate command.

---

## 11. Implementation Notes

- Read `IDateTimeProvider.UtcNow` once per handler execution.
- Publish `TurnRequested` before `SaveChangesAsync`, matching `SendMessageHandler`'s outbox ordering.
- Do not introduce a new result type; reuse `TurnStartedResult` with `UserMessageId` = target's `ParentMessageId`.
- `ForceUseSearch` is the only generation option carried today; it flows through `TurnGenerationOptions` unchanged.
- No EF migration. No change to `ChatThread`, `ChatMessage`, `TurnRequested`, `TurnGenerationOptions`, or `ContextBuilder`.
