# Force Use Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a ChatGPT-like `forceUseSearch` request flag that enables web search for a single async assistant turn without storing enabled tools on chat messages.

**Architecture:** Treat search enablement as turn execution metadata. The HTTP request maps `forceUseSearch` into the application command, the command publishes it on `TurnRequested`, the worker carries it into `TurnContext`, and `AgentFrameworkRunner` attaches the `web_search` tool only for that turn.

**Tech Stack:** C#/.NET, FastEndpoints, Mediator.SourceGenerator/Mediator.Abstractions, MassTransit outbox, Microsoft Agents AI, existing `IAgentTool`/`WebSearchTool`.

---

## Design Update: Tool Availability vs Tool Mode

The current intended behavior is ChatGPT-like:

- `ForceUseSearch = false`: expose the normal allowed tool set and let the agent framework decide whether to call a tool with `ChatToolMode.Auto`.
- `ForceUseSearch = true`: expose the allowed tool set and require the `web_search` tool with `ChatToolMode.RequireSpecific(AgentToolNames.WebSearch)`.

Keep these as two separate policy decisions in `AgentFrameworkRunner`:

```csharp
IReadOnlyList<IAgentTool> selectedTools = SelectTools(context.GenerationOptions);
ChatToolMode toolMode = SelectToolMode(context.GenerationOptions);
```

`SelectTools` answers: "Which tools is this turn allowed to see?"

`SelectToolMode` answers: "Can the model choose freely, or must it call a specific tool?"

Today, exposing all registered tools is acceptable because `web_search` is the only registered tool. When more powerful tools are added, do not let "registered in DI" automatically mean "available to every chat turn." Add an explicit selection policy so safe/default tools can remain auto-available while sensitive tools require a specific option or permission.

## Scope

Implement this path:

```text
POST /v1/chats or POST /v1/chats/{chatId}/messages
  -> forceUseSearch: true
  -> CreateChatCommand / SendMessageCommand
  -> ModelUsability validates model supports tool calling
  -> TurnRequested.ForceUseSearch
  -> ChatTurnOrchestrator
  -> TurnContext.ForceUseSearch
  -> AgentFrameworkRunner exposes web_search for this turn
```

Do not add `EnabledTools` or `ForceUseSearch` to `ChatMessage`.

Do not introduce a general tool-selection API yet. The public contract is intentionally ChatGPT-like: the client asks to force web search, not to select arbitrary tools.

Per `AGENTS.md`, this plan does not schedule new or modified tests until the user explicitly approves test work. Verification steps use build/manual inspection only; every `dotnet` command requires elevated permission before execution.

## File Structure

- Modify `src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Endpoint.cs`
  - Add `ForceUseSearch` to the request record.
  - Pass the value to `CreateChatCommand`.

- Modify `src/services/Chat/Chat.Api/Endpoints/Chats/SendMessage/Endpoint.cs`
  - Add `ForceUseSearch` to the request record.
  - Pass the value to `SendMessageCommand`.

- Modify `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommand.cs`
  - Add a `bool ForceUseSearch` command property.

- Modify `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageCommand.cs`
  - Add a `bool ForceUseSearch` command property.

- Modify `src/services/Chat/Chat.Application/Chats/Errors/ChatOperationErrors.cs`
  - Add a conflict error for a model that cannot use tools/search.

- Modify `src/services/Chat/Chat.Application/Chats/ModelUsability.cs`
  - Add a `requiresToolCalling` parameter and validate `model.Profile.Capabilities.SupportsToolCalling`.

- Modify `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`
  - Pass `command.ForceUseSearch` into model usability validation.
  - Publish it on `TurnRequested`.

- Modify `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageHandler.cs`
  - Pass `command.ForceUseSearch` into model usability validation.
  - Publish it on `TurnRequested`.

- Modify `src/services/Chat/Chat.Application/Turns/TurnRequested.cs`
  - Add `bool ForceUseSearch`.

- Modify `src/services/Chat/Chat.Application/Abstractions/Turns/TurnContext.cs`
  - Add `bool ForceUseSearch`.

- Modify `src/services/Chat/Chat.Application/Turns/ContextBuilder.cs`
  - Accept `ForceUseSearch` from the current turn job, not from persisted messages.

- Modify `src/services/Chat/Chat.Application/Abstractions/Turns/IContextBuilder.cs`
  - Add `bool forceUseSearch` to `BuildAsync`.

