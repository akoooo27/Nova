# Read URL Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give chat agents a `read_url` tool — the model passes one absolute URL and Firecrawl scrapes it to clean markdown — mirroring the existing `web_search` tool.

**Architecture:** Per the approved spec `docs/superpowers/specs/2026-06-16-read-url-tool-design.md`. A provider-agnostic `IUrlReader` seam lives in Application; `ReadUrlTool` depends only on `IUrlReader` and returns a structured `ReadUrlResponse` (matching `WebSearchTool`/`WebSearchResponse`). `FirecrawlUrlReader` is the **only** class that imports the Firecrawl SDK. The feature is **purely additive**: the runner already attaches every registered `IAgentTool`, and streaming/telemetry are tool-agnostic, so no domain, contract, runner, migration, `TurnContext`, or telemetry change is needed.

**Tech Stack:** .NET 10, `Firecrawl` SDK 1.1.2 (tryAGI), Mediator.SourceGenerator, FastEndpoints, Microsoft.Agents.AI / Microsoft.Extensions.AI, Microsoft.Extensions.Options.

---

## ⚠️ Binding rules (inherited from the turn pipeline — check after every task)

1. **Provider quarantine.** `using Firecrawl;` appears in exactly one file: `FirecrawlUrlReader`. `IUrlReader`, `ReadPage`, and `ReadUrlResponse` are provider-agnostic and carry no SDK types.
2. **Tool quarantine.** `ReadUrlTool` is a plain class with one narrow injected seam (`IUrlReader`). Never inject `DbContext` or the service provider.
3. **`TurnEvent` is append-only.** `ToolCallEvent`/`ToolResultEvent` already exist; add no new event shapes.
4. **No domain change.** No tool state on `ChatThread`/`ChatMessage`; no migration.
5. **Runner untouched.** `AgentFrameworkRunner` already attaches all registered tools; registering `ReadUrlTool` is sufficient.

## Ground rules (project conventions)

- `Mediator.SourceGenerator` — never MediatR. FastEndpoints — never controllers. MassTransit stays pinned.
- Match surrounding style: `internal sealed` for infrastructure adapters, `public sealed` for the tool (Infrastructure DI must see it — same as `WebSearchTool`), named arguments, records for DTOs, `#pragma warning disable CA1031` around the catch-all (as `WebSearchTool` does).
- **Per `AGENTS.md`, this plan schedules no automated tests** (no explicit test approval). Verification is build + manual smoke only. See "Deferred until explicit approval".
- **Per `AGENTS.md`, ask for elevated permission before running any `dotnet` command.**
- Run all commands from repo root `/Users/akakijomidava/RiderProjects/Nova`.

## File Structure Overview

```
Directory.Packages.props                                            (Task 1: add Firecrawl 1.1.2)
src/services/Chat/
  Chat.Infrastructure/Chat.Infrastructure.csproj                    (Task 1: reference Firecrawl)
  Chat.Application/
    Abstractions/WebRead/IUrlReader.cs                              (Task 2: create — seam + records)
    Turns/Tools/AgentToolNames.cs                                   (Task 3: add read_url)
    Turns/Tools/ReadUrlTool.cs                                      (Task 3: create)
  Chat.Infrastructure/
    WebRead/FirecrawlUrlReader.cs                                   (Task 4: create — SDK quarantine)
    Options/FirecrawlOptions.cs                                     (Task 5: create)
    DependencyInjection.cs                                          (Task 5: wire AddTurnPipeline)
  Chat.TurnWorker/appsettings.json                                  (Task 5: Firecrawl section)
```

---

## Task 1: Add the Firecrawl SDK package

The SDK targets net10.0 with zero dependencies. Versions are centrally managed.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`

- [ ] **Step 1: Add the central package version.** In `Directory.Packages.props`, add this line inside the `<ItemGroup>`, next to the other Infrastructure agent packages (after the `Microsoft.Agents.AI.OpenAI` line):

```xml
    <PackageVersion Include="Firecrawl" Version="1.1.2" />
```

- [ ] **Step 2: Reference it from Chat.Infrastructure.** In `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`, add to the first `<ItemGroup>` (after the `Microsoft.Agents.AI.OpenAI` reference):

```xml
    <PackageReference Include="Firecrawl" />
