# Exa Web Search Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give chat agents a drag-and-drop `web_search` tool backed by Exa, toggled per message by the user and persisted on the turn so the worker honors it without trusting the job payload.

**Architecture:** Per the approved spec `docs/superpowers/specs/2026-06-13-exa-web-search-tool-design.md`. A provider-agnostic `IAgentTool` seam lives in Application; `WebSearchTool` depends only on `IWebSearchClient`; `AgentFrameworkRunner` is the sole code that adapts tools to `Microsoft.Extensions.AI.AIFunction`. The per-turn selection is an `EnabledTools` value object stored on the assistant `ChatMessage` (jsonb). A fail-fast capability gate at send time uses the existing `ModelCapabilities.SupportsToolCalling`.

**Tech Stack:** .NET 10, Mediator.SourceGenerator, FastEndpoints, EF Core + Npgsql (jsonb), Microsoft.Agents.AI / Microsoft.Extensions.AI (1.10.0), Microsoft.Extensions.Http.Resilience, ErrorOr, FluentValidation, xunit.

---

## ⚠️ Binding rules (inherited from the turn pipeline — check after every task)

1. **Agent Framework types appear ONLY in `src/services/Chat/Chat.Infrastructure/Agents/`.** If you type `using Microsoft.Agents.AI` or `using Microsoft.Extensions.AI` anywhere else — stop. `IAgentTool` and `IWebSearchClient` are provider-agnostic.
2. **Each tool is a plain class with one narrow injected seam** (`WebSearchTool` ← `IWebSearchClient`). Never inject `DbContext` or the service provider.
3. **`TurnRequested` stays ids-only.** The tool selection is loaded from the DB by the worker, never carried in the job.
4. **State transitions go through the `ChatThread` aggregate.** `EnabledTools` is set via `BeginAssistantMessage`, never via SQL.
5. **`TurnEvent` is append-only.** `ToolCallEvent`/`ToolResultEvent` already exist; add no new event shapes.
6. **`IContextBuilder.BuildAsync` gets no new parameter.** We enrich the `TurnContext` *result* only.

## Ground rules (project conventions)

- `Mediator.SourceGenerator` — never MediatR. FastEndpoints — never controllers. MassTransit stays at 8.4.1.
- TDD throughout (user-approved): failing test → run red → implement → run green → commit. Build must pass before every commit.
- Value-object factory methods return `ErrorOr<T>`. Follow surrounding style (named arguments, `internal sealed` handlers, expression-bodied where the codebase does it).
- Run all commands from repo root `/Users/akakijomidava/RiderProjects/Nova`.
- `TelemetryAgentRunner` **already emits `tool_used`** (verified) — no telemetry task is needed.

## File Structure Overview

```
src/services/Chat/
  Chat.Domain/Chats/
    ValueObjects/EnabledTools.cs                         (Task 1: create)
    Entities/ChatMessage.cs                              (Task 2: add EnabledTools)
    ChatThread.cs                                        (Task 2: BeginAssistantMessage param)
  Chat.Infrastructure/Chats/Configurations/
    ChatMessageConfiguration.cs                          (Task 3: jsonb mapping)
  Chat.Infrastructure/Database/Migrations/*              (Task 3: generated)
  Chat.Application/
    Abstractions/Turns/IAgentTool.cs                     (Task 4: create)
    Abstractions/Turns/IWebSearchClient.cs               (Task 5: create)
    Turns/Tools/AgentToolNames.cs                        (Task 4: create)
    Turns/Tools/WebSearchResultFormatter.cs              (Task 5: create)
    Turns/Tools/WebSearchTool.cs                         (Task 5: create)
    Abstractions/Turns/TurnContext.cs                    (Task 6: add EnabledTools)
    Turns/ContextBuilder.cs                              (Task 6: populate)
    Chats/Errors/ChatOperationErrors.cs                  (Task 7: 2 errors)
    Chats/ModelUsability.cs                              (Task 7: tool gate)
    Chats/Commands/{CreateChat,SendMessage}/*            (Task 7: Tools field)
  Chat.Api/Endpoints/Chats/{CreateChat,SendMessage}/Endpoint.cs  (Task 8)
  Chat.Infrastructure/Agents/Tools/ExaOptions.cs         (Task 9: create)
  Chat.Infrastructure/Agents/Tools/ExaWebSearchClient.cs (Task 9: create)
  Chat.Infrastructure/Agents/AgentFrameworkRunner.cs     (Task 10: attach tools)
  Chat.Infrastructure/DependencyInjection.cs             (Task 10: AddWebSearchTool)
  Chat.TurnWorker/appsettings.json                       (Task 10: Exa section)
tests/Chat/
  Chat.Domain.Tests/Chats/EnabledToolsTests.cs           (Task 1)
  Chat.Domain.Tests/Chats/ChatMessageEnabledToolsTests.cs(Task 2)
  Chat.Application.Tests/Turns/AgentToolNamesTests.cs     (Task 4)
  Chat.Application.Tests/Turns/WebSearchToolTests.cs      (Task 5)
  Chat.Application.Tests/Turns/{ContextBuilder,CreateChatHandler,SendMessageHandler}Tests.cs (Tasks 6,7)
  Chat.Application.Tests/ModelCatalog/TestCatalogFactory.cs (Task 7: supportsToolCalling param)
  Chat.Infrastructure.Tests/ (NEW project) ExaWebSearchClientTests.cs (Task 9)
Nova.slnx                                                 (Task 9: add test project)
```

---

## Task 1: `EnabledTools` value object

The validated per-turn tool selection. Normalizes (trim + lowercase), de-duplicates, sorts (stable equality), caps count.