- Modify `src/services/Chat/Chat.Application/Turns/ChatTurnOrchestrator.cs`
  - Pass `job.ForceUseSearch` into `contextBuilder.BuildAsync`.

- Modify `src/services/Chat/Chat.Infrastructure/Agents/AgentFrameworkRunner.cs`
  - Filter tools so `web_search` is attached only when `context.ForceUseSearch` is true.
  - Add a direct search instruction when forced.

## Task 1: Add API Request Flag

**Files:**
- Modify: `src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Endpoint.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/Chats/SendMessage/Endpoint.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommand.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageCommand.cs`

- [ ] **Step 1: Update create-chat request and command mapping**

In `src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Endpoint.cs`, change the request record and command construction to:

```csharp
internal sealed record Request(string Message, Guid ModelId, bool ForceUseSearch = false);
```

```csharp
CreateChatCommand command = new(request.Message, request.ModelId, request.ForceUseSearch);
```

- [ ] **Step 2: Update send-message request and command mapping**

In `src/services/Chat/Chat.Api/Endpoints/Chats/SendMessage/Endpoint.cs`, change the request record and command construction to:

```csharp
internal sealed record Request(string Message, Guid ModelId, bool ForceUseSearch = false);
```

```csharp
SendMessageCommand command = new
(
    ChatId: Route<Guid>("chatId"),
    Message: request.Message,
    LlmModelId: request.ModelId,
    ForceUseSearch: request.ForceUseSearch
);
```

- [ ] **Step 3: Update command contracts**

In `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommand.cs`, replace the command record with:

```csharp
public sealed record CreateChatCommand
(
    string Message,
    Guid LlmModelId,
    bool ForceUseSearch = false
) : ICommand<ErrorOr<TurnStartedResult>>;
```

In `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageCommand.cs`, replace the command record with:

```csharp
public sealed record SendMessageCommand
(
    Guid ChatId,
    string Message,
    Guid LlmModelId,
    bool ForceUseSearch = false
) : ICommand<ErrorOr<TurnStartedResult>>;
```

- [ ] **Step 4: Inspect compile references**

Run this search and update any constructor calls that still use the old positional shape:

```bash
rg -n "new\\s+\\(.*CreateChatCommand|CreateChatCommand\\(|SendMessageCommand\\(" src tests
```

Expected: all direct command construction either passes the fourth boolean or relies on the default.

## Task 2: Validate Tool-Capable Models When Search Is Forced

**Files:**
- Modify: `src/services/Chat/Chat.Application/Chats/Errors/ChatOperationErrors.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/ModelUsability.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageHandler.cs`

- [ ] **Step 1: Add the operation error**

In `src/services/Chat/Chat.Application/Chats/Errors/ChatOperationErrors.cs`, add:

```csharp
public static Error LlmModelDoesNotSupportToolCalling(LlmModelId modelId) =>
    Error.Conflict
    (
        code: "Chat.LlmModelDoesNotSupportToolCalling",
        description: $"LLM model '{modelId.Value}' does not support tool calling."
    );
```

- [ ] **Step 2: Extend model usability validation**

In `src/services/Chat/Chat.Application/Chats/ModelUsability.cs`, change the method signature to:

```csharp
public static async Task<ErrorOr<Success>> EnsureUsableAsync
(
    ILlmProviderRepository providers,
    LlmModelId modelId,
    CancellationToken cancellationToken,
    bool requiresToolCalling = false
)
```

After the existing disabled-model check, add:

```csharp
if (requiresToolCalling && !model.Profile.Capabilities.SupportsToolCalling)
{
    return ChatOperationErrors.LlmModelDoesNotSupportToolCalling(modelId);
}
```

- [ ] **Step 3: Validate create-chat forced search**

In `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`, change the `ModelUsability.EnsureUsableAsync` call to:

```csharp
ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
(
    providers: providers,
    modelId: modelId,
    cancellationToken: cancellationToken,
    requiresToolCalling: command.ForceUseSearch
);
```

- [ ] **Step 4: Validate send-message forced search**

In `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageHandler.cs`, change the `ModelUsability.EnsureUsableAsync` call to:

```csharp
ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
(
    providers: providers,
    modelId: modelId,
    cancellationToken: cancellationToken,
    requiresToolCalling: command.ForceUseSearch
);
```

Use `modelId`, not `modelIdResult.Value`, because the handler already assigned the validated value.

## Task 3: Carry ForceUseSearch Across the Worker Boundary