```

- [ ] **Step 3: Restore + build to confirm the package resolves** (ask for elevated permission first per `AGENTS.md`)

Run: `dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
Expected: Build succeeded (package restored from NuGet).

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj
git commit -m "chore(chat): add Firecrawl SDK package reference"
```

---

## Task 2: Add the `IUrlReader` seam

Provider-agnostic seam + result records, mirroring `Abstractions/WebSearch/IWebSearchClient.cs` (interface + records in one file).

**Files:**
- Create: `src/services/Chat/Chat.Application/Abstractions/WebRead/IUrlReader.cs`

- [ ] **Step 1: Create `IUrlReader.cs`**

```csharp
namespace Chat.Application.Abstractions.WebRead;

public interface IUrlReader
{
    Task<ReadPage> ReadAsync(string url, CancellationToken cancellationToken);
}

public sealed record ReadPage(string Url, string? Title, string Markdown);

public sealed record ReadUrlResponse(bool Available, ReadPage? Page, string? Note);
```

- [ ] **Step 2: Build Chat.Application** (ask for elevated permission first per `AGENTS.md`)

Run: `dotnet build src/services/Chat/Chat.Application/Chat.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/services/Chat/Chat.Application/Abstractions/WebRead/IUrlReader.cs
git commit -m "feat(chat): add url reader seam for the read_url tool"
```

---

## Task 3: Add the `ReadUrlTool` and register its name

`ReadUrlTool` depends only on `IUrlReader`, returns `ReadUrlResponse`, validates the URL scheme, and fails gracefully (`Available: false` + `Note`) — identical pattern to `WebSearchTool`.

**Files:**
- Modify: `src/services/Chat/Chat.Application/Turns/Tools/AgentToolNames.cs`
- Create: `src/services/Chat/Chat.Application/Turns/Tools/ReadUrlTool.cs`

- [ ] **Step 1: Add `read_url` to `AgentToolNames`.** Replace the contents of `AgentToolNames.cs` with:

```csharp
namespace Chat.Application.Turns.Tools;

public static class AgentToolNames
{
    public const string WebSearch = "web_search";

    public const string ReadUrl = "read_url";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { WebSearch, ReadUrl };

    public static bool IsKnown(string name) => Known.Contains(name);
}
```

- [ ] **Step 2: Create `ReadUrlTool.cs`**

```csharp
using System.ComponentModel;

using Chat.Application.Abstractions.Turns;
using Chat.Application.Abstractions.WebRead;

namespace Chat.Application.Turns.Tools;

public sealed class ReadUrlTool(IUrlReader reader) : IAgentTool
{
    private const string UnavailableMessage =
        "That page could not be read. Tell the user you couldn't open the URL and continue with what you know.";

    public string Name => AgentToolNames.ReadUrl;

    public Delegate CreateInvocation() => ReadAsync;

    [Description("Fetch the full readable content of a specific web page as markdown. " +
                 "Use to read a URL the user gave you, or a result returned by web_search.")]
    private async Task<ReadUrlResponse> ReadAsync
    (
        [Description("Absolute http(s) URL of the page to read.")] string url,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsHttpUrl(url))
        {
            return new ReadUrlResponse
            (
                Available: false,
                Page: null,
                Note: "The URL must be an absolute http or https address."
            );
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
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
            return new ReadUrlResponse(Available: false, Page: null, Note: UnavailableMessage);
        }
    }

    private static bool IsHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
```

- [ ] **Step 3: Build Chat.Application** (ask for elevated permission first per `AGENTS.md`)

Run: `dotnet build src/services/Chat/Chat.Application/Chat.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Application/Turns/Tools/AgentToolNames.cs src/services/Chat/Chat.Application/Turns/Tools/ReadUrlTool.cs
git commit -m "feat(chat): add read_url agent tool"
```

---

## Task 4: Add the Firecrawl adapter (SDK quarantine)

`FirecrawlUrlReader` is the only file that imports the Firecrawl SDK. It scrapes a single URL, trims oversized markdown, and maps the result to `ReadPage`.

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/WebRead/FirecrawlUrlReader.cs`

- [ ] **Step 1: Create `FirecrawlUrlReader.cs`**

