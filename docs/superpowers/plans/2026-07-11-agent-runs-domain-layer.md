# Agent Runs Domain Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PR #1 of agent mode — the generic, kind-agnostic `AgentRun`/`AgentRunActivity` domain concept plus its persistence, fully inert (nothing calls it yet).

**Architecture:** Per the approved spec `docs/superpowers/specs/2026-07-11-agent-runs-domain-design.md`. `AgentRun` is a new aggregate root in `Chat.Domain/AgentRuns/` — chat-anchored, no status (the assistant `ChatMessage` owns the lifecycle), no stored counters (state derives from the append-only activity log). Activities carry a closed execution-native `ActivityKind` plus an open kind-owned `ActivityType` string and a validated JSON `Detail`. Persistence adds two tables (`agent_runs`, `agent_run_activities`) with a composite FK onto the `chat_messages` alternate key and a unique `(run_id, sequence)` idempotency backstop.

**Tech Stack:** .NET 10, EF Core + Npgsql (snake_case naming convention), ErrorOr, xunit (plain `Assert`).

## Global Constraints

- Tests are **domain unit tests only** — no infrastructure, repository, or API tests (project rule).
- Value-object factories return `ErrorOr<T>`; `FromDatabase` throws `DomainException`; ids use `Guid.CreateVersion7()` via `New()`.
- Repositories are `internal sealed`. Named arguments at call sites. Enums stored as strings in Postgres.
- `AgentRun` is descriptive, never authoritative: no status field, no code consults it to gate transitions.
- Activities are append-only: no update or delete paths anywhere in domain or infrastructure.
- Plain xunit `Assert` — no FluentAssertions. Commit after every task; build must pass before each commit.
- Run all commands from the repo root `/Users/akakijomidava/conductor/workspaces/Nova/da-nang` on branch `agent-runs-domain-layer-pr`.
- Do NOT run `dotnet ef database update` — `Chat.MigrationWorker` applies migrations at runtime.

## File Structure Overview

```
src/services/Chat/Chat.Domain/AgentRuns/
  ValueObjects/AgentRunId.cs                     Task 1
  ValueObjects/AgentRunActivityId.cs             Task 1
  ValueObjects/AgentRunKind.cs                   Task 1
  ValueObjects/ActivityKind.cs                   Task 1
  ValueObjects/AgentTask.cs                      Task 2
  ValueObjects/ActivityType.cs                   Task 2
  ValueObjects/ActivityTitle.cs                  Task 2
  ValueObjects/ActivityDetail.cs                 Task 2
  ValueObjects/ActivitySequence.cs               Task 3
  ValueObjects/TokenUsage.cs                     Task 3
  Entities/AgentRunActivity.cs                   Task 4
  AgentRun.cs                                    Task 4
  AgentRunErrors.cs                              Task 4
  IAgentRunRepository.cs                         Task 4
src/services/Chat/Chat.Infrastructure/AgentRuns/
  Configurations/AgentRunConfiguration.cs        Task 5
  Configurations/AgentRunActivityConfiguration.cs Task 5
  Repositories/AgentRunRepository.cs             Task 5
src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs        Task 5 (DbSet)
src/services/Chat/Chat.Infrastructure/DependencyInjection.cs           Task 5 (DI)
src/services/Chat/Chat.Infrastructure/Database/Migrations/*_AgentRuns* Task 6
tests/Chat/Chat.Domain.Tests/AgentRuns/
  AgentRunIdentifierValueObjectTests.cs          Task 1
  AgentRunStringValueObjectTests.cs              Task 2
  ActivitySequenceTests.cs                       Task 3
  TokenUsageTests.cs                             Task 3
  AgentRunTests.cs                               Task 4
```

---

### Task 1: Identifier and enum value objects

**Files:**
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/AgentRunId.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/AgentRunActivityId.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/AgentRunKind.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivityKind.cs`
- Test: `tests/Chat/Chat.Domain.Tests/AgentRuns/AgentRunIdentifierValueObjectTests.cs`

**Interfaces:**
- Consumes: `DomainException` (namespace `Chat.Domain`, resolved via parent-namespace lookup — no using needed), `ErrorOr`.
- Produces: `AgentRunId` and `AgentRunActivityId` (each with `New()`, `ErrorOr<T> Create(Guid)`, `FromDatabase(Guid)`, `Guid Value`); `AgentRunKind { Research = 1 }`; `ActivityKind { Phase = 1, Thought = 2, ToolCall = 3, Observation = 4, Error = 5 }`.

- [ ] **Step 1: Write the failing tests**

`tests/Chat/Chat.Domain.Tests/AgentRuns/AgentRunIdentifierValueObjectTests.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns;

public sealed class AgentRunIdentifierValueObjectTests
{
    [Fact]
    public void AgentRunId_New_ProducesNonEmptyValue()
    {
        Assert.NotEqual(Guid.Empty, AgentRunId.New().Value);
    }

    [Fact]
    public void AgentRunId_Create_RejectsEmptyGuid()
    {
        ErrorOr<AgentRunId> result = AgentRunId.Create(Guid.Empty);

        Assert.True(result.IsError);
        Assert.Equal("AgentRunId.Empty", result.FirstError.Code);
    }

    [Fact]
    public void AgentRunId_Create_AcceptsNonEmptyGuid()
    {
        Guid value = Guid.CreateVersion7();

        ErrorOr<AgentRunId> result = AgentRunId.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
    }

    [Fact]
    public void AgentRunId_FromDatabase_ThrowsOnEmptyGuid()
    {
        Assert.Throws<DomainException>(() => AgentRunId.FromDatabase(Guid.Empty));
    }

    [Fact]
    public void AgentRunActivityId_New_ProducesNonEmptyValue()
    {
        Assert.NotEqual(Guid.Empty, AgentRunActivityId.New().Value);
    }

    [Fact]
    public void AgentRunActivityId_Create_RejectsEmptyGuid()
    {
        ErrorOr<AgentRunActivityId> result = AgentRunActivityId.Create(Guid.Empty);

        Assert.True(result.IsError);
        Assert.Equal("AgentRunActivityId.Empty", result.FirstError.Code);
    }

