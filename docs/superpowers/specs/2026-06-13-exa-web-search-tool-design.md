# Exa Web Search Tool — Design Spec

**Goal:** Give chat agents a web search tool backed by [Exa](https://exa.ai). The model calls a `web_search` function; we hit Exa's `/search` endpoint, return ranked results with snippets, and let the model synthesize an answer with citations. The tool is **drag-and-drop**: one DI line adds it, deleting that line removes it entirely, and swapping Exa for another provider is a one-class change. The user toggles web search **per message**; the choice is persisted on the turn so the worker honors it without trusting the job payload.

**Builds on:** `docs/superpowers/specs/2026-06-12-chat-turn-pipeline-design.md` and its plan. This spec realizes **Rule 5** ("Agent tools get the same quarantine"), which that design deliberately deferred.

---

## 1. Binding rules inherited from the turn pipeline

These are not new; they constrain every decision below. Violating them recreates the coupling the pipeline design exists to prevent.

1. **Rule 1 — Agent Framework quarantine.** `Microsoft.Agents.AI` / `Microsoft.Extensions.AI` types appear **only** in `Chat.Infrastructure/Agents/`. The tool seam exposed to the rest of the system is provider-agnostic.
2. **Rule 3 — Cross-cutting = decorator + one DI registration.** Telemetry (`tool_used`) rides the existing `TelemetryAgentRunner` decorator; no edits to the hot path.
3. **Rule 5 — Tool quarantine.** Each tool is a plain class with a narrow, constructor-injected dependency set, registered in one place. `AgentFrameworkRunner` is the only code that adapts a tool to framework types. **Tools never receive `DbContext` or the service provider** — they receive the one seam they need.
4. **Rule 6 — `TurnEvent` is append-only.** `ToolCallEvent` / `ToolResultEvent` already exist; we add no new event shapes.
5. **Rule 7 — State transitions go through the `ChatThread` aggregate.** The per-turn tool selection is set via the aggregate, never via SQL.
6. **Ids-only job rule.** `TurnRequested` carries ids only; the worker re-loads all state from the database and never trusts payload state. The tool selection is therefore **persisted**, not carried in the job.
7. **Rule 4 — `IContextBuilder` assembles system prompt + history + memories.** We do **not** add a parameter to `BuildAsync`; we enrich the `TurnContext` *result* it already produces.

---

## 2. Architecture

### 2.1 The tool seam (`IAgentTool`) — the drag-and-drop unit

A provider-agnostic seam in **Application**. It carries no framework types, so it is legal anywhere.

```csharp
// Chat.Application/Abstractions/Turns/IAgentTool.cs
namespace Chat.Application.Abstractions.Turns;

public interface IAgentTool
{
    /// <summary>Stable identifier (e.g. "web_search"). Used for per-turn toggling and telemetry.</summary>
    string Name { get; }

    /// <summary>
    /// The delegate the model invokes. Its parameters and [Description] attributes drive the
    /// generated JSON schema. Returns the string the model reads back as the tool result.
    /// </summary>
    Delegate CreateInvocation();
}
```

`Delegate` and `System.ComponentModel.DescriptionAttribute` are BCL types — Rule 1 stays intact. The runner is the sole adapter to `AIFunction`.

### 2.2 The web search tool (`WebSearchTool`) — Application

Lives in Application, depends on **one** narrow seam (`IWebSearchClient`), fully unit-testable with a fake — no HTTP. Provider-agnostic name; "Exa" appears nowhere here.

```csharp
// Chat.Application/Turns/Tools/WebSearchTool.cs
internal sealed class WebSearchTool(IWebSearchClient client) : IAgentTool
{
    public string Name => AgentToolNames.WebSearch; // "web_search"

    public Delegate CreateInvocation() => SearchAsync;

    [Description("Search the public web for current information. Returns ranked results with snippets to cite.")]
    private async Task<string> SearchAsync(
        [Description("The search query.")] string query,
        [Description("Maximum number of results to return (1-10).")] int count = 5,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WebSearchResult> results = await client.SearchAsync(query, count, cancellationToken);
        // Render to a compact, model-friendly numbered block: [n] Title — URL\n   snippet
        return WebSearchResultFormatter.ToToolResult(results);
    }
}
```

`AgentToolNames` is a small static catalog of known tool-name constants in Application. The API validates requested names against it; the domain stores the (validated) strings opaquely.

### 2.3 Provider seam + Exa adapter — Infrastructure

```csharp
// Chat.Application/Abstractions/Turns/IWebSearchClient.cs
public sealed record WebSearchResult(string Title, string Url, string Snippet, DateTimeOffset? PublishedAt);

public interface IWebSearchClient
{
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int count, CancellationToken cancellationToken);
}
```

```
Chat.Infrastructure/Agents/Tools/
  ExaWebSearchClient.cs   // IWebSearchClient via typed HttpClient -> POST {BaseUrl}/search
  ExaOptions.cs           // "Exa" config section
```

- **Request:** `POST /search` with `{ query, numResults, contents: { text: { maxCharacters }, highlights: {} } }`, auth header `x-api-key`.
- **Response → `WebSearchResult`:** title, url, the best highlight (fallback to a truncated `text`), and `publishedDate` when present.
- **Resilience:** typed `HttpClient` registered with `AddStandardResilienceHandler()` (`Microsoft.Extensions.Http.Resilience`, already in `Directory.Packages.props`).
- **Count:** the tool's `count` parameter (default 5) is the single source of truth for how many results to request; the client clamps it to `[1, ExaOptions.MaxResults]` for cost control.

```csharp
// Chat.Infrastructure/Agents/Tools/ExaOptions.cs
public sealed class ExaOptions
{
    public const string SectionName = "Exa";

    [Required] public string ApiKey { get; init; } = string.Empty;
    public Uri BaseUrl { get; init; } = new("https://api.exa.ai");
    public int MaxResults { get; init; } = 10; // hard cap; the tool's `count` param is clamped to this
}
```

### 2.4 Runner adaptation — the only framework-touching code

`AgentFrameworkRunner` gains an injected `IEnumerable<IAgentTool>` (the full registered catalog). Per turn it:

1. Filters to tools whose `Name` is in `context.EnabledTools`.
2. Adapts each via `AIFunctionFactory.Create(tool.CreateInvocation(), new AIFunctionFactoryOptions { Name = tool.Name })` — parameter `[Description]`s generate the schema.
3. Attaches the resulting `AIFunction`s to the agent and enables automatic function invocation.

**Empty enabled set → no tools attached → today's exact behavior.** This is the only file that imports the framework, preserving Rule 1.

---

## 3. The per-message toggle

### 3.1 Data flow

```
Request.tools: ["web_search"]   (CreateChat and SendMessage)
   → Command.Tools
   → send-time validation (known names + model capability)         §3.3
   → ChatThread.BeginAssistantMessage(..., enabledTools, ...)        Rule 7
   → ChatMessage.EnabledTools  (persisted jsonb column)
   → TurnRequested (ids only — unchanged)                           ids-only rule
   → worker loads thread → ContextBuilder reads EnabledTools
   → TurnContext.EnabledTools                                        Rule 4 (result, not param)
   → AgentFrameworkRunner attaches only the enabled tools
```

### 3.2 Persistence (no new entity)

- New value object `Chat.Domain.Chats.ValueObjects.EnabledTools` wrapping a normalized, distinct `IReadOnlySet<string>`. Factory `Create(IEnumerable<string>) : ErrorOr<EnabledTools>` validates format only (non-blank, lowercased, distinct, max count); it does **not** know which tools exist. `EnabledTools.None` is the empty default.
- New field `ChatMessage.EnabledTools` (default `None`), set when the assistant message is created. Threaded through `CreateAssistantMessage` ← `ChatThread.BeginAssistantMessage`.
- Persisted as a `jsonb` column `enabled_tools` via an EF Core value converter. One migration, ever — adding future tools needs no schema change.
- `TurnContext` gains `IReadOnlyList<string> EnabledTools`. `ContextBuilder` populates it from `assistantMessage.EnabledTools` (it already holds the message; no new `BuildAsync` parameter — Rule 4 holds).

### 3.3 Model-capability gate — what happens when the model can't search

`ModelCapabilities.SupportsToolCalling` **already exists** in the domain, so this is a read, not a schema change.

**Primary behavior — fail fast at send time.** In the `CreateChat` / `SendMessage` handlers, alongside the existing `ModelUsability.EnsureUsableAsync` check, validate the tool selection *before* persisting the turn:

- Any requested tool name not in `AgentToolNames` → `Chat.UnknownTool` (400).
- Tools requested **and** `!model.Profile.Capabilities.SupportsToolCalling` → `Chat.ModelDoesNotSupportTools` (409 Conflict).

No turn is persisted, no job published, nothing streamed. The send endpoints already declare `400` and `409` problem responses, so the client reacts cleanly — grey out / disable the web-search toggle for non-tool-capable models, or surface "this model can't search the web," exactly like ChatGPT/Claude. (Frontend behavior is out of scope; the contract enables it.)

**Backstop — the existing agent-run error contract.** If a tool ever reaches an incapable model anyway (stale catalog, provider quirk), the pipeline's existing rule applies unchanged: exceptions from the agent run → `FailAssistantMessage` + `FailedEvent` + ack (never blind-retry a half-streamed turn). No crash, no retry storm — a clean failed turn.

---

## 4. Streaming & telemetry

- **Streaming:** `TurnEventMapper` already maps `FunctionCallContent → ToolCallEvent` and `FunctionResultContent → ToolResultEvent`. The SSE "🔍 Searching the web…" affordance works with **no contract change** (Rule 6 untouched).
- **Telemetry:** extend the existing `TelemetryAgentRunner` decorator to capture a `tool_used` PostHog event (properties: tool name, model, chat id) when it observes a `ToolCallEvent` in the pass-through stream — the `tool_used` event the pipeline spec called for. Removable by deleting the same one DI registration that removes all analytics (Rule 3).

---

## 5. Wiring — what "drag-and-drop" means here

The tool runs only in the worker (where `AgentFrameworkRunner` lives), so registration goes in `AddTurnPipeline`:

```csharp
// one call adds the whole feature
services.AddWebSearchTool(configuration);
//   → AddOptions<ExaOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()
//   → AddHttpClient<IWebSearchClient, ExaWebSearchClient>().AddStandardResilienceHandler()
//   → AddScoped<IAgentTool, WebSearchTool>()     // additive — runner injects IEnumerable<IAgentTool>
```

- **Remove:** delete that one line → tool, config, and HTTP client vanish; the runner gets an empty tool set; nothing else changes.
- **Add another tool:** new `IAgentTool` class + its name in `AgentToolNames` + one `AddScoped<IAgentTool, …>()`. No migration, no runner edit, no contract change.
- **Swap providers:** new `IWebSearchClient` implementation + one DI line. `WebSearchTool` is untouched.

The API process does **not** register the tool or the Exa client — it only persists the validated tool names and reads `SupportsToolCalling`. `AgentToolNames` is a static catalog (no DI).

---

## 6. Component table

| Item | Project | Responsibility | Depends on |
|------|---------|----------------|------------|
| `IAgentTool` | Application/Abstractions/Turns | Provider-agnostic tool seam | — |
| `AgentToolNames` | Application | Known tool-name constants for validation | — |
| `IWebSearchClient`, `WebSearchResult` | Application/Abstractions/Turns | Provider-agnostic search seam | — |
| `WebSearchTool` | Application/Turns/Tools | `web_search` tool; formats results | `IWebSearchClient` |
| `WebSearchResultFormatter` | Application/Turns/Tools | Results → model-friendly string | — |
| `EnabledTools` (value object) | Domain/Chats/ValueObjects | Validated per-turn tool selection | — |
| `ChatMessage.EnabledTools` | Domain/Chats/Entities | Persisted turn state (Rule 7) | `EnabledTools` |
| `TurnContext.EnabledTools` | Application/Abstractions/Turns | Carries selection to the runner | — |
| `ContextBuilder` (modified) | Application/Turns | Populates `TurnContext.EnabledTools` | (existing) |
| `CreateChat`/`SendMessage` (modified) | Application/Chats/Commands | Accept + validate `Tools`; capability gate | `ILlmProviderRepository` |
| `ExaWebSearchClient`, `ExaOptions` | Infrastructure/Agents/Tools | Exa `/search` over resilient HttpClient | `HttpClient`, `ExaOptions` |
| `AgentFrameworkRunner` (modified) | Infrastructure/Agents | Filter by `EnabledTools`, adapt to `AIFunction` | `IEnumerable<IAgentTool>` |
| `TelemetryAgentRunner` (modified) | Infrastructure (decorator) | Emit `tool_used` | `IAnalytics` |
| `AddWebSearchTool` | Infrastructure/DependencyInjection | Single registration unit | (DI) |
| Endpoints (modified) | Api/Endpoints/Chats | Add optional `tools` to requests | — |

---

## 7. Testing strategy

TDD, mirroring the turn-pipeline plan: failing test → implement → commit per task. Targets:

- **Domain:** `EnabledTools` factory (normalize/distinct/validate); `ChatMessage`/`ChatThread` persist the selection on the assistant message.
- **Application:** `WebSearchTool` formats a fake client's results and surfaces the query/count (fake `IWebSearchClient`); `ContextBuilder` copies `EnabledTools` into `TurnContext`; `CreateChat`/`SendMessage` reject unknown tools (400) and tool-incapable models (409) and otherwise persist the selection.
- **Infrastructure:** `ExaWebSearchClient` maps a canned Exa JSON payload to `WebSearchResult` (mocked `HttpMessageHandler`); runner attaches only enabled tools (can be an Application-level test against the seam).

Per `AGENTS.md`, test scope is confirmed by the user for this work (TDD, "follow pipeline examples").

---

## 8. Out of scope / future

- **`read_url` / two-step research tool**, multi-provider search, Exa `/answer` (RAG) mode — explicitly not this pass; the seam admits them later with no contract change.
- **Frontend toggle UI** — this repo is the backend; the contract (request `tools`, `409` on incapable models) is what enables it.
- **Admin surface for `SupportsToolCalling`** — the flag already exists and is read here; managing it is a catalog concern, untouched.
- **Note (not fixed):** `TurnEventMapper` labels `ToolResultEvent.Tool` with the call id rather than the tool name; correcting it needs call-id→name correlation across streaming updates. The correct name is already on `ToolCallEvent`, so the UI is unaffected; left as a minor follow-up.

## 9. Checklist for any future tool

1. Plain class implementing `IAgentTool`, one narrow injected seam — never `DbContext` or the provider.
2. Name constant added to `AgentToolNames`.
3. One `AddScoped<IAgentTool, …>()` (plus its own client/options if external).
4. Only `AgentFrameworkRunner` adapts it to framework types.
5. No new `TurnEvent` shapes; no new `BuildAsync` parameters; toggle state through the aggregate.