**Files:**
- Modify: `src/services/Chat/Chat.Application/Turns/TurnRequested.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageHandler.cs`

- [ ] **Step 1: Add the job property**

In `src/services/Chat/Chat.Application/Turns/TurnRequested.cs`, replace the record with:

```csharp
public sealed record TurnRequested
(
    Guid ChatId,
    string UserId,
    Guid AssistantMessageId,
    bool ForceUseSearch = false
);
```

The default keeps old serialized messages tolerant if any local dev queue contains older shape messages.

- [ ] **Step 2: Publish forced-search state from create-chat**

In `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`, change the `TurnRequested` creation to:

```csharp
TurnRequested turnRequested = new
(
    ChatId: thread.Id.Value,
    UserId: userId.Value,
    AssistantMessageId: assistantMessageId.Value,
    ForceUseSearch: command.ForceUseSearch
);
```

- [ ] **Step 3: Publish forced-search state from send-message**

In `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageHandler.cs`, change the `TurnRequested` creation to:

```csharp
TurnRequested turnRequested = new
(
    ChatId: thread.Id.Value,
    UserId: userId.Value,
    AssistantMessageId: assistantMessageId.Value,
    ForceUseSearch: command.ForceUseSearch
);
```

## Task 4: Carry ForceUseSearch Into TurnContext

**Files:**
- Modify: `src/services/Chat/Chat.Application/Abstractions/Turns/TurnContext.cs`
- Modify: `src/services/Chat/Chat.Application/Abstractions/Turns/IContextBuilder.cs`
- Modify: `src/services/Chat/Chat.Application/Turns/ContextBuilder.cs`
- Modify: `src/services/Chat/Chat.Application/Turns/ChatTurnOrchestrator.cs`

- [ ] **Step 1: Add the context property**

In `src/services/Chat/Chat.Application/Abstractions/Turns/TurnContext.cs`, replace the `TurnContext` record with:

```csharp
public sealed record TurnContext
(
    Guid TurnId,
    Guid ChatId,
    string UserId,
    string ExternalModelId,
    string SystemPrompt,
    bool ForceUseSearch,
    IReadOnlyList<TurnMessage> Messages
);
```

- [ ] **Step 2: Update the context builder interface**

In `src/services/Chat/Chat.Application/Abstractions/Turns/IContextBuilder.cs`, change `BuildAsync` to include the new parameter:

```csharp
Task<ErrorOr<TurnContext>> BuildAsync
(
    ChatThread thread,
    ChatMessage assistantMessage,
    RetrievedMemories memories,
    bool forceUseSearch,
    CancellationToken cancellationToken
);
```

- [ ] **Step 3: Update ContextBuilder signature and construction**

In `src/services/Chat/Chat.Application/Turns/ContextBuilder.cs`, add `bool forceUseSearch` before the cancellation token:

```csharp
public async Task<ErrorOr<TurnContext>> BuildAsync
(
    ChatThread thread,
    ChatMessage assistantMessage,
    RetrievedMemories memories,
    bool forceUseSearch,
    CancellationToken cancellationToken
)
```

Then add the property to the returned context:

```csharp
return new TurnContext
(
    TurnId: assistantMessage.Id.Value,
    ChatId: thread.Id.Value,
    UserId: thread.UserId.Value,
    ExternalModelId: model.ExternalModelId.Value,
    SystemPrompt: DefaultSystemPrompt,
    ForceUseSearch: forceUseSearch,
    Messages: history
);
```

- [ ] **Step 4: Pass the job value from the orchestrator**

In `src/services/Chat/Chat.Application/Turns/ChatTurnOrchestrator.cs`, change the `contextBuilder.BuildAsync` call to:

```csharp
ErrorOr<TurnContext> contextResult = await contextBuilder.BuildAsync
(
    thread: thread,
    assistantMessage: assistantMessage,
    memories: memories,
    forceUseSearch: job.ForceUseSearch,
    cancellationToken: cancellationToken
);
```

- [ ] **Step 5: Update fake context builders if tests are later approved**

If test work is approved, update `tests/Chat/Chat.Application.Tests/Turns/FakeContextBuilder.cs` to match the new interface signature and to preserve the `forceUseSearch` value in returned contexts.