```csharp
using Chat.Application.Abstractions.WebRead;

using Firecrawl;

namespace Chat.Infrastructure.WebRead;

internal sealed class FirecrawlUrlReader(FirecrawlClient client) : IUrlReader
{
    private const int MaxContentLength = 50_000;

    public async Task<ReadPage> ReadAsync(string url, CancellationToken cancellationToken)
    {
        var response = await client.Scraping.ScrapeAndExtractFromUrlAsync
        (
            url,
            cancellationToken: cancellationToken
        );

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

- [ ] **Step 2: Verify the SDK surface against the installed 1.1.2 package.** Before building, confirm the exact namespace, client type, and member names. They are expected to be: root namespace `Firecrawl`; client `FirecrawlClient`; `client.Scraping.ScrapeAndExtractFromUrlAsync(url, cancellationToken:)`; result `response.Data.Markdown`, `response.Data.Metadata.SourceURL`, `response.Data.Metadata.Title`. If 1.1.2 differs (method name, a request-object parameter instead of a bare string, or the cancellation-token shape), adjust this single file to match — the `IUrlReader` contract and `ReadPage` mapping stay the same. `var response` is used deliberately so the exact SDK response type name need not be hard-coded.

- [ ] **Step 3: Build Chat.Infrastructure** (ask for elevated permission first per `AGENTS.md`)

Run: `dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/WebRead/FirecrawlUrlReader.cs
git commit -m "feat(chat): add Firecrawl url reader"
```

---

## Task 5: Options + wire the tool into the turn pipeline

Add `FirecrawlOptions` (mirrors `ExaOptions`), register the SDK client + reader + tool in `AddTurnPipeline`, and add the worker config section. This is the single drag-and-drop registration block.

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Options/FirecrawlOptions.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Modify: `src/services/Chat/Chat.TurnWorker/appsettings.json`

- [ ] **Step 1: Create `FirecrawlOptions.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Options;

public sealed class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    public Uri BaseUrl { get; init; } = new("https://api.firecrawl.dev");
}
```

- [ ] **Step 2: Add the three usings to `DependencyInjection.cs`.** Add to the existing `using` block (alphabetically among the `Chat.*` / external usings):

```csharp
using Chat.Application.Abstractions.WebRead;
using Chat.Infrastructure.WebRead;

using Firecrawl;
```

(`Chat.Application.Turns.Tools`, `Chat.Infrastructure.Options`, `Microsoft.Extensions.Options`, and `Microsoft.Extensions.DependencyInjection` are already imported.)

- [ ] **Step 3: Register the read_url tool inside `AddTurnPipeline`.** Immediately after the existing Exa `AddHttpClient<IWebSearchClient, ExaWebSearchClient>(...).AddStandardResilienceHandler();` block and before the `// Decorator stack (spec Rule 3)` comment, add:

```csharp
        // read_url tool (Firecrawl). Delete this block to remove the tool entirely.
        services
            .AddOptions<FirecrawlOptions>()
            .Bind(configuration.GetSection(FirecrawlOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(serviceProvider =>
        {
            FirecrawlOptions options = serviceProvider.GetRequiredService<IOptions<FirecrawlOptions>>().Value;

            return new FirecrawlClient(options.ApiKey);
        });

        services.AddScoped<IUrlReader, FirecrawlUrlReader>();
        services.AddScoped<IAgentTool, ReadUrlTool>();
```

Notes:
- `FirecrawlClient` is `IDisposable` and wraps an `HttpClient`, so **singleton** is the correct lifetime (the DI container disposes it on shutdown). `ReadUrlTool` and `FirecrawlUrlReader` are scoped, matching `WebSearchTool`.
- If the verification in Task 4 found that 1.1.2 exposes a base-URL constructor/property or accepts an injected `HttpClient`, set `BaseUrl` from `options.BaseUrl` here (and, if an `HttpClient` can be injected, prefer routing it through `AddHttpClient(...).AddStandardResilienceHandler()` to match the resilience `ExaWebSearchClient` gets). Otherwise the default cloud endpoint and the SDK's own transport are used.

- [ ] **Step 4: Add the `Firecrawl` config section.** Replace `Chat.TurnWorker/appsettings.json` with:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Agent": {
    "BaseUrl": "https://openrouter.ai/api/v1"
  },
  "Firecrawl": {
    "BaseUrl": "https://api.firecrawl.dev"
  }
}
```

`Firecrawl:ApiKey` is a secret — supply it via `Firecrawl__ApiKey` (environment / user-secrets), exactly like `Exa__ApiKey`. Registering the tool makes the key required at worker startup (`ValidateOnStart`); deleting the block in Step 3 removes that requirement — the drag-and-drop contract.

- [ ] **Step 5: Build the whole solution** (ask for elevated permission first per `AGENTS.md`)

Run: `dotnet build Nova.slnx`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Options/FirecrawlOptions.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs src/services/Chat/Chat.TurnWorker/appsettings.json
git commit -m "feat(chat): wire the read_url tool into the turn pipeline"
```

