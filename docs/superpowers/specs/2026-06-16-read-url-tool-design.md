# Read URL Tool — Design Spec

**Date:** 2026-06-16
**Status:** Approved design, pre-implementation

**Goal:** Give chat agents a `read_url` tool. The model passes one absolute URL; we scrape that page with [Firecrawl](https://firecrawl.dev) and return its clean markdown. The model calls the tool repeatedly to read several pages in a turn (`web_search` → `read_url` → `read_url`), exactly the **search-then-read** pattern shipped by Claude (`web_fetch`), Gemini (URL context), and ChatGPT ("open URL"). The tool is **drag-and-drop**: one registration adds it, deleting that registration removes it, and swapping Firecrawl for another reader is a one-class change.

**Distinct from crawling.** "Give the model a URL" means *read that one page* — not crawl the site. Whole-site traversal is a long-running, asynchronous, potentially huge job; the frontier labs put it in a separate **Deep-Research mode**, never in a blocking in-chat tool call. This spec ships the read primitive only and explicitly defers site-crawl (see §10).

**Builds on:** `docs/superpowers/specs/2026-06-12-chat-turn-pipeline-design.md` (turn pipeline) and `docs/superpowers/specs/2026-06-13-exa-web-search-tool-design.md` (the `web_search` tool whose shape this mirrors). The Exa spec foreshadowed this exact tool in its out-of-scope list (`read_url`).

---

## 1. Binding rules inherited from the turn pipeline

These constrain every decision below; violating one recreates the coupling the pipeline design exists to prevent.

1. **Rule 1 — Framework/provider quarantine.** `Microsoft.Agents.AI` / `Microsoft.Extensions.AI` live only in `Chat.Infrastructure/Agents/`. By the same philosophy, the **Firecrawl SDK appears in exactly one class** (`FirecrawlUrlReader`); the seam exposed to the rest of the system is provider-agnostic.
2. **Rule 3 — Cross-cutting = decorator + one DI registration.** Telemetry rides the existing `TelemetryAgentRunner` decorator; no edits to the hot path.
3. **Rule 5 — Tool quarantine.** Each tool is a plain class with a narrow, constructor-injected dependency set, registered in one place. `AgentFrameworkRunner` is the only code that adapts a tool to framework types. **Tools never receive `DbContext` or the service provider** — only the one seam they need.
4. **Rule 6 — `TurnEvent` is append-only.** `ToolCallEvent` / `ToolResultEvent` already exist; we add no new event shapes.
5. **Ids-only job rule.** `TurnRequested` carries ids (+ existing flags) only; unchanged here.
6. **Rule 4 — `IContextBuilder` is not a junk drawer.** Unchanged — `read_url` needs nothing from context assembly.

---

## 2. Architecture

### 2.1 The tool seam (`IAgentTool`) — already exists

`read_url` plugs into the existing provider-agnostic seam in Application; no change to it.

```csharp
public interface IAgentTool
{
    string Name { get; }
    Delegate CreateInvocation();
}
```

`AgentFrameworkRunner` is the sole adapter from `CreateInvocation()` to `Microsoft.Extensions.AI.AIFunction`.

### 2.2 The read tool (`ReadUrlTool`) — Application

Lives in Application, depends on **one** narrow seam (`IUrlReader`), fully unit-testable with a fake — no HTTP, no SDK. Provider-agnostic; "Firecrawl" appears nowhere here. Returns a **structured response record** (matching the current `WebSearchTool`, which returns `WebSearchResponse`); the framework serializes it for the model.

```csharp
// Chat.Application/Turns/Tools/ReadUrlTool.cs
public sealed class ReadUrlTool(IUrlReader reader) : IAgentTool
{
    private const string UnavailableMessage =
        "That page could not be read. Tell the user you couldn't open the URL and continue with what you know.";

    public string Name => AgentToolNames.ReadUrl; // "read_url"

    public Delegate CreateInvocation() => ReadAsync;

    [Description("Fetch the full readable content of a specific web page as markdown. " +
                 "Use to read a URL the user gave you, or a result returned by web_search.")]
    private async Task<ReadUrlResponse> ReadAsync(
        [Description("Absolute http(s) URL of the page to read.")] string url,
        CancellationToken cancellationToken = default)
    {
        if (!IsHttpUrl(url))
        {
            return new ReadUrlResponse(Available: false, Page: null,
                Note: "The URL must be an absolute http or https address.");
        }

        try
        {
            ReadPage page = await reader.ReadAsync(url, cancellationToken);
            return new ReadUrlResponse(Available: true, Page: page, Note: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new ReadUrlResponse(Available: false, Page: null, Note: UnavailableMessage);
        }
    }

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
```

Failure is **graceful** (`Available: false` + `Note`) — a bad URL or a Firecrawl hiccup never crashes the turn, identical to `WebSearchTool`. `AgentToolNames` gains `ReadUrl = "read_url"` (and adds it to the `Known` set for catalog completeness). The tool's `Name` returns this constant; the current request contract exposes no tools array (only `forceUseSearch`), so the constant is for consistency with `web_search`, not request validation.

### 2.3 Provider seam + Firecrawl adapter — Infrastructure

```csharp
// Chat.Application/Abstractions/WebRead/IUrlReader.cs
public interface IUrlReader
{
    Task<ReadPage> ReadAsync(string url, CancellationToken cancellationToken);
}

public sealed record ReadPage(string Url, string? Title, string Markdown);

public sealed record ReadUrlResponse(bool Available, ReadPage? Page, string? Note);
```

```csharp
// Chat.Infrastructure/WebRead/FirecrawlUrlReader.cs
internal sealed class FirecrawlUrlReader(FirecrawlClient client) : IUrlReader
{
    private const int MaxContentLength = 50_000; // trim very large pages before they reach the model

    public async Task<ReadPage> ReadAsync(string url, CancellationToken cancellationToken)
    {
        var response = await client.Scraping.ScrapeAndExtractFromUrlAsync(url, cancellationToken: cancellationToken);

        string markdown = response.Data?.Markdown ?? string.Empty;
        if (markdown.Length > MaxContentLength)
        {
            markdown = markdown[..MaxContentLength];
        }

        return new ReadPage
        (
            Url: response.Data?.Metadata?.SourceURL ?? url,
            Title: response.Data?.Metadata?.Title,
            Markdown: markdown
        );
    }
}
```

- **SDK:** `Firecrawl` (tryAGI), generated from Firecrawl's official OpenAPI spec. Latest **1.1.2**, **targets net10.0**, **zero dependencies** — a native fit for this solution. This is the only `using Firecrawl;` in the codebase (the provider quarantine).
- **Single-page scrape:** `client.Scraping.ScrapeAndExtractFromUrlAsync(url)` → `Data.Markdown`, `Data.Metadata.SourceURL` / `Data.Metadata.Title`.
- **Truncation:** the adapter caps markdown at `MaxContentLength` (mirrors how `ExaWebSearchClient` clamps result count internally) — the provider owns "how much content we return."
- **Exact SDK member names** (`Data`, `Markdown`, `Metadata.*`, the cancellation-token parameter) are verified against the installed 1.1.2 API at implementation time, per the house rule used in prior tool plans.

### 2.4 Runner — no change

`AgentFrameworkRunner` already injects `IEnumerable<IAgentTool>` and attaches **all** registered tools every turn, varying only `ChatToolMode` (`Auto`, or `RequireSpecific(web_search)` when `ForceUseSearch`). Registering `ReadUrlTool` is therefore sufficient — it is attached automatically and the model may call it in any normal (`Auto`) turn. **No edit to the runner, `TurnContext`, or `TurnGenerationOptions`.** During a forced-search turn, `web_search` remains required and `read_url` remains available for follow-up tool steps.

---

## 3. What changes — and what deliberately does not

Because the runner attaches all registered tools and the stream/telemetry are tool-agnostic, this feature is **purely additive**.

**Unchanged (no edit):** API request contracts, `CreateChat`/`SendMessage` commands + validators, the domain (`ChatThread`, `ChatMessage`), `TurnRequested`, `TurnContext`, `TurnGenerationOptions`, `ContextBuilder`, `ChatTurnOrchestrator`, `AgentFrameworkRunner`, `TelemetryAgentRunner`, `TurnEventMapper`. **No EF migration.**

**New / modified:**
- New: `IUrlReader` + `ReadPage` + `ReadUrlResponse` (Application/Abstractions/WebRead).
- New: `ReadUrlTool` (Application/Turns/Tools).
- Modified: `AgentToolNames` (add `read_url`).
- New: `FirecrawlUrlReader` (Infrastructure/WebRead), `FirecrawlOptions` (Infrastructure/Options).
- Modified: `DependencyInjection.AddTurnPipeline` (one `AddReadUrlTool` call), `Directory.Packages.props` + `Chat.Infrastructure.csproj` (the `Firecrawl` package), `Chat.TurnWorker/appsettings.json` (a `Firecrawl` section).

---

## 4. Tool availability

The force-use-search plan recorded a deliberate caution: *"do not let 'registered in DI' automatically mean 'available to every chat turn.' Add an explicit selection policy so safe/default tools can remain auto-available while sensitive tools require a specific option or permission."*

**Decision:** `read_url` is a **safe default** — read-only, no side effects, bounded output, and SSRF-safe (the fetch runs on Firecrawl's servers, not ours). It therefore stays an **always-available, model-decided (`Auto`) tool**, like `web_search` today, with **no runner change**.

The explicit `SelectTools(generationOptions)` availability policy that plan foreshadowed is **deferred** until the first genuinely sensitive/expensive tool arrives — the asynchronous site-crawl (§10) — which is when gating earns its complexity. The seam admits it then with no rework here.

---

## 5. Streaming & telemetry — zero change (verified)

- **Streaming:** `TurnEventMapper` maps `FunctionCallContent → ToolCallEvent` and `FunctionResultContent → ToolResultEvent` by name/call-id generically. `read_url`'s call and result stream over SSE with no contract change (Rule 6 intact). An SSE "📄 Reading page…" affordance works with no backend work.
- **Telemetry:** `TelemetryAgentRunner` collects every `ToolCallEvent.Tool` into `tools_used` and emits the `tool_used` PostHog event for **any** tool. `read_url` is captured automatically; removable by deleting the same one DI registration that removes all analytics (Rule 3).

---

## 6. Security

- **SSRF** is mitigated because Firecrawl fetches the URL server-side; the worker never makes the outbound request to a model/user-supplied address.
- The tool performs cheap **scheme validation** (absolute http/https only) before calling the reader.
- **Domain allow/blocklists** (which the labs layer on top) are noted as future hardening, not this pass.

---

## 7. Wiring — what "drag-and-drop" means here

The tool runs only in the worker (where `AgentFrameworkRunner` lives), so registration goes in `AddTurnPipeline`:

```csharp
// FirecrawlOptions — Chat.Infrastructure/Options/FirecrawlOptions.cs
public sealed class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    [Required] public string ApiKey { get; init; } = string.Empty;
    public Uri BaseUrl { get; init; } = new("https://api.firecrawl.dev");
}
```

```csharp
// one call adds the whole feature, inside AddTurnPipeline
services.AddReadUrlTool(configuration);
//   → AddOptions<FirecrawlOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()
//   → AddSingleton(FirecrawlClient) built from options   // IDisposable + HttpClient-backed → singleton, like a typed HttpClient
//   → AddScoped<IAgentTool, ReadUrlTool>()               // additive — runner injects IEnumerable<IAgentTool>
```

- The `FirecrawlClient` lifetime is **singleton** (it wraps and reuses an `HttpClient`). `BaseUrl` is applied to the client where the 1.1.2 API exposes it (constructor/property), otherwise via the SDK's `FIRECRAWL_BASE_URL` convention — confirmed at implementation. If 1.1.2 supports injecting an `HttpClient`, route it through `AddHttpClient(...).AddStandardResilienceHandler()` to match the resilience `ExaWebSearchClient` gets; otherwise rely on the SDK's own transport.
- **Remove:** delete the one `AddReadUrlTool` line → tool, SDK client, and config requirement vanish; the runner gets one fewer tool; nothing else changes.
- **Swap providers:** new `IUrlReader` implementation + one DI line. `ReadUrlTool` is untouched.
- `Directory.Packages.props` gains `<PackageVersion Include="Firecrawl" Version="1.1.2" />`; `Chat.Infrastructure.csproj` references it. Worker `appsettings.json` gets `"Firecrawl": { "BaseUrl": "https://api.firecrawl.dev" }`; `Firecrawl:ApiKey` is a secret supplied via `Firecrawl__ApiKey` (env/user-secrets), exactly like `Exa__ApiKey`.

The API process does not register the tool or the Firecrawl client.

---

## 8. Component table

| Item | Project | Responsibility | Depends on |
|------|---------|----------------|------------|
| `IUrlReader`, `ReadPage`, `ReadUrlResponse` | Application/Abstractions/WebRead | Provider-agnostic read seam + records | — |
| `ReadUrlTool` | Application/Turns/Tools | `read_url` tool; URL validation; graceful failure | `IUrlReader` |
| `AgentToolNames` (modified) | Application/Turns/Tools | Add `ReadUrl = "read_url"` | — |
| `FirecrawlUrlReader` | Infrastructure/WebRead | Firecrawl scrape → `ReadPage`; trims content | `FirecrawlClient` |
| `FirecrawlOptions` | Infrastructure/Options | `Firecrawl` config section | — |
| `AddReadUrlTool` | Infrastructure/DependencyInjection | Single registration unit | (DI) |
| `Firecrawl` 1.1.2 | Directory.Packages.props + csproj | The SDK | — |

---

## 9. Testing strategy

Per `AGENTS.md`, test work is not scheduled without explicit user approval. If approved, TDD targets mirror the Exa work:

- **Application:** `ReadUrlTool` validates URLs (rejects non-http(s) with `Available:false`), relays a fake `IUrlReader`'s page, and returns a graceful `Available:false` when the reader throws; `AgentToolNames` knows `read_url`.
- **Infrastructure:** `FirecrawlUrlReader` maps a canned scrape payload to `ReadPage` and truncates oversized markdown (against a stubbed SDK transport / `HttpMessageHandler`). This requires a `Chat.Infrastructure.Tests` project, created if not already present — the current solution has only `Chat.Domain.Tests` and `Chat.Application.Tests`.

---

## 10. Out of scope / future

- **Whole-site crawl / Deep-Research async mode** (Firecrawl `/crawl` + `/map`) — the honest home for "crawl a website": a bounded, progress-streamed job on the existing turn pipeline, gated by the deferred `SelectTools` policy. Not this pass.
- **Batch / multi-URL reads**, LLM `extract`, screenshots — the `IUrlReader` seam admits them later.
- **Domain allow/blocklists**, per-user rate limiting — future hardening.
- **Frontend** — backend only; the structured `ReadUrlResponse` and existing tool-call SSE events are what enable any UI affordance.

---

## 11. Checklist for any future tool (reaffirmed)

1. Plain class implementing `IAgentTool`, one narrow injected seam — never `DbContext` or the provider.
2. Name constant added to `AgentToolNames`.
3. One `AddScoped<IAgentTool, …>()` (plus its own client/options if external).
4. Only `AgentFrameworkRunner` adapts it to framework types; the provider SDK/HTTP stays in one adapter class.
5. No new `TurnEvent` shapes; no new `BuildAsync` parameters.
6. Before exposing a **sensitive or expensive** tool to every turn, introduce the explicit `SelectTools` availability policy (deferred from §4).