**Files:**
- Create: `src/services/Chat/Chat.Domain/Chats/ValueObjects/EnabledTools.cs`
- Test: `tests/Chat/Chat.Domain.Tests/Chats/EnabledToolsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Chat.Domain.Chats.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.Chats;

public sealed class EnabledToolsTests
{
    [Fact]
    public void Create_WhenNull_ReturnsNone()
    {
        ErrorOr<EnabledTools> result = EnabledTools.Create(null);

        Assert.False(result.IsError);
        Assert.True(result.Value.IsEmpty);
    }

    [Fact]
    public void Create_NormalizesTrimsLowercasesAndDeduplicates()
    {
        ErrorOr<EnabledTools> result = EnabledTools.Create(["Web_Search", " web_search "]);

        Assert.False(result.IsError);
        Assert.Equal(new[] { "web_search" }, result.Value.Names);
    }

    [Fact]
    public void Create_WhenNameBlank_ReturnsValidationError()
    {
        ErrorOr<EnabledTools> result = EnabledTools.Create(["  "]);

        Assert.True(result.IsError);
        Assert.Equal("EnabledTools.BlankName", result.FirstError.Code);
    }

    [Fact]
    public void Create_WhenTooMany_ReturnsValidationError()
    {
        string[] many = Enumerable.Range(0, EnabledTools.MaxCount + 1).Select(i => $"tool_{i}").ToArray();

        ErrorOr<EnabledTools> result = EnabledTools.Create(many);

        Assert.True(result.IsError);
        Assert.Equal("EnabledTools.TooMany", result.FirstError.Code);
    }

    [Fact]
    public void Equality_IsStructuralOverNames()
    {
        EnabledTools a = EnabledTools.Create(["web_search"]).Value;
        EnabledTools b = EnabledTools.Create(["web_search"]).Value;

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
```

- [ ] **Step 2: Run it — expect FAIL (does not compile, `EnabledTools` missing)**

Run: `dotnet test tests/Chat/Chat.Domain.Tests --filter "FullyQualifiedName~EnabledToolsTests"`
Expected: build error — `EnabledTools` not found.

- [ ] **Step 3: Create `EnabledTools.cs`**

```csharp
using ErrorOr;

namespace Chat.Domain.Chats.ValueObjects;

public sealed class EnabledTools
{
    public const int MaxCount = 8;

    private readonly string[] _names;

    private EnabledTools(string[] names) => _names = names;

    public IReadOnlyList<string> Names => _names;

    public bool IsEmpty => _names.Length == 0;

    public static EnabledTools None { get; } = new([]);

    public static ErrorOr<EnabledTools> Create(IEnumerable<string>? names)
    {
        if (names is null)
        {
            return None;
        }

        List<string> normalized = [];

        foreach (string name in names)
        {
            string trimmed = name?.Trim().ToLowerInvariant() ?? string.Empty;

            if (trimmed.Length == 0)
            {
                return Error.Validation
                (
                    code: "EnabledTools.BlankName",
                    description: "Tool names cannot be blank."
                );
            }

            if (!normalized.Contains(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        if (normalized.Count > MaxCount)
        {
            return Error.Validation
            (
                code: "EnabledTools.TooMany",
                description: $"Cannot enable more than {MaxCount} tools."
            );
        }

        normalized.Sort(StringComparer.Ordinal);

        return new EnabledTools([.. normalized]);
    }

    public static EnabledTools FromDatabase(IEnumerable<string> names)
    {
        string[] ordered = names
            .Select(name => name.Trim().ToLowerInvariant())
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return new EnabledTools(ordered);
    }

    public override bool Equals(object? obj) =>
        obj is EnabledTools other && _names.SequenceEqual(other._names);

    public override int GetHashCode()
    {
        HashCode hash = new();

        foreach (string name in _names)
        {
            hash.Add(name);
        }

        return hash.ToHashCode();
    }
}
```

- [ ] **Step 4: Run it — expect PASS (5 tests)**

Run: `dotnet test tests/Chat/Chat.Domain.Tests --filter "FullyQualifiedName~EnabledToolsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Domain/Chats/ValueObjects/EnabledTools.cs tests/Chat/Chat.Domain.Tests/Chats/EnabledToolsTests.cs
git commit -m "feat(chat): add EnabledTools value object for per-turn tool selection"
```

---

## Task 2: Persist the selection on the assistant message

`BeginAssistantMessage` takes an optional trailing `enabledTools` (defaults to `None`, so every existing caller stays green). `RegenerateAssistant` inherits the original turn's selection.

**Files:**
- Modify: `src/services/Chat/Chat.Domain/Chats/Entities/ChatMessage.cs`
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs`
- Test: `tests/Chat/Chat.Domain.Tests/Chats/ChatMessageEnabledToolsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Domain.Tests.Chats;

public sealed class ChatMessageEnabledToolsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 13, 12, 0, 0, TimeSpan.Zero);

    private static ChatThread NewThread() => ChatThread.Create
    (
        userId: UserId.Create("auth0|user-1").Value,
        title: ChatTitle.Create("Hi").Value,
        firstUserMessage: MessageContent.Create("Hello").Value,
        createdAt: Now
    );

    [Fact]
    public void BeginAssistantMessage_StoresEnabledTools()
    {
        ChatThread thread = NewThread();
        EnabledTools tools = EnabledTools.Create(["web_search"]).Value;

        ErrorOr<ChatMessage> assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now,
            enabledTools: tools
        );

        Assert.False(assistant.IsError);
        Assert.Equal(new[] { "web_search" }, assistant.Value.EnabledTools.Names);
    }

    [Fact]
    public void BeginAssistantMessage_DefaultsToNone()
    {
        ChatThread thread = NewThread();

        ErrorOr<ChatMessage> assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: LlmModelId.New(),
            createdAt: Now
        );

        Assert.False(assistant.IsError);
        Assert.True(assistant.Value.EnabledTools.IsEmpty);
    }
}
```

- [ ] **Step 2: Run it — expect FAIL (compile: `enabledTools` param and `EnabledTools` property missing)**

Run: `dotnet test tests/Chat/Chat.Domain.Tests --filter "FullyQualifiedName~ChatMessageEnabledToolsTests"`
Expected: build error.

- [ ] **Step 3: Add `EnabledTools` to `ChatMessage`.** Add the property after `SiblingIndex` (around line 28):

```csharp
    public EnabledTools EnabledTools { get; private set; } = EnabledTools.None;
```

Add `EnabledTools enabledTools` to the **private constructor** parameter list (after `siblingIndex`) and assign it in the body:

```csharp
        SiblingIndex siblingIndex,
        EnabledTools enabledTools
    ) : base(id)
    {
        // ... existing assignments ...
        SiblingIndex = siblingIndex;
        EnabledTools = enabledTools;
    }