    [Fact]
    public void AgentRunActivityId_FromDatabase_ThrowsOnEmptyGuid()
    {
        Assert.Throws<DomainException>(() => AgentRunActivityId.FromDatabase(Guid.Empty));
    }
}
```

Add to the top of the file `using Chat.Domain;` if `DomainException` does not resolve (it lives in the `Chat.Domain` namespace).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~AgentRuns"`
Expected: FAIL to compile — `AgentRunId` does not exist.

- [ ] **Step 3: Implement the four value objects**

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/AgentRunId.cs` (mirrors `ChatId`):

```csharp
using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record AgentRunId
{
    public Guid Value { get; }

    private AgentRunId(Guid value)
    {
        Value = value;
    }

    public static AgentRunId New() => new(Guid.CreateVersion7());

    public static ErrorOr<AgentRunId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "AgentRunId.Empty",
                description: "Agent run id cannot be empty."
            );
        }

        return new AgentRunId(value);
    }

    public static AgentRunId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty agent run id.");

        return new AgentRunId(value);
    }

    public override string ToString() => Value.ToString();
}
```

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/AgentRunActivityId.cs` — identical shape, replacing the type name, error code `AgentRunActivityId.Empty`, description "Agent run activity id cannot be empty." / "Database contained an empty agent run activity id.":

```csharp
using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record AgentRunActivityId
{
    public Guid Value { get; }

    private AgentRunActivityId(Guid value)
    {
        Value = value;
    }

    public static AgentRunActivityId New() => new(Guid.CreateVersion7());

    public static ErrorOr<AgentRunActivityId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "AgentRunActivityId.Empty",
                description: "Agent run activity id cannot be empty."
            );
        }

        return new AgentRunActivityId(value);
    }

    public static AgentRunActivityId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty agent run activity id.");

        return new AgentRunActivityId(value);
    }

    public override string ToString() => Value.ToString();
}
```

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/AgentRunKind.cs` (mirrors `MessageStatus` style):

```csharp
namespace Chat.Domain.AgentRuns.ValueObjects;

#pragma warning disable CA1008
public enum AgentRunKind
{
    Research = 1
}
```

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivityKind.cs`:

```csharp
namespace Chat.Domain.AgentRuns.ValueObjects;

#pragma warning disable CA1008
public enum ActivityKind
{
    Phase = 1,
    Thought = 2,
    ToolCall = 3,
    Observation = 4,
    Error = 5
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~AgentRuns"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Domain/AgentRuns tests/Chat/Chat.Domain.Tests/AgentRuns
git commit -m "feat(chat): add agent run identifier and enum value objects"
```

---

### Task 2: String value objects — AgentTask, ActivityType, ActivityTitle, ActivityDetail

**Files:**
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/AgentTask.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivityType.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivityTitle.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivityDetail.cs`
- Test: `tests/Chat/Chat.Domain.Tests/AgentRuns/AgentRunStringValueObjectTests.cs`

**Interfaces:**
- Consumes: `DomainException`, `ErrorOr`, `System.Text.Json` (BCL — no new package).
- Produces: `AgentTask` (`MaxLength = 32_768`), `ActivityType` (`MaxLength = 100`, lowercase `[a-z0-9._-]`), `ActivityTitle` (`MaxLength = 300`), `ActivityDetail` (`MaxLength = 16_384`, must parse as JSON). Each: `string Value`, `ErrorOr<T> Create(string?)`, `FromDatabase(string)`.

- [ ] **Step 1: Write the failing tests**

`tests/Chat/Chat.Domain.Tests/AgentRuns/AgentRunStringValueObjectTests.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns;

public sealed class AgentRunStringValueObjectTests
{
    // AgentTask