## Task 5: Filter Tools in AgentFrameworkRunner

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/Agents/AgentFrameworkRunner.cs`

- [ ] **Step 1: Add the tool-name using**

At the top of `src/services/Chat/Chat.Infrastructure/Agents/AgentFrameworkRunner.cs`, add:

```csharp
using Chat.Application.Turns.Tools;
```

- [ ] **Step 2: Add forced search instruction constant**

Inside `AgentFrameworkRunner`, add:

```csharp
private const string ForceSearchInstruction =
    "For this turn, use web search before answering. Base the answer on the search results when they are available.";
```

- [ ] **Step 3: Filter registered tools by ForceUseSearch**

Replace the current `tools` construction with:

```csharp
IEnumerable<IAgentTool> enabledTools = context.ForceUseSearch
    ? _tools.Where(tool => tool.Name == AgentToolNames.WebSearch)
    : [];

IList<AITool> tools = enabledTools
    .Select(tool => (AITool)AIFunctionFactory.Create
    (
        method: tool.CreateInvocation(),
        new AIFunctionFactoryOptions { Name = tool.Name }
    ))
    .ToList();
```

- [ ] **Step 4: Add instruction-level forcing**

Before creating the `AIAgent`, compute instructions:

```csharp
string instructions = context.ForceUseSearch
    ? $"{context.SystemPrompt}{Environment.NewLine}{Environment.NewLine}{ForceSearchInstruction}"
    : context.SystemPrompt;
```

Then change the agent creation to:

```csharp
AIAgent agent = _client
    .GetChatClient(context.ExternalModelId)
    .AsAIAgent(instructions: instructions, tools: tools);
```

- [ ] **Step 5: Verify hard tool-choice support during implementation**

Inspect the installed Microsoft Agents AI package APIs before finalizing. If this version exposes a per-run or agent-level required tool-choice option, prefer that over instruction-only forcing while keeping the same `TurnContext.ForceUseSearch` input.

Do not add a new abstraction for tool choice in this task. The only supported public behavior is forced web search.

## Task 6: Build and Manual Verification

**Files:**
- No source files expected.

- [ ] **Step 1: Search for stale construction**

Run:

```bash
rg -n "TurnContext\\(|TurnRequested\\(|BuildAsync\\(" src tests
```

Expected: every construction and call includes the new `ForceUseSearch` or `forceUseSearch` value, except calls intentionally relying on default values.

- [ ] **Step 2: Build with elevated permission**

Before running, ask for elevated permission as required by `AGENTS.md`.

Run:

```bash
dotnet build Nova.slnx
```

Expected: build succeeds with no compile errors from changed command records, context builder interface, or turn runner.

- [ ] **Step 3: Manual request shape check**

Use this request body for `POST /v1/chats`:

```json
{
  "message": "Who won MVP?",
  "modelId": "00000000-0000-0000-0000-000000000000",
  "forceUseSearch": true
}
```

Expected behavior in code review:

```text
Endpoint Request.ForceUseSearch
  -> CreateChatCommand.ForceUseSearch
  -> ModelUsability requires SupportsToolCalling
  -> TurnRequested.ForceUseSearch
  -> TurnContext.ForceUseSearch
  -> AgentFrameworkRunner attaches web_search only for this turn
```

- [ ] **Step 4: Confirm domain remains clean**

Run:

```bash
rg -n "ForceUseSearch|EnabledTools" src/services/Chat/Chat.Domain
```

Expected: no matches. Search state is not persisted on `ChatMessage` or `ChatThread`.

## Deferred Until Explicit Approval

Per `AGENTS.md`, ask the user before adding or modifying tests. If approved, add focused coverage for:

- `CreateChatHandler` publishes `TurnRequested.ForceUseSearch = true` when requested.
- `SendMessageHandler` publishes `TurnRequested.ForceUseSearch = true` when requested.
- `ModelUsability` rejects forced search for models with `SupportsToolCalling = false`.
- `ContextBuilder` copies the job value into `TurnContext`.
- `AgentFrameworkRunner` exposes no tools when `ForceUseSearch = false` and exposes only `web_search` when true. This may need a small test seam around tool adaptation if direct runner testing is awkward.

## Self-Review

- Spec coverage: The plan covers API contract, command flow, model capability validation, outbox job metadata, turn context, runner tool filtering, and domain non-persistence.
- Placeholder scan: No task relies on `TBD`, vague "handle errors", or unspecified files.
- Type consistency: The flag is consistently named `ForceUseSearch` in records and `forceUseSearch` as a local/interface parameter.
- Scope check: This is one subsystem: forced web search for chat turns. General tool selection, client metadata/source reporting, and stored generation options are intentionally out of scope.