```

In `CreateUserMessage`, pass `enabledTools: EnabledTools.None` as the final argument. In `CreateAssistantMessage`, add an `EnabledTools enabledTools` parameter (after `siblingIndex`) and pass it through:

```csharp
    internal static ChatMessage CreateAssistantMessage
    (
        ChatId chatId,
        ChatMessageId parentMessageId,
        LlmModelId llmModelId,
        DateTimeOffset createdAt,
        SiblingIndex siblingIndex,
        EnabledTools enabledTools
    ) => new
    (
        id: ChatMessageId.New(),
        chatId: chatId,
        parentMessageId: parentMessageId,
        role: MessageRole.Assistant,
        content: null,
        llmModelId: llmModelId,
        status: MessageStatus.Generating,
        createdAt: createdAt,
        completedAt: null,
        siblingIndex: siblingIndex,
        enabledTools: enabledTools
    );
```

- [ ] **Step 4: Thread it through `ChatThread`.** In `BeginAssistantMessage`, add the optional trailing parameter and pass it down:

```csharp
    public ErrorOr<ChatMessage> BeginAssistantMessage
    (
        ChatMessageId parentMessageId,
        LlmModelId llmModelId,
        DateTimeOffset createdAt,
        EnabledTools? enabledTools = null
    )
    {
        // ... existing parent null / role checks unchanged ...

        ChatMessage message = ChatMessage.CreateAssistantMessage
        (
            chatId: Id,
            parentMessageId: parentMessageId,
            llmModelId: llmModelId,
            createdAt: createdAt,
            siblingIndex: GetNextSiblingIndex(parentMessageId),
            enabledTools: enabledTools ?? EnabledTools.None
        );

        _messages.Add(message);
        SetHead(message.Id, createdAt);

        return message;
    }
```

In `RegenerateAssistant`, pass the original message's selection to the new sibling — add `enabledTools: target.EnabledTools` as the final argument of its `ChatMessage.CreateAssistantMessage` call. Add `using Chat.Domain.Chats.ValueObjects;` if not already present (it is).

- [ ] **Step 5: Run it — expect PASS, then run the whole domain suite for regressions**

Run: `dotnet test tests/Chat/Chat.Domain.Tests`
Expected: PASS, no regressions (existing `BeginAssistantMessage` callers compile via the optional parameter).

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Domain tests/Chat/Chat.Domain.Tests/Chats/ChatMessageEnabledToolsTests.cs
git commit -m "feat(chat): persist enabled tools on the assistant message"
```

---