    [Fact]
    public void AgentTask_Create_TrimsAndAccepts()
    {
        ErrorOr<AgentTask> result = AgentTask.Create("  What changed in EU AI law?  ");

        Assert.False(result.IsError);
        Assert.Equal("What changed in EU AI law?", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AgentTask_Create_RejectsMissingValue(string? value)
    {
        ErrorOr<AgentTask> result = AgentTask.Create(value);

        Assert.True(result.IsError);
        Assert.Equal("AgentTask.Required", result.FirstError.Code);
    }

    [Fact]
    public void AgentTask_Create_RejectsValueOverMaxLength()
    {
        ErrorOr<AgentTask> result = AgentTask.Create(new string('a', AgentTask.MaxLength + 1));

        Assert.True(result.IsError);
        Assert.Equal("AgentTask.TooLong", result.FirstError.Code);
    }

    [Fact]
    public void AgentTask_FromDatabase_ThrowsOnInvalidValue()
    {
        Assert.Throws<DomainException>(() => AgentTask.FromDatabase(""));
    }

    // ActivityType

    [Theory]
    [InlineData("web.search")]
    [InlineData("file_edit")]
    [InlineData("source-capture")]
    [InlineData("tool9")]
    public void ActivityType_Create_AcceptsWellFormedTypes(string value)
    {
        ErrorOr<ActivityType> result = ActivityType.Create(value);

        Assert.False(result.IsError);
        Assert.Equal(value, result.Value.Value);
    }

    [Theory]
    [InlineData("Web.Search")]
    [InlineData("web search")]
    [InlineData("web/search")]
    public void ActivityType_Create_RejectsInvalidCharacters(string value)
    {
        ErrorOr<ActivityType> result = ActivityType.Create(value);

        Assert.True(result.IsError);
        Assert.Equal("ActivityType.InvalidFormat", result.FirstError.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ActivityType_Create_RejectsMissingValue(string? value)
    {
        ErrorOr<ActivityType> result = ActivityType.Create(value);

        Assert.True(result.IsError);
        Assert.Equal("ActivityType.Required", result.FirstError.Code);
    }

    [Fact]
    public void ActivityType_Create_RejectsValueOverMaxLength()
    {
        ErrorOr<ActivityType> result = ActivityType.Create(new string('a', ActivityType.MaxLength + 1));

        Assert.True(result.IsError);
        Assert.Equal("ActivityType.TooLong", result.FirstError.Code);
    }

    [Fact]
    public void ActivityType_FromDatabase_ThrowsOnInvalidValue()
    {
        Assert.Throws<DomainException>(() => ActivityType.FromDatabase("Not Valid"));
    }

    // ActivityTitle

    [Fact]
    public void ActivityTitle_Create_TrimsAndAccepts()
    {
        ErrorOr<ActivityTitle> result = ActivityTitle.Create("  Searching: EU battery regulation  ");

        Assert.False(result.IsError);
        Assert.Equal("Searching: EU battery regulation", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ActivityTitle_Create_RejectsMissingValue(string? value)
    {
        ErrorOr<ActivityTitle> result = ActivityTitle.Create(value);

        Assert.True(result.IsError);
        Assert.Equal("ActivityTitle.Required", result.FirstError.Code);
    }

    [Fact]
    public void ActivityTitle_Create_RejectsValueOverMaxLength()
    {
        ErrorOr<ActivityTitle> result = ActivityTitle.Create(new string('a', ActivityTitle.MaxLength + 1));

        Assert.True(result.IsError);
        Assert.Equal("ActivityTitle.TooLong", result.FirstError.Code);
    }

    [Fact]
    public void ActivityTitle_FromDatabase_ThrowsOnInvalidValue()
    {
        Assert.Throws<DomainException>(() => ActivityTitle.FromDatabase(""));
    }

    // ActivityDetail

    [Fact]
    public void ActivityDetail_Create_AcceptsJsonObject()
    {
        ErrorOr<ActivityDetail> result = ActivityDetail.Create("""{"url":"https://example.com","domain":"example.com"}""");

        Assert.False(result.IsError);
    }

    [Fact]
    public void ActivityDetail_Create_RejectsMalformedJson()
    {
        ErrorOr<ActivityDetail> result = ActivityDetail.Create("{not json");

        Assert.True(result.IsError);
        Assert.Equal("ActivityDetail.InvalidJson", result.FirstError.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ActivityDetail_Create_RejectsMissingValue(string? value)
    {
        ErrorOr<ActivityDetail> result = ActivityDetail.Create(value);

        Assert.True(result.IsError);
        Assert.Equal("ActivityDetail.Required", result.FirstError.Code);
    }

    [Fact]
    public void ActivityDetail_Create_RejectsValueOverMaxLength()
    {
        string oversized = $$"""{"payload":"{{new string('a', ActivityDetail.MaxLength)}}"}""";

        ErrorOr<ActivityDetail> result = ActivityDetail.Create(oversized);

        Assert.True(result.IsError);
        Assert.Equal("ActivityDetail.TooLong", result.FirstError.Code);
    }

    [Fact]
    public void ActivityDetail_FromDatabase_ThrowsOnEmptyValue()
    {
        Assert.Throws<DomainException>(() => ActivityDetail.FromDatabase(""));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~AgentRuns"`
Expected: FAIL to compile — `AgentTask` does not exist.

- [ ] **Step 3: Implement the four value objects**

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/AgentTask.cs` (mirrors `ChatTitle`; max length matches `MessageContent.MaxLength` because the task arrives as a user message):

```csharp
using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record AgentTask
{
    public const int MaxLength = 32_768;

    public string Value { get; }

    private AgentTask(string value)
    {
        Value = value;
    }

    public static ErrorOr<AgentTask> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "AgentTask.Required",
                description: "Agent task is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "AgentTask.TooLong",
                description: $"Agent task cannot exceed {MaxLength} characters."
            );
        }

        return new AgentTask(trimmed);
    }

    public static AgentTask FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid agent task.");

        return new AgentTask(value);
    }

    public override string ToString() => Value;
}
```

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivityType.cs`:

```csharp
using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record ActivityType
{
    public const int MaxLength = 100;

    public string Value { get; }

    private ActivityType(string value)
    {
        Value = value;
    }

    public static ErrorOr<ActivityType> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ActivityType.Required",
                description: "Activity type is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ActivityType.TooLong",
                description: $"Activity type cannot exceed {MaxLength} characters."
            );
        }

        if (!IsWellFormed(trimmed))
        {
            return Error.Validation
            (
                code: "ActivityType.InvalidFormat",
                description: "Activity type may only contain lowercase letters, digits, '.', '_' and '-'."
            );
        }

        return new ActivityType(trimmed);
    }

    public static ActivityType FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || !IsWellFormed(value))
            throw new DomainException("Database contained an invalid activity type.");

        return new ActivityType(value);
    }

    private static bool IsWellFormed(string value) =>
        value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_' or '-');

    public override string ToString() => Value;
}
```

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivityTitle.cs` — same shape as `AgentTask` with `MaxLength = 300`, codes `ActivityTitle.Required` / `ActivityTitle.TooLong`, descriptions "Activity title is required." / "Activity title cannot exceed {MaxLength} characters." / "Database contained an invalid activity title.":

```csharp
using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record ActivityTitle
{
    public const int MaxLength = 300;

    public string Value { get; }

    private ActivityTitle(string value)
    {
        Value = value;
    }

    public static ErrorOr<ActivityTitle> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ActivityTitle.Required",
                description: "Activity title is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ActivityTitle.TooLong",
                description: $"Activity title cannot exceed {MaxLength} characters."
            );
        }

        return new ActivityTitle(trimmed);
    }

    public static ActivityTitle FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid activity title.");

        return new ActivityTitle(value);
    }

    public override string ToString() => Value;
}
```

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivityDetail.cs`. JSON validity is checked in `Create` only; `FromDatabase` skips re-parsing because the `jsonb` column type makes invalid JSON unstorable:

```csharp
using System.Text.Json;

using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record ActivityDetail
{
    public const int MaxLength = 16_384;

    public string Value { get; }

    private ActivityDetail(string value)
    {
        Value = value;
    }

    public static ErrorOr<ActivityDetail> Create(string? value)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "ActivityDetail.Required",
                description: "Activity detail is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "ActivityDetail.TooLong",
                description: $"Activity detail cannot exceed {MaxLength} characters."
            );
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            return Error.Validation
            (
                code: "ActivityDetail.InvalidJson",
                description: "Activity detail must be valid JSON."
            );
        }

        return new ActivityDetail(trimmed);
    }

    public static ActivityDetail FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid activity detail.");

        return new ActivityDetail(value);
    }

    public override string ToString() => Value;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~AgentRuns"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Domain/AgentRuns tests/Chat/Chat.Domain.Tests/AgentRuns
git commit -m "feat(chat): add agent run string value objects"
```

---

### Task 3: ActivitySequence and TokenUsage

**Files:**
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivitySequence.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/TokenUsage.cs`
- Test: `tests/Chat/Chat.Domain.Tests/AgentRuns/ActivitySequenceTests.cs`
- Test: `tests/Chat/Chat.Domain.Tests/AgentRuns/TokenUsageTests.cs`

**Interfaces:**
- Produces: `ActivitySequence` (`int Value` > 0; `Create(int)`, `FromDatabase(int)`); `TokenUsage` (`int InputTokens`, `int OutputTokens`, both ≥ 0; `Zero`, `Create(int, int)`, `FromDatabase(int, int)`, `TokenUsage Add(TokenUsage other)`). `TokenUsage` needs a private parameterless constructor for EF complex-property materialization (mirrors `ModelCapabilities`).

- [ ] **Step 1: Write the failing tests**

`tests/Chat/Chat.Domain.Tests/AgentRuns/ActivitySequenceTests.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns;

public sealed class ActivitySequenceTests
{
    [Fact]
    public void Create_AcceptsPositiveValue()
    {
        ErrorOr<ActivitySequence> result = ActivitySequence.Create(1);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveValue(int value)
    {
        ErrorOr<ActivitySequence> result = ActivitySequence.Create(value);

        Assert.True(result.IsError);
        Assert.Equal("ActivitySequence.NotPositive", result.FirstError.Code);
    }

    [Fact]
    public void FromDatabase_ThrowsOnNonPositiveValue()
    {
        Assert.Throws<DomainException>(() => ActivitySequence.FromDatabase(0));
    }
}
```

`tests/Chat/Chat.Domain.Tests/AgentRuns/TokenUsageTests.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns;

public sealed class TokenUsageTests
{
    [Fact]
    public void Zero_HasNoTokens()
    {
        Assert.Equal(0, TokenUsage.Zero.InputTokens);
        Assert.Equal(0, TokenUsage.Zero.OutputTokens);
    }

    [Fact]
    public void Create_AcceptsNonNegativeCounts()
    {
        ErrorOr<TokenUsage> result = TokenUsage.Create(inputTokens: 10, outputTokens: 5);

        Assert.False(result.IsError);
        Assert.Equal(10, result.Value.InputTokens);
        Assert.Equal(5, result.Value.OutputTokens);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Create_RejectsNegativeCounts(int inputTokens, int outputTokens)
    {
        ErrorOr<TokenUsage> result = TokenUsage.Create(inputTokens, outputTokens);

        Assert.True(result.IsError);
        Assert.Equal("TokenUsage.Negative", result.FirstError.Code);
    }

    [Fact]
    public void Add_SumsBothCountsWithoutMutatingOperands()
    {
        TokenUsage first = TokenUsage.Create(inputTokens: 10, outputTokens: 5).Value;
        TokenUsage second = TokenUsage.Create(inputTokens: 3, outputTokens: 2).Value;

        TokenUsage sum = first.Add(second);

        Assert.Equal(13, sum.InputTokens);
        Assert.Equal(7, sum.OutputTokens);
        Assert.Equal(10, first.InputTokens);
        Assert.Equal(5, first.OutputTokens);
    }

    [Fact]
    public void FromDatabase_ThrowsOnNegativeCounts()
    {
        Assert.Throws<DomainException>(() => TokenUsage.FromDatabase(inputTokens: -1, outputTokens: 0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~AgentRuns"`
Expected: FAIL to compile — `ActivitySequence` does not exist.

- [ ] **Step 3: Implement the two value objects**

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/ActivitySequence.cs`:

```csharp
using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record ActivitySequence
{
    public int Value { get; }

    private ActivitySequence(int value)
    {
        Value = value;
    }

    public static ErrorOr<ActivitySequence> Create(int value)
    {
        if (value <= 0)
        {
            return Error.Validation
            (
                code: "ActivitySequence.NotPositive",
                description: "Activity sequence must be a positive integer."
            );
        }

        return new ActivitySequence(value);
    }

    public static ActivitySequence FromDatabase(int value)
    {
        if (value <= 0)
            throw new DomainException("Database contained a non-positive activity sequence.");

        return new ActivitySequence(value);
    }

    public override string ToString() => Value.ToString();
}
```

`src/services/Chat/Chat.Domain/AgentRuns/ValueObjects/TokenUsage.cs`:

```csharp
using ErrorOr;

namespace Chat.Domain.AgentRuns.ValueObjects;

public sealed record TokenUsage
{
    public int InputTokens { get; private init; }

    public int OutputTokens { get; private init; }

    private TokenUsage()
    {
        // For EF Core
    }

    private TokenUsage(int inputTokens, int outputTokens)
    {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    public static TokenUsage Zero => new(inputTokens: 0, outputTokens: 0);

    public static ErrorOr<TokenUsage> Create(int inputTokens, int outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0)
        {
            return Error.Validation
            (
                code: "TokenUsage.Negative",
                description: "Token counts cannot be negative."
            );
        }

        return new TokenUsage(inputTokens, outputTokens);
    }

    public static TokenUsage FromDatabase(int inputTokens, int outputTokens)
    {
        if (inputTokens < 0 || outputTokens < 0)
            throw new DomainException("Database contained negative token counts.");

        return new TokenUsage(inputTokens, outputTokens);
    }

    public TokenUsage Add(TokenUsage other) => new
    (
        inputTokens: InputTokens + other.InputTokens,
        outputTokens: OutputTokens + other.OutputTokens
    );
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~AgentRuns"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Domain/AgentRuns tests/Chat/Chat.Domain.Tests/AgentRuns
git commit -m "feat(chat): add activity sequence and token usage value objects"
```

---

### Task 4: AgentRunActivity entity, AgentRun aggregate, errors, repository interface

**Files:**
- Create: `src/services/Chat/Chat.Domain/AgentRuns/Entities/AgentRunActivity.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/AgentRunErrors.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/AgentRun.cs`
- Create: `src/services/Chat/Chat.Domain/AgentRuns/IAgentRunRepository.cs`
- Test: `tests/Chat/Chat.Domain.Tests/AgentRuns/AgentRunTests.cs`

**Interfaces:**
- Consumes: all Task 1–3 value objects; `ChatId`, `ChatMessageId` (`Chat.Domain.Chats.ValueObjects`); `UserId` (`Chat.Domain.Shared`); `LlmModelId` (`Chat.Domain.ModelCatalog.ValueObjects`); `AggregateRoot<TId>`, `Entity<TId>` (`SharedKernel`).
- Produces:
  - `AgentRun.Start(AgentRunKind kind, ChatId chatId, ChatMessageId assistantMessageId, UserId userId, AgentTask task, LlmModelId llmModelId, DateTimeOffset startedAt)` → `AgentRun`
  - `AgentRun.AppendActivity(ActivitySequence sequence, ActivityKind kind, ActivityType type, ActivityTitle title, ActivityDetail? detail, DateTimeOffset occurredAt)` → `ErrorOr<AgentRunActivity>`
  - `AgentRun.RecordUsage(TokenUsage delta)` → `ErrorOr<Success>`; `AgentRun.Finish(DateTimeOffset finishedAt)` → `ErrorOr<Success>`
  - `AgentRun.CurrentPhase` → `ActivityTitle?` (computed); `AgentRun.Activities` → `IReadOnlyCollection<AgentRunActivity>`
  - Error codes: `AgentRun.StaleActivitySequence`, `AgentRun.AlreadyFinished`, `AgentRun.FinishedBeforeStarted`
  - `IAgentRunRepository { void Add(AgentRun run); Task<AgentRun?> GetByIdAsync(AgentRunId, CancellationToken = default); Task<AgentRun?> GetByAssistantMessageIdAsync(ChatMessageId, CancellationToken = default); }`

- [ ] **Step 1: Write the failing tests**

`tests/Chat/Chat.Domain.Tests/AgentRuns/AgentRunTests.cs`:

```csharp
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.Entities;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Domain.Tests.AgentRuns;

public sealed class AgentRunTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 7, 11, 12, 0, 0, TimeSpan.Zero);

    private static AgentRun StartRun() =>
        AgentRun.Start
        (
            kind: AgentRunKind.Research,
            chatId: ChatId.New(),
            assistantMessageId: ChatMessageId.New(),
            userId: UserId.Create("auth0|user-1").Value,
            task: AgentTask.Create("What changed in EU AI law in 2026?").Value,
            llmModelId: LlmModelId.New(),
            startedAt: StartedAt
        );

    private static ErrorOr<AgentRunActivity> Append
    (
        AgentRun run,
        int sequence,
        ActivityKind kind = ActivityKind.ToolCall,
        string type = "web.search",
        string title = "Searching: EU AI law",
        string? detail = null
    ) => run.AppendActivity
    (
        sequence: ActivitySequence.Create(sequence).Value,
        kind: kind,
        type: ActivityType.Create(type).Value,
        title: ActivityTitle.Create(title).Value,
        detail: detail is null ? null : ActivityDetail.Create(detail).Value,
        occurredAt: StartedAt.AddSeconds(sequence)
    );

    [Fact]
    public void Start_InitializesDescriptiveStateWithNoActivities()
    {
        AgentRun run = StartRun();

        Assert.NotEqual(Guid.Empty, run.Id.Value);
        Assert.Equal(AgentRunKind.Research, run.Kind);
        Assert.Equal("What changed in EU AI law in 2026?", run.Task.Value);
        Assert.Equal(StartedAt, run.StartedAt);
        Assert.Null(run.FinishedAt);
        Assert.Equal(TokenUsage.Zero, run.Usage);
        Assert.Empty(run.Activities);
        Assert.Null(run.CurrentPhase);
    }

    [Fact]
    public void AppendActivity_AddsActivityBoundToRun()
    {
        AgentRun run = StartRun();

        ErrorOr<AgentRunActivity> result = Append(run, sequence: 1, detail: """{"query":"EU AI law"}""");

        Assert.False(result.IsError);
        Assert.Equal(run.Id, result.Value.RunId);
        Assert.Equal(1, result.Value.Sequence.Value);
        Assert.Equal("""{"query":"EU AI law"}""", result.Value.Detail!.Value);
        Assert.Single(run.Activities);
    }

    [Fact]
    public void AppendActivity_AllowsSequenceGaps()
    {
        AgentRun run = StartRun();

        Append(run, sequence: 1);
        ErrorOr<AgentRunActivity> result = Append(run, sequence: 5);

        Assert.False(result.IsError);
        Assert.Equal(2, run.Activities.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AppendActivity_RejectsSequenceAtOrBelowHighestPersisted(int staleSequence)
    {
        AgentRun run = StartRun();
        Append(run, sequence: 1);
        Append(run, sequence: 2);

        ErrorOr<AgentRunActivity> result = Append(run, sequence: staleSequence);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.StaleActivitySequence", result.FirstError.Code);
        Assert.Equal(2, run.Activities.Count);
    }

    [Fact]
    public void AppendActivity_AfterFinish_IsRejected()
    {
        AgentRun run = StartRun();
        run.Finish(StartedAt.AddMinutes(5));

        ErrorOr<AgentRunActivity> result = Append(run, sequence: 1);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.AlreadyFinished", result.FirstError.Code);
    }

    [Fact]
    public void RecordUsage_AccumulatesAcrossCalls()
    {
        AgentRun run = StartRun();

        run.RecordUsage(TokenUsage.Create(inputTokens: 10, outputTokens: 5).Value);
        run.RecordUsage(TokenUsage.Create(inputTokens: 3, outputTokens: 2).Value);

        Assert.Equal(13, run.Usage.InputTokens);
        Assert.Equal(7, run.Usage.OutputTokens);
    }

    [Fact]
    public void RecordUsage_AfterFinish_IsRejected()
    {
        AgentRun run = StartRun();
        run.Finish(StartedAt.AddMinutes(5));

        ErrorOr<Success> result = run.RecordUsage(TokenUsage.Create(inputTokens: 1, outputTokens: 1).Value);

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.AlreadyFinished", result.FirstError.Code);
    }

    [Fact]
    public void Finish_SetsFinishedAt()
    {
        AgentRun run = StartRun();
        DateTimeOffset finishedAt = StartedAt.AddMinutes(5);

        ErrorOr<Success> result = run.Finish(finishedAt);

        Assert.False(result.IsError);
        Assert.Equal(finishedAt, run.FinishedAt);
    }

    [Fact]
    public void Finish_Twice_IsRejected()
    {
        AgentRun run = StartRun();
        run.Finish(StartedAt.AddMinutes(5));

        ErrorOr<Success> result = run.Finish(StartedAt.AddMinutes(6));

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.AlreadyFinished", result.FirstError.Code);
        Assert.Equal(StartedAt.AddMinutes(5), run.FinishedAt);
    }

    [Fact]
    public void Finish_BeforeStartedAt_IsRejected()
    {
        AgentRun run = StartRun();

        ErrorOr<Success> result = run.Finish(StartedAt.AddMinutes(-1));

        Assert.True(result.IsError);
        Assert.Equal("AgentRun.FinishedBeforeStarted", result.FirstError.Code);
        Assert.Null(run.FinishedAt);
    }

    [Fact]
    public void CurrentPhase_TracksLatestPhaseActivityBySequence()
    {
        AgentRun run = StartRun();

        Assert.Null(run.CurrentPhase);

        Append(run, sequence: 1, kind: ActivityKind.Phase, type: "phase", title: "Planning");
        Append(run, sequence: 2, kind: ActivityKind.ToolCall, type: "web.search", title: "Searching");
        Append(run, sequence: 3, kind: ActivityKind.Phase, type: "phase", title: "Synthesizing");

        Assert.Equal("Synthesizing", run.CurrentPhase!.Value);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~AgentRuns"`
Expected: FAIL to compile — `AgentRun` does not exist.

- [ ] **Step 3: Implement entity, errors, aggregate, repository interface**

`src/services/Chat/Chat.Domain/AgentRuns/Entities/AgentRunActivity.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

using SharedKernel;

namespace Chat.Domain.AgentRuns.Entities;

public sealed class AgentRunActivity : Entity<AgentRunActivityId>
{
    public AgentRunId RunId { get; private set; } = default!;

    public ActivitySequence Sequence { get; private set; } = default!;

    public ActivityKind Kind { get; private set; }

    public ActivityType Type { get; private set; } = default!;

    public ActivityTitle Title { get; private set; } = default!;

    public ActivityDetail? Detail { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    private AgentRunActivity()
    {
        // EF Core materialization only
    }

    private AgentRunActivity
    (
        AgentRunActivityId id,
        AgentRunId runId,
        ActivitySequence sequence,
        ActivityKind kind,
        ActivityType type,
        ActivityTitle title,
        ActivityDetail? detail,
        DateTimeOffset occurredAt
    ) : base(id)
    {
        RunId = runId;
        Sequence = sequence;
        Kind = kind;
        Type = type;
        Title = title;
        Detail = detail;
        OccurredAt = occurredAt;
    }

    internal static AgentRunActivity Create
    (
        AgentRunId runId,
        ActivitySequence sequence,
        ActivityKind kind,
        ActivityType type,
        ActivityTitle title,
        ActivityDetail? detail,
        DateTimeOffset occurredAt
    ) => new
    (
        id: AgentRunActivityId.New(),
        runId: runId,
        sequence: sequence,
        kind: kind,
        type: type,
        title: title,
        detail: detail,
        occurredAt: occurredAt
    );
}
```

`src/services/Chat/Chat.Domain/AgentRuns/AgentRunErrors.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;

using ErrorOr;

namespace Chat.Domain.AgentRuns;

public static class AgentRunErrors
{
    public static Error StaleActivitySequence(AgentRunId runId, ActivitySequence sequence) =>
        Error.Conflict
        (
            code: "AgentRun.StaleActivitySequence",
            description:
            $"Activity sequence '{sequence.Value}' is at or below the highest sequence already recorded for run '{runId.Value}'."
        );

    public static Error AlreadyFinished(AgentRunId runId) =>
        Error.Conflict
        (
            code: "AgentRun.AlreadyFinished",
            description: $"Agent run '{runId.Value}' is already finished."
        );

    public static Error FinishedBeforeStarted(AgentRunId runId) =>
        Error.Validation
        (
            code: "AgentRun.FinishedBeforeStarted",
            description: $"Agent run '{runId.Value}' cannot finish before it started."
        );
}
```

`src/services/Chat/Chat.Domain/AgentRuns/AgentRun.cs`:

```csharp
using Chat.Domain.AgentRuns.Entities;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using SharedKernel;

namespace Chat.Domain.AgentRuns;

public sealed class AgentRun : AggregateRoot<AgentRunId>
{
    private readonly List<AgentRunActivity> _activities = [];

    public ChatId ChatId { get; private set; } = default!;

    public ChatMessageId AssistantMessageId { get; private set; } = default!;

    public UserId UserId { get; private set; } = default!;

    public AgentRunKind Kind { get; private set; }

    public AgentTask Task { get; private set; } = default!;

    public LlmModelId LlmModelId { get; private set; } = default!;

    public TokenUsage Usage { get; private set; } = default!;

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public IReadOnlyCollection<AgentRunActivity> Activities => _activities;

    public ActivityTitle? CurrentPhase =>
        _activities
            .Where(activity => activity.Kind == ActivityKind.Phase)
            .MaxBy(activity => activity.Sequence.Value)?
            .Title;

    private AgentRun()
    {
        // EF Core materialization only
    }

    private AgentRun
    (
        AgentRunId id,
        ChatId chatId,
        ChatMessageId assistantMessageId,
        UserId userId,
        AgentRunKind kind,
        AgentTask task,
        LlmModelId llmModelId,
        DateTimeOffset startedAt
    ) : base(id)
    {
        ChatId = chatId;
        AssistantMessageId = assistantMessageId;
        UserId = userId;
        Kind = kind;
        Task = task;
        LlmModelId = llmModelId;
        Usage = TokenUsage.Zero;
        StartedAt = startedAt;
    }

    public static AgentRun Start
    (
        AgentRunKind kind,
        ChatId chatId,
        ChatMessageId assistantMessageId,
        UserId userId,
        AgentTask task,
        LlmModelId llmModelId,
        DateTimeOffset startedAt
    ) => new
    (
        id: AgentRunId.New(),
        chatId: chatId,
        assistantMessageId: assistantMessageId,
        userId: userId,
        kind: kind,
        task: task,
        llmModelId: llmModelId,
        startedAt: startedAt
    );

    public ErrorOr<AgentRunActivity> AppendActivity
    (
        ActivitySequence sequence,
        ActivityKind kind,
        ActivityType type,
        ActivityTitle title,
        ActivityDetail? detail,
        DateTimeOffset occurredAt
    )
    {
        if (FinishedAt is not null)
            return AgentRunErrors.AlreadyFinished(Id);

        int highestSequence = _activities.Count == 0
            ? 0
            : _activities.Max(activity => activity.Sequence.Value);

        if (sequence.Value <= highestSequence)
            return AgentRunErrors.StaleActivitySequence(Id, sequence);

        AgentRunActivity activity = AgentRunActivity.Create
        (
            runId: Id,
            sequence: sequence,
            kind: kind,
            type: type,
            title: title,
            detail: detail,
            occurredAt: occurredAt
        );

        _activities.Add(activity);

        return activity;
    }

    public ErrorOr<Success> RecordUsage(TokenUsage delta)
    {
        if (FinishedAt is not null)
            return AgentRunErrors.AlreadyFinished(Id);

        Usage = Usage.Add(delta);

        return Result.Success;
    }

    public ErrorOr<Success> Finish(DateTimeOffset finishedAt)
    {
        if (FinishedAt is not null)
            return AgentRunErrors.AlreadyFinished(Id);

        if (finishedAt < StartedAt)
            return AgentRunErrors.FinishedBeforeStarted(Id);

        FinishedAt = finishedAt;

        return Result.Success;
    }
}
```

`src/services/Chat/Chat.Domain/AgentRuns/IAgentRunRepository.cs`:

```csharp
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;

namespace Chat.Domain.AgentRuns;

public interface IAgentRunRepository
{
    void Add(AgentRun run);

    Task<AgentRun?> GetByIdAsync
    (
        AgentRunId id,
        CancellationToken cancellationToken = default
    );

    Task<AgentRun?> GetByAssistantMessageIdAsync
    (
        ChatMessageId assistantMessageId,
        CancellationToken cancellationToken = default
    );
}
```

Note: inside `AgentRun.cs` the property `Task` coexists with `System.Threading.Tasks.Task` only in `IAgentRunRepository.cs`, which is a different file — no conflict. Do not add async members to the aggregate.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj --filter "FullyQualifiedName~AgentRuns"`
Expected: PASS (all AgentRuns tests, including Tasks 1–3).

- [ ] **Step 5: Run the full domain test suite to confirm no regressions**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Domain/AgentRuns tests/Chat/Chat.Domain.Tests/AgentRuns
git commit -m "feat(chat): add AgentRun aggregate with append-only activities"
```

---

### Task 5: EF Core configurations, DbSet, repository implementation, DI registration

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/AgentRuns/Configurations/AgentRunConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/AgentRuns/Configurations/AgentRunActivityConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/AgentRuns/Repositories/AgentRunRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs` (add DbSet)
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs` (register repository)

**Interfaces:**
- Consumes: Task 4's `AgentRun`, `AgentRunActivity`, `IAgentRunRepository`, all value objects; `ChatThread`, `ChatMessage` (FK targets — `chat_messages` has alternate key `(ChatId, Id)` declared in `ChatMessageConfiguration`).
- Produces: tables `agent_runs`, `agent_run_activities`; `ChatDbContext.AgentRuns`; scoped `IAgentRunRepository` → `AgentRunRepository`.

No tests in this task (no infra tests — project rule). The deliverable check is a clean build; the schema is verified in Task 6 via the generated migration.

- [ ] **Step 1: Write AgentRunConfiguration**

`src/services/Chat/Chat.Infrastructure/AgentRuns/Configurations/AgentRunConfiguration.cs`:

```csharp
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats;
using Chat.Domain.Chats.Entities;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.AgentRuns.Configurations;

internal sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_runs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => AgentRunId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.ChatId)
            .HasConversion
            (
                id => id.Value,
                value => ChatId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.AssistantMessageId)
            .HasConversion
            (
                id => id.Value,
                value => ChatMessageId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasConversion
            (
                id => id.Value,
                value => UserId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Task)
            .HasConversion
            (
                task => task.Value,
                value => AgentTask.FromDatabase(value)
            )
            .HasMaxLength(AgentTask.MaxLength)
            .IsRequired();

        builder.Property(x => x.LlmModelId)
            .HasConversion
            (
                id => id.Value,
                value => LlmModelId.FromDatabase(value)
            )
            .IsRequired();

        builder.ComplexProperty(x => x.Usage, usage =>
        {
            usage.Property(value => value.InputTokens)
                .HasColumnName("input_tokens");

            usage.Property(value => value.OutputTokens)
                .HasColumnName("output_tokens");
        });

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.FinishedAt);

        builder.Ignore(x => x.CurrentPhase);

        builder.HasIndex(x => x.AssistantMessageId)
            .IsUnique();

        builder.HasOne<ChatThread>()
            .WithMany()
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ChatMessage>()
            .WithMany()
            .HasForeignKey(x => new { x.ChatId, x.AssistantMessageId })
            .HasPrincipalKey(x => new { x.ChatId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Activities)
            .WithOne()
            .HasForeignKey(activity => activity.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Activities)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(x => x.DomainEvents);
    }
}
```

The `chat_id` index required by the spec is created automatically by EF for the `ChatThread` FK; no explicit `HasIndex(x => x.ChatId)` is needed.

- [ ] **Step 2: Write AgentRunActivityConfiguration**

`src/services/Chat/Chat.Infrastructure/AgentRuns/Configurations/AgentRunActivityConfiguration.cs`:

```csharp
using Chat.Domain.AgentRuns.Entities;
using Chat.Domain.AgentRuns.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.AgentRuns.Configurations;

internal sealed class AgentRunActivityConfiguration : IEntityTypeConfiguration<AgentRunActivity>
{
    public void Configure(EntityTypeBuilder<AgentRunActivity> builder)
    {
        builder.ToTable("agent_run_activities");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion
            (
                id => id.Value,
                value => AgentRunActivityId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(x => x.RunId)
            .HasConversion
            (
                id => id.Value,
                value => AgentRunId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.Sequence)
            .HasConversion
            (
                sequence => sequence.Value,
                value => ActivitySequence.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion
            (
                type => type.Value,
                value => ActivityType.FromDatabase(value)
            )
            .HasMaxLength(ActivityType.MaxLength)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasConversion
            (
                title => title.Value,
                value => ActivityTitle.FromDatabase(value)
            )
            .HasMaxLength(ActivityTitle.MaxLength)
            .IsRequired();

        builder.Property(x => x.Detail)
            .HasConversion
            (
                detail => detail!.Value,
                value => ActivityDetail.FromDatabase(value)
            )
            .HasColumnType("jsonb");

        builder.Property(x => x.OccurredAt)
            .IsRequired();

        builder.HasIndex(x => new { x.RunId, x.Sequence })
            .IsUnique();
    }
}
```

- [ ] **Step 3: Write the repository implementation**

`src/services/Chat/Chat.Infrastructure/AgentRuns/Repositories/AgentRunRepository.cs`:

```csharp
using Chat.Domain.AgentRuns;
using Chat.Domain.AgentRuns.ValueObjects;
using Chat.Domain.Chats.ValueObjects;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.AgentRuns.Repositories;

internal sealed class AgentRunRepository(ChatDbContext db) : IAgentRunRepository
{
    public void Add(AgentRun run)
    {
        db.AgentRuns.Add(run);
    }

    public async Task<AgentRun?> GetByIdAsync
    (
        AgentRunId id,
        CancellationToken cancellationToken = default
    )
    {
        return await db.AgentRuns
            .Include(x => x.Activities.OrderBy(activity => activity.Sequence))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<AgentRun?> GetByAssistantMessageIdAsync
    (
        ChatMessageId assistantMessageId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.AgentRuns
            .Include(x => x.Activities.OrderBy(activity => activity.Sequence))
            .FirstOrDefaultAsync(x => x.AssistantMessageId == assistantMessageId, cancellationToken);
    }
}
```

- [ ] **Step 4: Add the DbSet and DI registration**

In `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`, add a using for `Chat.Domain.AgentRuns` and, after the `Projects` DbSet property:

```csharp
public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
```

In `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`, add usings for `Chat.Domain.AgentRuns` and `Chat.Infrastructure.AgentRuns.Repositories`, then next to the existing repository registrations (around line 130):

```csharp
services.AddScoped<IAgentRunRepository, AgentRunRepository>();
```

- [ ] **Step 5: Build the solution**

Run: `dotnet build Nova.slnx`
Expected: Build succeeded, no new warnings.

- [ ] **Step 6: Run the full domain test suite (regression check)**

Run: `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/AgentRuns src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): add agent run persistence and repository"
```

---

### Task 6: EF Core migration

**Files:**
- Create (generated): `src/services/Chat/Chat.Infrastructure/Database/Migrations/<timestamp>_AgentRuns.cs` + `.Designer.cs`
- Modify (generated): `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: Task 5's configurations.
- Produces: the `agent_runs` and `agent_run_activities` schema, applied automatically by `Chat.MigrationWorker` at runtime.

- [ ] **Step 1: Generate the migration**

```bash
dotnet ef migrations add AgentRuns \
  --project src/services/Chat/Chat.Infrastructure \
  --startup-project src/services/Chat/Chat.Api
```

If `dotnet ef` is not installed: `dotnet tool install --global dotnet-ef`, then retry.

- [ ] **Step 2: Inspect the generated migration**

Open the generated `<timestamp>_AgentRuns.cs` and verify — do not hand-edit unless a check fails:

- `CreateTable "agent_runs"`: `id` (uuid, PK), `chat_id` (uuid), `assistant_message_id` (uuid), `user_id` (text), `kind` (text), `task` (character varying(32768)), `llm_model_id` (uuid), `input_tokens` (integer), `output_tokens` (integer), `started_at` (timestamptz), `finished_at` (timestamptz, nullable).
- FK `agent_runs.chat_id` → `chats.id`, `onDelete: Cascade`.
- Composite FK `(chat_id, assistant_message_id)` → `chat_messages (chat_id, id)` (the alternate key), `onDelete: Cascade`.
- Unique index on `assistant_message_id`; an index on `chat_id` (auto-created for the FK).
- `CreateTable "agent_run_activities"`: `id` (uuid, PK), `run_id` (uuid), `sequence` (integer), `kind` (text), `type` (character varying(100)), `title` (character varying(300)), `detail` (jsonb, nullable), `occurred_at` (timestamptz).
- FK `run_id` → `agent_runs.id`, `onDelete: Cascade`; unique index on `(run_id, sequence)`.

If the composite FK is missing or points at the wrong principal, the `ChatMessage` alternate key declaration in `ChatMessageConfiguration` (`HasAlternateKey(x => new { x.ChatId, x.Id })`) was not picked up — re-check the `HasPrincipalKey` call in Task 5 Step 1.

- [ ] **Step 3: Build to confirm the migration compiles**

Run: `dotnet build Nova.slnx`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Database/Migrations
git commit -m "feat(chat): add agent runs migration"
```

---

## Verification (whole PR)

1. `dotnet build Nova.slnx` — clean.
2. `dotnet test tests/Chat/Chat.Domain.Tests/Chat.Domain.Tests.csproj` — all pass (new AgentRuns suites + existing suites).
3. `git diff origin/main --stat` — changes confined to `Chat.Domain/AgentRuns`, `Chat.Infrastructure/AgentRuns`, `ChatDbContext.cs`, `DependencyInjection.cs`, `Database/Migrations`, tests, and the two docs.
4. Nothing references `AgentRun` outside the new files (inert PR): `grep -r "AgentRun" src --include="*.cs" -l` shows only the new domain/infrastructure files plus `ChatDbContext.cs` and `DependencyInjection.cs`.