---

## Task 6: Verification

- [ ] **Step 1: Build the whole solution** (ask for elevated permission first per `AGENTS.md`)

Run: `dotnet build Nova.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Confirm the provider quarantine holds — the SDK is imported in exactly one file**

Run: `grep -rn "using Firecrawl" src/services/Chat --include="*.cs"`
Expected: a single match, in `Chat.Infrastructure/WebRead/FirecrawlUrlReader.cs`.

- [ ] **Step 3: Confirm no framework/domain leak**

Run: `grep -rn "Firecrawl" src/services/Chat/Chat.Application src/services/Chat/Chat.Domain --include="*.cs"`
Expected: no output (Application/Domain stay provider-agnostic).

- [ ] **Step 4: Manual smoke (requires a real Firecrawl key + running infra).** Set `Firecrawl__ApiKey`, run the AppHost, create a chat with a tool-capable model, and ask the model to read a specific URL (e.g. "Read https://example.com and summarize it"). Confirm the SSE stream emits a `tool_call` event for `read_url`, followed by a `tool_result` and grounded tokens. Record the result in the PR description.

- [ ] **Step 5: Final commit if anything was adjusted**

```bash
git add -A
git commit -m "feat(chat): verify the read_url tool end to end"
```

---

## Deferred until explicit approval

Per `AGENTS.md`, ask the user before adding or modifying tests. If approved, add focused coverage:

- **`ReadUrlTool`** (Chat.Application.Tests): rejects a non-http(s) URL with `Available: false`; relays a fake `IUrlReader`'s `ReadPage` with `Available: true`; returns the graceful unavailable message when the fake reader throws. Add a `FakeUrlReader` test double next to the existing `FakeWebSearchClient`.
- **`AgentToolNames`** (Chat.Application.Tests): `IsKnown("read_url")` is true; `ReadUrl == "read_url"`.
- **`FirecrawlUrlReader`** (Chat.Infrastructure.Tests): maps a canned Firecrawl scrape payload to `ReadPage` and truncates markdown beyond `MaxContentLength`, against a stubbed transport / `HttpMessageHandler`. This requires creating a `Chat.Infrastructure.Tests` project — the solution currently has only `Chat.Domain.Tests` and `Chat.Application.Tests` — and registering it in `Nova.slnx` (plus `InternalsVisibleTo` so the test can see `internal FirecrawlUrlReader`).

---

## Self-Review

- **Spec coverage:** seam `IUrlReader` (T2) ✓; `ReadUrlTool` + name (T3) ✓; `FirecrawlUrlReader` SDK quarantine + truncation (T4) ✓; `FirecrawlOptions` + drag-and-drop DI + appsettings (T5) ✓; "always-available auto tool, no runner change" — realized by registering `IAgentTool` only, no runner task ✓; streaming/telemetry zero-change — verified in spec §5, no task needed ✓; security scheme-validation (T3) ✓; tests deferred per `AGENTS.md` ✓.
- **Placeholder scan:** none — every step has concrete file content or an exact command + expected output. The Task 4 verification step is an explicit, bounded API check (house convention), not a placeholder.
- **Type consistency:** `IUrlReader.ReadAsync(string, CancellationToken) : Task<ReadPage>` is implemented by `FirecrawlUrlReader` and consumed by `ReadUrlTool`; `ReadPage(Url, Title, Markdown)` and `ReadUrlResponse(Available, Page, Note)` are used identically across T2/T3/T4; `AgentToolNames.ReadUrl == "read_url"` is the `ReadUrlTool.Name`; DI registers `IUrlReader → FirecrawlUrlReader`, `FirecrawlClient` (singleton), and `IAgentTool → ReadUrlTool`, matching every constructor dependency.
- **Green-at-every-commit:** T1 (package) builds; T2 (seam) builds; T3 (tool) builds against the T2 seam; T4 (adapter) builds against the SDK + seam; T5 wires everything and builds the solution. Each task compiles independently.