## Task 3: EF mapping + migration (jsonb)

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatMessageConfiguration.cs`
- Create (generated): `src/services/Chat/Chat.Infrastructure/Database/Migrations/*_ChatMessageEnabledTools.cs`

- [ ] **Step 1: Add the converter + comparer.** Add these usings at the top of `ChatMessageConfiguration.cs`:

```csharp
using System.Text.Json;

using Microsoft.EntityFrameworkCore.ChangeTracking;
```

Add this at the end of `Configure(...)` (after `builder.HasIndex(x => x.Status);`):

```csharp
        ValueComparer<EnabledTools> enabledToolsComparer = new
        (
            (left, right) => left!.Equals(right),
            tools => tools.GetHashCode(),
            tools => EnabledTools.FromDatabase(tools.Names)
        );

        builder.Property(x => x.EnabledTools)
            .HasConversion
            (
                tools => JsonSerializer.Serialize(tools.Names, (JsonSerializerOptions?)null),
                json => EnabledTools.FromDatabase(JsonSerializer.Deserialize<string[]>(json, (JsonSerializerOptions?)null) ?? Array.Empty<string>())
            )
            .HasColumnName("enabled_tools")
            .HasColumnType("jsonb")
            .IsRequired()
            .Metadata.SetValueComparer(enabledToolsComparer);
```

- [ ] **Step 2: Build to confirm the model compiles**

Run: `dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Generate the migration**

Run:
```bash
dotnet ef migrations add ChatMessageEnabledTools \
  --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj \
  --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj \
  --context ChatDbContext \
  --output-dir Database/Migrations
```
Expected: a new `*_ChatMessageEnabledTools.cs` migration adding an `enabled_tools jsonb NOT NULL` column to `chat_messages`. Open it and confirm `AddColumn<string>(name: "enabled_tools", ... type: "jsonb", nullable: false ...)` with a sensible default (`"[]"` or empty); if the generated default is missing, set `defaultValue: "[]"` on the `AddColumn`.

- [ ] **Step 4: Build the solution to confirm migration + snapshot compile**

Run: `dotnet build Nova.slnx`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure
git commit -m "feat(chat): map enabled tools to a jsonb column"
```

---

## Task 4: Tool seam — `IAgentTool` + `AgentToolNames`

**Files:**
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/IAgentTool.cs`
- Create: `src/services/Chat/Chat.Application/Turns/Tools/AgentToolNames.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/AgentToolNamesTests.cs`

- [ ] **Step 1: Write the failing test** (locks the contract string shared across layers)

```csharp
using Chat.Application.Turns.Tools;

namespace Chat.Application.Tests.Turns;

public sealed class AgentToolNamesTests
{
    [Fact]
    public void WebSearch_HasStableName()
    {
        Assert.Equal("web_search", AgentToolNames.WebSearch);
    }

    [Theory]
    [InlineData("web_search", true)]
    [InlineData("pizza_finder", false)]
    public void IsKnown_RecognizesOnlyRegisteredTools(string name, bool expected)
    {
        Assert.Equal(expected, AgentToolNames.IsKnown(name));
    }
}
```

- [ ] **Step 2: Run it — expect FAIL (compile, `AgentToolNames` missing)**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~AgentToolNamesTests"`
Expected: build error.

- [ ] **Step 3: Create `IAgentTool.cs`**

```csharp
namespace Chat.Application.Abstractions.Turns;

/// <summary>
/// Provider-agnostic agent tool seam (spec Rule 1/5). Carries no Agent Framework types;
/// <c>AgentFrameworkRunner</c> is the only code that adapts this to a framework AIFunction.
/// </summary>
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

- [ ] **Step 4: Create `AgentToolNames.cs`**

```csharp
namespace Chat.Application.Turns.Tools;

public static class AgentToolNames
{
    public const string WebSearch = "web_search";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal) { WebSearch };

    public static bool IsKnown(string name) => Known.Contains(name);
}
```

- [ ] **Step 5: Run it — expect PASS (3 cases)**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~AgentToolNamesTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Application/Abstractions/Turns/IAgentTool.cs src/services/Chat/Chat.Application/Turns/Tools/AgentToolNames.cs tests/Chat/Chat.Application.Tests/Turns/AgentToolNamesTests.cs
git commit -m "feat(chat): add provider-agnostic agent tool seam"
```

---

## Task 5: Web search seam + `WebSearchTool`

**Files:**
- Create: `src/services/Chat/Chat.Application/Abstractions/Turns/IWebSearchClient.cs`
- Create: `src/services/Chat/Chat.Application/Turns/Tools/WebSearchResultFormatter.cs`
- Create: `src/services/Chat/Chat.Application/Turns/Tools/WebSearchTool.cs`
- Create: `tests/Chat/Chat.Application.Tests/Turns/FakeWebSearchClient.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/WebSearchToolTests.cs`

- [ ] **Step 1: Create the fake client**

```csharp
using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Tests.Turns;

internal sealed class FakeWebSearchClient(IReadOnlyList<WebSearchResult> results, bool throws = false) : IWebSearchClient
{
    public string? LastQuery { get; private set; }

    public int LastCount { get; private set; }

    public Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int count, CancellationToken cancellationToken)
    {
        LastQuery = query;
        LastCount = count;

        if (throws)
        {
            throw new HttpRequestException("exa down");
        }

        return Task.FromResult(results);
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Chat.Application.Abstractions.Turns;
using Chat.Application.Turns.Tools;

namespace Chat.Application.Tests.Turns;

public sealed class WebSearchToolTests
{
    private static Func<string, int, CancellationToken, Task<string>> Invocation(WebSearchTool tool) =>
        (Func<string, int, CancellationToken, Task<string>>)tool.CreateInvocation();

    [Fact]
    public void Name_IsWebSearch()
    {
        WebSearchTool tool = new(new FakeWebSearchClient([]));

        Assert.Equal("web_search", tool.Name);
    }

    [Fact]
    public async Task Invocation_FormatsResultsAndPassesQueryAndCount()
    {
        FakeWebSearchClient client = new
        ([
            new WebSearchResult("Redis", "https://redis.io", "An in-memory store.", null)
        ]);
        WebSearchTool tool = new(client);

        string result = await Invocation(tool)("what is redis", 3, CancellationToken.None);

        Assert.Equal("what is redis", client.LastQuery);
        Assert.Equal(3, client.LastCount);
        Assert.Contains("Redis", result, StringComparison.Ordinal);
        Assert.Contains("https://redis.io", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invocation_WhenNoResults_SaysSo()
    {
        WebSearchTool tool = new(new FakeWebSearchClient([]));

        string result = await Invocation(tool)("obscure", 5, CancellationToken.None);

        Assert.Contains("No results", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invocation_WhenClientThrows_ReturnsGracefulMessage()
    {
        WebSearchTool tool = new(new FakeWebSearchClient([], throws: true));

        string result = await Invocation(tool)("redis", 5, CancellationToken.None);

        Assert.Contains("unavailable", result, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 3: Run it — expect FAIL (compile, types missing)**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~WebSearchToolTests"`
Expected: build error.

- [ ] **Step 4: Create `IWebSearchClient.cs`**

```csharp
namespace Chat.Application.Abstractions.Turns;

public sealed record WebSearchResult(string Title, string Url, string Snippet, DateTimeOffset? PublishedAt);

public interface IWebSearchClient
{
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int count, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Create `WebSearchResultFormatter.cs`**

```csharp
using System.Text;

using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Turns.Tools;

internal static class WebSearchResultFormatter
{
    private const int MaxSnippetLength = 500;

    public static string ToToolResult(IReadOnlyList<WebSearchResult> results)
    {
        if (results.Count == 0)
        {
            return "No results found.";
        }

        StringBuilder builder = new();

        for (int index = 0; index < results.Count; index++)
        {
            WebSearchResult result = results[index];

            builder.Append('[').Append(index + 1).Append("] ").AppendLine(result.Title);
            builder.Append("URL: ").AppendLine(result.Url);

            if (result.PublishedAt is not null)
            {
                builder.Append("Published: ").AppendLine(result.PublishedAt.Value.ToString("yyyy-MM-dd"));
            }

            string snippet = result.Snippet.Length > MaxSnippetLength
                ? result.Snippet[..MaxSnippetLength]
                : result.Snippet;

            builder.AppendLine(snippet);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }
}
```

- [ ] **Step 6: Create `WebSearchTool.cs`** (public so Infrastructure DI can register it — matches `ContextBuilder`)

```csharp
using System.ComponentModel;

using Chat.Application.Abstractions.Turns;

namespace Chat.Application.Turns.Tools;

public sealed class WebSearchTool(IWebSearchClient client) : IAgentTool
{
    private const int DefaultResultCount = 5;

    private const string UnavailableMessage =
        "Web search is currently unavailable. Answer using your existing knowledge and note that you could not search the web.";

    public string Name => AgentToolNames.WebSearch;

    public Delegate CreateInvocation() => SearchAsync;

    [Description("Search the public web for current information. Returns ranked results with snippets to cite.")]
    private async Task<string> SearchAsync
    (
        [Description("The search query.")] string query,
        [Description("Maximum number of results to return (1-10).")] int count = DefaultResultCount,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            IReadOnlyList<WebSearchResult> results = await client.SearchAsync(query, count, cancellationToken);

            return WebSearchResultFormatter.ToToolResult(results);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return UnavailableMessage;
        }
    }
}
```

- [ ] **Step 7: Run it — expect PASS (4 tests)**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~WebSearchToolTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/services/Chat/Chat.Application/Abstractions/Turns/IWebSearchClient.cs src/services/Chat/Chat.Application/Turns/Tools tests/Chat/Chat.Application.Tests/Turns/FakeWebSearchClient.cs tests/Chat/Chat.Application.Tests/Turns/WebSearchToolTests.cs
git commit -m "feat(chat): add web search tool over a provider-agnostic client seam"
```

---

## Task 6: Carry the selection into `TurnContext`

`ContextBuilder` reads `assistantMessage.EnabledTools` (no new `BuildAsync` parameter — Rule 6).

**Files:**
- Modify: `src/services/Chat/Chat.Application/Abstractions/Turns/TurnContext.cs`
- Modify: `src/services/Chat/Chat.Application/Turns/ContextBuilder.cs`
- Modify: `tests/Chat/Chat.Application.Tests/Turns/FakeContextBuilder.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs`

- [ ] **Step 1: Add the failing test** (append to `ContextBuilderTests`) and add an assertion to the existing chronological test that tools default empty:

In `BuildAsyncProducesChronologicalHistoryEndingAtTheUserMessage`, after the existing asserts, add:

```csharp
        Assert.Empty(context.Value.EnabledTools);
```

Append a new test:

```csharp
    [Fact]
    public async Task BuildAsyncCopiesEnabledToolsFromAssistantMessage()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: ExternalModelId.FromDatabase("gpt-4.1"),
            profile: TestCatalogFactory.CreateProfile()
        ).Value;
        _providers.AddExistingProvider(provider);

        ChatThread thread = ChatThread.Create
        (
            userId: UserId.Create("auth0|user-1").Value,
            title: ChatTitle.Create("Hello").Value,
            firstUserMessage: MessageContent.Create("What is Redis?").Value,
            createdAt: Now
        );

        ChatMessage assistant = thread.BeginAssistantMessage
        (
            parentMessageId: thread.CurrentMessageId,
            llmModelId: model.Id,
            createdAt: Now,
            enabledTools: EnabledTools.Create(["web_search"]).Value
        ).Value;

        ContextBuilder builder = new(_providers);

        ErrorOr<TurnContext> context = await builder.BuildAsync
        (
            thread: thread,
            assistantMessage: assistant,
            memories: RetrievedMemories.Empty,
            cancellationToken: CancellationToken.None
        );

        Assert.False(context.IsError);
        Assert.Equal(new[] { "web_search" }, context.Value.EnabledTools);
    }
```

- [ ] **Step 2: Run it — expect FAIL (compile: `TurnContext.EnabledTools` missing)**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ContextBuilderTests"`
Expected: build error.

- [ ] **Step 3: Add `EnabledTools` to `TurnContext`** (trailing positional):

```csharp
public sealed record TurnContext
(
    Guid TurnId,
    Guid ChatId,
    string UserId,
    string ExternalModelId,
    string SystemPrompt,
    IReadOnlyList<TurnMessage> Messages,
    IReadOnlyList<string> EnabledTools
);
```

- [ ] **Step 4: Populate it in `ContextBuilder`** — add the argument to the returned record:

```csharp
        return new TurnContext
        (
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            ExternalModelId: model.ExternalModelId.Value,
            SystemPrompt: DefaultSystemPrompt,
            Messages: history,
            EnabledTools: assistantMessage.EnabledTools.Names
        );
```

- [ ] **Step 5: Update `FakeContextBuilder`** — add `EnabledTools: []` to its `new(...)`:

```csharp
        TurnContext context = new
        (
            TurnId: assistantMessage.Id.Value,
            ChatId: thread.Id.Value,
            UserId: thread.UserId.Value,
            ExternalModelId: "gpt-4.1",
            SystemPrompt: "test",
            Messages: [new TurnMessage(TurnRole.User, "Hello")],
            EnabledTools: []
        );
```

- [ ] **Step 6: Run it — expect PASS, then run the full Turns suite for regressions**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~Turns"`
Expected: PASS (no other `TurnContext` construction exists; orchestrator tests use `FakeContextBuilder`).

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application/Abstractions/Turns/TurnContext.cs src/services/Chat/Chat.Application/Turns/ContextBuilder.cs tests/Chat/Chat.Application.Tests/Turns/FakeContextBuilder.cs tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs
git commit -m "feat(chat): carry enabled tools into the turn context"
```

---

## Task 7: Commands, validation, and the capability gate

Both commands gain an optional `Tools` list. `ModelUsability` validates known names + `SupportsToolCalling` (fail fast). Handlers map `Tools → EnabledTools → BeginAssistantMessage`.

**Files:**
- Modify: `src/services/Chat/Chat.Application/Chats/Errors/ChatOperationErrors.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/ModelUsability.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatCommand.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/CreateChat/CreateChatHandler.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageCommand.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/Commands/SendMessage/SendMessageHandler.cs`
- Modify: `tests/Chat/Chat.Application.Tests/ModelCatalog/TestCatalogFactory.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/CreateChatHandlerTests.cs`, `SendMessageHandlerTests.cs`

- [ ] **Step 1: Add the `supportsToolCalling` knob to the test factory** so we can seed a tool-incapable model:

```csharp
    public static LlmModelProfile CreateProfile(string name = "GPT-4.1", bool supportsToolCalling = true)
    {
        return LlmModelProfile.Create
        (
            name: ModelName.FromDatabase(name),
            description: ModelDescription.FromDatabase("General purpose model"),
            contextWindow: ContextWindow.FromDatabase(128000),
            capabilities: ModelCapabilities.Create
            (
                supportsVision: true,
                supportsReasoning: false,
                supportsToolCalling: supportsToolCalling
            )
        );
    }
```

- [ ] **Step 2: Write the failing handler tests.** Add a helper + three tests to **`CreateChatHandlerTests`**:

```csharp
    private LlmModel SeedModelWithoutToolSupport()
    {
        LlmProvider provider = TestCatalogFactory.CreateProvider();
        LlmModel model = provider.AddModel
        (
            externalModelId: TestCatalogFactory.CreateExternalModelId(),
            profile: TestCatalogFactory.CreateProfile(supportsToolCalling: false)
        ).Value;
        _providers.AddExistingProvider(provider);
        return model;
    }

    [Fact]
    public async Task HandlePersistsEnabledToolsWhenWebSearchRequested()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateChatCommand("What is Redis?", model.Id.Value, ["web_search"]), CancellationToken.None);

        Assert.False(result.IsError);
        ChatThread thread = Assert.Single(_chats.Threads);
        ChatMessage assistant = Assert.Single(thread.Messages, m => m.Role == MessageRole.Assistant);
        Assert.Equal(new[] { "web_search" }, assistant.EnabledTools.Names);
        Assert.Single(_messageBus.Published);
    }

    [Fact]
    public async Task HandleReturnsUnknownToolWhenToolNotRecognized()
    {
        LlmModel model = SeedModel();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateChatCommand("Hello", model.Id.Value, ["pizza_finder"]), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.UnknownTool", result.FirstError.Code);
        Assert.Empty(_chats.Threads);
        Assert.Empty(_messageBus.Published);
    }

    [Fact]
    public async Task HandleReturnsModelDoesNotSupportToolsWhenModelLacksToolCalling()
    {
        LlmModel model = SeedModelWithoutToolSupport();

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new CreateChatCommand("Hello", model.Id.Value, ["web_search"]), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.ModelDoesNotSupportTools", result.FirstError.Code);
        Assert.Empty(_chats.Threads);
        Assert.Empty(_messageBus.Published);
    }
```

Add the same `SeedModelWithoutToolSupport` helper to **`SendMessageHandlerTests`**, plus:

```csharp
    [Fact]
    public async Task HandlePersistsEnabledToolsWhenWebSearchRequested()
    {
        LlmModel model = SeedModel();
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new SendMessageCommand(thread.Id.Value, "Tell me more", model.Id.Value, ["web_search"]), CancellationToken.None);

        Assert.False(result.IsError);
        ChatMessage assistant = Assert.Single(thread.Messages, m => m.Id.Value == result.Value.AssistantMessageId);
        Assert.Equal(new[] { "web_search" }, assistant.EnabledTools.Names);
    }

    [Fact]
    public async Task HandleReturnsModelDoesNotSupportToolsWhenModelLacksToolCalling()
    {
        LlmModel model = SeedModelWithoutToolSupport();
        ChatThread thread = SeedThreadWithCompletedTurn(model);

        ErrorOr<TurnStartedResult> result = await CreateHandler()
            .Handle(new SendMessageCommand(thread.Id.Value, "Hello", model.Id.Value, ["web_search"]), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Chat.ModelDoesNotSupportTools", result.FirstError.Code);
        Assert.Empty(_messageBus.Published);
    }
```

- [ ] **Step 3: Run them — expect FAIL (compile: command `Tools` arg + errors missing)**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~CreateChatHandlerTests|FullyQualifiedName~SendMessageHandlerTests"`
Expected: build error.

- [ ] **Step 4: Add the two errors to `ChatOperationErrors`** (append inside the class):

```csharp
    public static Error UnknownTool(string toolName) =>
        Error.Validation
        (
            code: "Chat.UnknownTool",
            description: $"'{toolName}' is not a known agent tool."
        );

    public static Error ModelDoesNotSupportTools(LlmModelId modelId) =>
        Error.Conflict
        (
            code: "Chat.ModelDoesNotSupportTools",
            description: $"LLM model '{modelId.Value}' does not support tool calling."
        );
```

- [ ] **Step 5: Extend `ModelUsability`** — add a `requestedTools` parameter and the gate. Add `using Chat.Application.Turns.Tools;` at the top, then:

```csharp
    public static async Task<ErrorOr<Success>> EnsureUsableAsync
    (
        ILlmProviderRepository providers,
        LlmModelId modelId,
        IReadOnlyList<string> requestedTools,
        CancellationToken cancellationToken
    )
    {
        LlmProvider? provider = await providers.GetByModelIdAsync(modelId, cancellationToken);

        if (provider is null)
        {
            return ChatOperationErrors.LlmModelNotFound(modelId);
        }

        LlmModel? model = provider.FindModel(modelId);

        if (model is null)
        {
            return ChatOperationErrors.LlmModelNotFound(modelId);
        }

        if (!provider.IsEnabled || !model.IsEnabled)
        {
            return ChatOperationErrors.LlmModelDisabled(modelId);
        }

        if (requestedTools.Count > 0)
        {
            foreach (string tool in requestedTools)
            {
                if (!AgentToolNames.IsKnown(tool))
                {
                    return ChatOperationErrors.UnknownTool(tool);
                }
            }

            if (!model.Profile.Capabilities.SupportsToolCalling)
            {
                return ChatOperationErrors.ModelDoesNotSupportTools(modelId);
            }
        }

        return Result.Success;
    }
```

- [ ] **Step 6: Add `Tools` to both commands** (optional trailing — keeps existing call sites green):

`CreateChatCommand.cs`:
```csharp
public sealed record CreateChatCommand(string Message, Guid LlmModelId, IReadOnlyList<string>? Tools = null) : ICommand<ErrorOr<TurnStartedResult>>;
```

`SendMessageCommand.cs`:
```csharp
public sealed record SendMessageCommand
(
    Guid ChatId,
    string Message,
    Guid LlmModelId,
    IReadOnlyList<string>? Tools = null
) : ICommand<ErrorOr<TurnStartedResult>>;
```

- [ ] **Step 7: Wire the handlers.** In **`CreateChatHandler`**, after the `modelIdResult` block, add the parse to the aggregation:

```csharp
        ErrorOr<EnabledTools> enabledToolsResult = EnabledTools.Create(command.Tools);

        if (enabledToolsResult.IsError)
        {
            errors.AddRange(enabledToolsResult.Errors);
        }
```

After `if (errors.Count > 0) { return errors; }`, add:

```csharp
        EnabledTools enabledTools = enabledToolsResult.Value;
```

Change the `ModelUsability.EnsureUsableAsync` call to pass the names, and the `BeginAssistantMessage` call to pass the selection:

```csharp
        ErrorOr<Success> usabilityResult = await ModelUsability.EnsureUsableAsync
        (
            providers: providers,
            modelId: modelId,
            requestedTools: enabledTools.Names,
            cancellationToken: cancellationToken
        );
```
```csharp
        ErrorOr<ChatMessage> assistantMessageResult = thread.BeginAssistantMessage
        (
            parentMessageId: userMessageId,
            llmModelId: modelId,
            createdAt: now,
            enabledTools: enabledTools
        );
```

Apply the identical three edits to **`SendMessageHandler`** (parse into the aggregation block; `EnabledTools enabledTools = enabledToolsResult.Value;` after the early return; pass `requestedTools: enabledTools.Names` to `EnsureUsableAsync`; pass `enabledTools: enabledTools` to `BeginAssistantMessage`). Both handlers already import `Chat.Domain.Chats.ValueObjects`.

- [ ] **Step 8: Run them — expect PASS, then the full Application suite**

Run: `dotnet test tests/Chat/Chat.Application.Tests`
Expected: PASS (existing handler tests still green via optional `Tools`; new tool tests green).

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Application tests/Chat/Chat.Application.Tests
git commit -m "feat(chat): accept per-message tool selection and gate on model capability"
```

---

## Task 8: Expose `tools` on the HTTP endpoints

**Files:**
- Modify: `src/services/Chat/Chat.Api/Endpoints/Chats/CreateChat/Endpoint.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/Chats/SendMessage/Endpoint.cs`

- [ ] **Step 1: CreateChat** — add `Tools` to the request and pass it to the command:

```csharp
internal sealed record Request(string Message, Guid ModelId, IReadOnlyList<string>? Tools = null);
```
```csharp
        CreateChatCommand command = new(request.Message, request.ModelId, request.Tools);
```

- [ ] **Step 2: SendMessage** — same:

```csharp
internal sealed record Request(string Message, Guid ModelId, IReadOnlyList<string>? Tools = null);
```
```csharp
        SendMessageCommand command = new(Route<Guid>("chatId"), request.Message, request.ModelId, request.Tools);
```

- [ ] **Step 3: Build the API**

Run: `dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj`
Expected: Build succeeded. (Clients now send `{ "message": "...", "modelId": "...", "tools": ["web_search"] }`; omitting `tools` is unchanged behavior.)

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats
git commit -m "feat(chat): accept an optional tools list on chat turn endpoints"
```

---

## Task 9: Exa client (Infrastructure) + infra test project

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Tools/ExaOptions.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Agents/Tools/ExaWebSearchClient.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj` (InternalsVisibleTo)
- Create: `tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj`
- Create: `tests/Chat/Chat.Infrastructure.Tests/Agents/Tools/ExaWebSearchClientTests.cs`
- Modify: `Nova.slnx`

- [ ] **Step 1: Create the infra test project csproj**

`tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\src\services\Chat\Chat.Infrastructure\Chat.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register the project in the solution**

Run: `dotnet sln Nova.slnx add tests/Chat/Chat.Infrastructure.Tests/Chat.Infrastructure.Tests.csproj`
Expected: "Project ... added to the solution."

- [ ] **Step 3: Allow the test project to see internal types.** Add to `Chat.Infrastructure.csproj` (new ItemGroup):

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Chat.Infrastructure.Tests" />
  </ItemGroup>
```

- [ ] **Step 4: Write the failing mapping test**

`tests/Chat/Chat.Infrastructure.Tests/Agents/Tools/ExaWebSearchClientTests.cs`:
```csharp
using System.Net;
using System.Text;

using Chat.Application.Abstractions.Turns;
using Chat.Infrastructure.Agents.Tools;

using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Tests.Agents.Tools;

public sealed class ExaWebSearchClientTests
{
    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    [Fact]
    public async Task SearchAsync_MapsExaResultsAndClampsCount()
    {
        const string json = """
        {
          "results": [
            {
              "title": "Redis",
              "url": "https://redis.io",
              "text": "Redis is an in-memory data store used as a database, cache and message broker.",
              "highlights": ["Redis is an in-memory data store."],
              "publishedDate": "2024-01-15T00:00:00.000Z"
            }
          ]
        }
        """;

        StubHandler handler = new(json);
        HttpClient httpClient = new(handler) { BaseAddress = new Uri("https://api.exa.ai") };
        ExaWebSearchClient client = new(httpClient, Options.Create(new ExaOptions { MaxResults = 3 }));

        IReadOnlyList<WebSearchResult> results = await client.SearchAsync("what is redis", 50, CancellationToken.None);

        WebSearchResult single = Assert.Single(results);
        Assert.Equal("Redis", single.Title);
        Assert.Equal("https://redis.io", single.Url);
        Assert.Equal("Redis is an in-memory data store.", single.Snippet);
        Assert.Equal(2024, single.PublishedAt!.Value.Year);
        Assert.Contains("\"numResults\":3", handler.RequestBody!, StringComparison.Ordinal); // clamped to MaxResults
    }
}
```

- [ ] **Step 5: Run it — expect FAIL (compile: `ExaWebSearchClient`/`ExaOptions` missing)**

Run: `dotnet test tests/Chat/Chat.Infrastructure.Tests`
Expected: build error.

- [ ] **Step 6: Create `ExaOptions.cs`**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Chat.Infrastructure.Agents.Tools;

public sealed class ExaOptions
{
    public const string SectionName = "Exa";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    public Uri BaseUrl { get; init; } = new("https://api.exa.ai");

    public int MaxResults { get; init; } = 10;
}
```

- [ ] **Step 7: Create `ExaWebSearchClient.cs`**

```csharp
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Chat.Application.Abstractions.Turns;

using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Agents.Tools;

internal sealed class ExaWebSearchClient(HttpClient httpClient, IOptions<ExaOptions> options) : IWebSearchClient
{
    private readonly int _maxResults = options.Value.MaxResults;

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int count, CancellationToken cancellationToken)
    {
        int clamped = Math.Clamp(count, 1, _maxResults);

        ExaSearchRequest request = new
        (
            Query: query,
            NumResults: clamped,
            Contents: new ExaContentsRequest(Text: true, Highlights: new ExaHighlightsRequest(NumSentences: 3))
        );

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync("/search", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        ExaSearchResponse? payload = await response.Content.ReadFromJsonAsync<ExaSearchResponse>(cancellationToken);

        if (payload?.Results is null)
        {
            return [];
        }

        return payload.Results
            .Select(result => new WebSearchResult
            (
                Title: result.Title ?? result.Url,
                Url: result.Url,
                Snippet: BestSnippet(result),
                PublishedAt: ParseDate(result.PublishedDate)
            ))
            .ToList();
    }

    private static string BestSnippet(ExaResult result)
    {
        if (result.Highlights is { Length: > 0 })
        {
            return string.Join(" ", result.Highlights);
        }

        return result.Text ?? string.Empty;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out DateTimeOffset parsed) ? parsed : null;

    private sealed record ExaSearchRequest
    (
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("numResults")] int NumResults,
        [property: JsonPropertyName("contents")] ExaContentsRequest Contents
    );

    private sealed record ExaContentsRequest
    (
        [property: JsonPropertyName("text")] bool Text,
        [property: JsonPropertyName("highlights")] ExaHighlightsRequest Highlights
    );

    private sealed record ExaHighlightsRequest
    (
        [property: JsonPropertyName("numSentences")] int NumSentences
    );

    private sealed record ExaSearchResponse
    (
        [property: JsonPropertyName("results")] ExaResult[]? Results
    );

    private sealed record ExaResult
    (
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("highlights")] string[]? Highlights,
        [property: JsonPropertyName("publishedDate")] string? PublishedDate
    );
}
```

- [ ] **Step 8: Run it — expect PASS**

Run: `dotnet test tests/Chat/Chat.Infrastructure.Tests`
Expected: PASS (1 test).

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Agents/Tools src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj tests/Chat/Chat.Infrastructure.Tests Nova.slnx
git commit -m "feat(chat): add Exa web search client over a resilient HttpClient"
```

---

## Task 10: Attach tools in the runner + drag-and-drop DI

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/Agents/AgentFrameworkRunner.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj` (resilience package)
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
- Modify: `src/services/Chat/Chat.TurnWorker/appsettings.json`

- [ ] **Step 1: Add the resilience package** to `Chat.Infrastructure.csproj` (version is centrally managed):

```xml
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
```

- [ ] **Step 2: Inject and attach tools in `AgentFrameworkRunner`.** Replace the field/constructor and the agent-creation lines:

```csharp
    private readonly OpenAIClient _client;
    private readonly IReadOnlyList<IAgentTool> _tools;

    public AgentFrameworkRunner(IOptions<AgentOptions> options, IEnumerable<IAgentTool> tools)
    {
        AgentOptions value = options.Value;

        _client = new OpenAIClient
        (
            new ApiKeyCredential(value.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(value.BaseUrl.ToString()) }
        );

        _tools = tools.ToList();
    }
```

Inside `RunAsync`, before building `messages`, build the filtered tool list and pass it to `AsAIAgent`:

```csharp
        IList<AITool> tools = _tools
            .Where(tool => context.EnabledTools.Contains(tool.Name))
            .Select(tool => (AITool)AIFunctionFactory.Create
            (
                tool.CreateInvocation(),
                new AIFunctionFactoryOptions { Name = tool.Name }
            ))
            .ToList();

        AIAgent agent = _client
            .GetChatClient(context.ExternalModelId)
            .AsAIAgent(instructions: context.SystemPrompt, tools: tools);
```

(`AIFunctionFactory`, `AIFunctionFactoryOptions`, `AITool` are all in `Microsoft.Extensions.AI`, already imported. `AsAIAgent(instructions:, tools:)` is the verified 1.10.0 overload. An empty list means no tools — today's behavior. `ChatClientAgent` auto-invokes the functions.)

- [ ] **Step 3: Add the drag-and-drop registration.** In `DependencyInjection.cs`, add these usings:

```csharp
using Chat.Infrastructure.Agents.Tools;

using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
```

Add the call inside `AddTurnPipeline` (after the runner/decorator registration, before `AddAnalytics`):

```csharp
        services.AddWebSearchTool(configuration);
```

Add the method:

```csharp
    private static IServiceCollection AddWebSearchTool(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ExaOptions>()
            .Bind(configuration.GetSection(ExaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddHttpClient<IWebSearchClient, ExaWebSearchClient>((serviceProvider, httpClient) =>
            {
                ExaOptions options = serviceProvider.GetRequiredService<IOptions<ExaOptions>>().Value;
                httpClient.BaseAddress = options.BaseUrl;
                httpClient.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
            })
            .AddStandardResilienceHandler();

        services.AddScoped<IAgentTool, WebSearchTool>();

        return services;
    }
```

(`IAgentTool` and `IWebSearchClient` are in `Chat.Application.Abstractions.Turns` — already imported; `WebSearchTool` is in `Chat.Application.Turns.Tools` — add `using Chat.Application.Turns.Tools;`.)

- [ ] **Step 4: Add Exa config to the worker.** In `Chat.TurnWorker/appsettings.json`, add a sibling to `Agent`:

```json
  "Exa": {
    "BaseUrl": "https://api.exa.ai",
    "MaxResults": 10
  }
```

Note: `Exa:ApiKey` is a secret — supply it via environment (`Exa__ApiKey`) or user-secrets, exactly like `Agent:ApiKey`. Registering the tool makes the key required at worker startup (`ValidateOnStart`); removing `AddWebSearchTool` removes that requirement — the drag-and-drop contract.

- [ ] **Step 5: Build the worker + solution**

Run: `dotnet build Nova.slnx`
Expected: Build succeeded.

- [ ] **Step 6: Manual smoke (not automated — requires a real Exa key + running infra).** Set `Exa__ApiKey`, run the AppHost, create a chat with a tool-capable model and `"tools": ["web_search"]`, and confirm the SSE stream emits a `tool_call` event for `web_search` followed by grounded tokens. Record the result in the PR description.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure src/services/Chat/Chat.TurnWorker/appsettings.json
git commit -m "feat(chat): wire the Exa web search tool into the agent runner"
```

---

## Task 11: Full verification

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Nova.slnx`
Expected: Build succeeded, 0 errors (SonarAnalyzer warnings unchanged from baseline).

- [ ] **Step 2: Run all Chat tests**

Run: `dotnet test tests/Chat/Chat.Domain.Tests tests/Chat/Chat.Application.Tests tests/Chat/Chat.Infrastructure.Tests`
Expected: all PASS.

- [ ] **Step 3: Confirm Rule 1 holds — no framework leak outside the quarantine**

Run: `grep -rn "Microsoft.Agents.AI\|Microsoft.Extensions.AI" src/services/Chat --include="*.cs" | grep -v "/Agents/"`
Expected: no output (every framework reference lives under `Chat.Infrastructure/Agents/`).

- [ ] **Step 4: Final commit if anything was adjusted**

```bash
git add -A
git commit -m "test(chat): verify Exa web search tool end to end"
```

---

## Self-review notes (author)

- **Spec coverage:** tool seam (T4) ✓, Exa client (T9) ✓, WebSearchTool (T5) ✓, per-message persisted toggle (T1–T3, T6–T8) ✓, capability fail-fast gate (T7) ✓, drag-and-drop single registration (T10) ✓, `tool_used` telemetry — already present, no task ✓, streaming events — already mapped, no task ✓.
- **Placeholders:** none — every step has concrete code/commands and expected output.
- **Type consistency:** `EnabledTools.Names : IReadOnlyList<string>`; `TurnContext.EnabledTools : IReadOnlyList<string>`; `IAgentTool.CreateInvocation() : Delegate` consumed by `AIFunctionFactory.Create`; `IWebSearchClient.SearchAsync(string, int, CancellationToken)` matched by `FakeWebSearchClient`, `ExaWebSearchClient`, and `WebSearchTool`; command `Tools : IReadOnlyList<string>?` flows endpoint → command → `EnabledTools.Create` → `BeginAssistantMessage(enabledTools:)`.
- **Green-at-every-commit:** `BeginAssistantMessage` and both command records take optional trailing parameters, so Tasks 1–6 never break existing callers; handlers start consuming the selection only in Task 7.
