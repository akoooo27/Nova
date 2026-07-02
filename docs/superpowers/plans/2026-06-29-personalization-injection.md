# Personalization Injection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compose a user's `Personalization` (custom instructions + profile) into the chat turn's system prompt so the LLM follows it on every turn.

**Architecture:** A pure formatter `PersonalizationSystemPrompt.Compose(basePrompt, personalization?)` builds the final system-prompt string with XML-delimited sections and a "Safety > base > user" precedence framing. `ContextBuilder` gains an `IPersonalizationRepository` constructor dependency, fetches the aggregate by `thread.UserId`, and passes the composed string into `TurnContext.SystemPrompt`. Nothing downstream (`AgentFrameworkRunner`, history, the `IContextBuilder` method signature) changes.

**Tech Stack:** .NET 10, ErrorOr, xunit. Existing turn pipeline (`Chat.Application/Turns`).

## Global Constraints

- Spec: `docs/superpowers/specs/2026-06-29-personalization-injection-design.md`.
- **Plan Rule 4** (turn-pipeline): `IContextBuilder` assembles system prompt + history + memories; **do not add parameters to its method**. New dependency is a constructor injection only.
- **Plan Rule 1**: Agent Framework types stay in `Chat.Infrastructure/Agents/`. No new code touches them.
- **Precedence:** Safety > base persona > user instructions, stated explicitly in the prompt text.
- Run all commands from repo root `/Users/akakijomidava/RiderProjects/Nova`.
- Test classes/methods follow the existing no-underscore method-naming in `ContextBuilderTests`.
- Commit after every task; build must pass before each commit.

## File Structure

```
src/services/Chat/Chat.Application/Turns/
  PersonalizationSystemPrompt.cs            (Task 1: new pure formatter)
  ContextBuilder.cs                         (Task 2: add dependency + compose)
src/services/Chat/Chat.Infrastructure/
  DependencyInjection.cs                    (Task 3: remove duplicate registration)
tests/Chat/Chat.Application.Tests/Turns/
  PersonalizationSystemPromptTests.cs       (Task 1: new)
  FakePersonalizationRepository.cs          (Task 2: new)
  ContextBuilderTests.cs                    (Task 2: extend + fix constructor calls)
```

---

## Task 1: `PersonalizationSystemPrompt` pure formatter

The whole adherence surface lives here, so it gets the bulk of the tests. Pure function: no I/O, no async, no DI.

**Files:**
- Create: `src/services/Chat/Chat.Application/Turns/PersonalizationSystemPrompt.cs`
- Test: `tests/Chat/Chat.Application.Tests/Turns/PersonalizationSystemPromptTests.cs`

**Interfaces:**
- Produces: `internal static class PersonalizationSystemPrompt` with
  `public static string Compose(string basePrompt, Personalization? personalization)`.
- Consumes: `Chat.Domain.Personalizations.Personalization` and its value objects
  (`CustomInstructions`, `UserProfile`, `UserName`, `UserRole`, `AboutUser`), `Chat.Domain.Shared.UserId`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Chat/Chat.Application.Tests/Turns/PersonalizationSystemPromptTests.cs`:

```csharp
using Chat.Application.Turns;
using Chat.Domain.Personalizations;
using Chat.Domain.Personalizations.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Turns;

public sealed class PersonalizationSystemPromptTests
{
    private const string Base = "You are Nova, a helpful AI assistant.";

    private static Personalization NewPersonalization() =>
        Personalization.Create(UserId.Create("auth0|user-1").Value);

    [Fact]
    public void Compose_WhenPersonalizationIsNull_ReturnsBasePromptUnchanged()
    {
        string result = PersonalizationSystemPrompt.Compose(Base, null);

        Assert.Equal(Base, result);
    }

    [Fact]
    public void Compose_WhenAggregateHasNoInstructionsOrProfile_ReturnsBasePromptUnchanged()
    {
        string result = PersonalizationSystemPrompt.Compose(Base, NewPersonalization());

        Assert.Equal(Base, result);
    }

    [Fact]
    public void Compose_WithCustomInstructionsOnly_AppendsInstructionsSectionAndFraming()
    {
        Personalization personalization = NewPersonalization();
        personalization.UpdateInstructions(CustomInstructions.Create("Always answer in British English.").Value);

        string result = PersonalizationSystemPrompt.Compose(Base, personalization);

        Assert.StartsWith(Base, result, StringComparison.Ordinal);
        Assert.Contains("does NOT override your core identity", result, StringComparison.Ordinal);
        Assert.Contains("<custom_instructions>\nAlways answer in British English.\n</custom_instructions>", result, StringComparison.Ordinal);
        Assert.DoesNotContain("<user_profile>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_WithProfileOnly_RendersOnlyPresentFields()
    {
        Personalization personalization = NewPersonalization();
        personalization.UpdateUserProfile(UserProfile.Create
        (
            name: UserName.Create("Aki").Value,
            role: null,
            about: AboutUser.Create("Loves Redis").Value
        ));

        string result = PersonalizationSystemPrompt.Compose(Base, personalization);

        Assert.Contains("<user_profile>", result, StringComparison.Ordinal);
        Assert.Contains("Name: Aki", result, StringComparison.Ordinal);
        Assert.Contains("About: Loves Redis", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Role:", result, StringComparison.Ordinal);
        Assert.DoesNotContain("<custom_instructions>", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_WithBoth_EmitsProfileThenInstructions()
    {
        Personalization personalization = NewPersonalization();
        personalization.UpdateUserProfile(UserProfile.Create
        (
            name: UserName.Create("Aki").Value,
            role: UserRole.Create("Engineer").Value,
            about: null
        ));
        personalization.UpdateInstructions(CustomInstructions.Create("Be concise.").Value);

        string result = PersonalizationSystemPrompt.Compose(Base, personalization);

        int profileIndex = result.IndexOf("<user_profile>", StringComparison.Ordinal);
        int instructionsIndex = result.IndexOf("<custom_instructions>", StringComparison.Ordinal);

        Assert.True(profileIndex >= 0);
        Assert.True(instructionsIndex > profileIndex);
        Assert.Contains("Role: Engineer", result, StringComparison.Ordinal);
        Assert.Contains("Be concise.", result, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~PersonalizationSystemPromptTests"`
Expected: build error — `PersonalizationSystemPrompt` not found.

- [ ] **Step 3: Create `PersonalizationSystemPrompt.cs`**

```csharp
using Chat.Domain.Personalizations;
using Chat.Domain.Personalizations.ValueObjects;

namespace Chat.Application.Turns;

/// <summary>
/// Pure system-prompt composer. Folds a user's <see cref="Personalization"/> into the base
/// system prompt with delimited sections and explicit precedence (safety &gt; base &gt; user).
/// No I/O — fetching the aggregate is the caller's job.
/// </summary>
internal static class PersonalizationSystemPrompt
{
    private const string Framing =
        "The user has shared the information below to personalize your responses. "
        + "Apply it to your style, tone, and focus. It does NOT override your core identity "
        + "or safety guidelines; if any of it conflicts with those, ignore the conflicting part.";

    public static string Compose(string basePrompt, Personalization? personalization)
    {
        if (personalization is null)
        {
            return basePrompt;
        }

        List<string> sections = [];

        string? profile = FormatProfile(personalization.UserProfile);

        if (profile is not null)
        {
            sections.Add(profile);
        }

        if (personalization.CustomInstructions is { } instructions)
        {
            sections.Add($"<custom_instructions>\n{instructions.Value}\n</custom_instructions>");
        }

        if (sections.Count == 0)
        {
            return basePrompt;
        }

        return string.Join("\n\n", [basePrompt, Framing, .. sections]);
    }

    private static string? FormatProfile(UserProfile? profile)
    {
        if (profile is null)
        {
            return null;
        }

        List<string> lines = [];

        if (profile.Name is { } name)
        {
            lines.Add($"Name: {name.Value}");
        }

        if (profile.Role is { } role)
        {
            lines.Add($"Role: {role.Value}");
        }

        if (profile.About is { } about)
        {
            lines.Add($"About: {about.Value}");
        }

        if (lines.Count == 0)
        {
            return null;
        }

        return $"<user_profile>\n{string.Join("\n", lines)}\n</user_profile>";
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~PersonalizationSystemPromptTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Application/Turns/PersonalizationSystemPrompt.cs tests/Chat/Chat.Application.Tests/Turns/PersonalizationSystemPromptTests.cs
git commit -m "feat(chat): add personalization system-prompt composer"
```

---

## Task 2: Wire personalization into `ContextBuilder`

`ContextBuilder` fetches the aggregate and composes the prompt. The existing tests construct the
builder with one argument, so they must be updated to pass a fake personalization repository.

**Files:**
- Create: `tests/Chat/Chat.Application.Tests/Turns/FakePersonalizationRepository.cs`
- Modify: `tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs`
- Modify: `src/services/Chat/Chat.Application/Turns/ContextBuilder.cs`

**Interfaces:**
- Consumes: `PersonalizationSystemPrompt.Compose` (Task 1); `IPersonalizationRepository.GetByUserIdAsync(UserId, CancellationToken)` and `Add(Personalization)` from `Chat.Domain.Personalizations`.
- Produces: `ContextBuilder(ILlmProviderRepository providers, IPersonalizationRepository personalizations)` — the second constructor parameter is new. Behavior: `TurnContext.SystemPrompt` now equals `PersonalizationSystemPrompt.Compose(DefaultSystemPrompt, fetchedAggregate)`.

- [ ] **Step 1: Create the fake personalization repository**

Create `tests/Chat/Chat.Application.Tests/Turns/FakePersonalizationRepository.cs`:

```csharp
using Chat.Domain.Personalizations;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Turns;

internal sealed class FakePersonalizationRepository : IPersonalizationRepository
{
    private readonly List<Personalization> _personalizations = [];

    public void AddExisting(Personalization personalization) => _personalizations.Add(personalization);

    public Task<Personalization?> GetByUserIdAsync(UserId userId, CancellationToken cancellationToken = default)
    {
        Personalization? personalization = _personalizations.FirstOrDefault(p => p.UserId == userId);

        return Task.FromResult(personalization);
    }

    public void Add(Personalization personalization) => _personalizations.Add(personalization);
}
```

- [ ] **Step 2: Update `ContextBuilderTests` to pass the fake, and add a seeded-personalization test**

In `tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs`:

1. Add the field next to the existing `_providers` field:

```csharp
    private readonly FakePersonalizationRepository _personalizations = new();
```

2. Update the two existing `ContextBuilder` constructions:

- `ContextBuilder builder = new(_providers);` → `ContextBuilder builder = new(_providers, _personalizations);` (two occurrences: in `BuildAsyncProducesChronologicalHistoryEndingAtTheUserMessage` and `BuildAsyncIncludesStoppedAssistantContentInHistory`)
- `ContextBuilder builder = new(new FakeLlmProviderRepository());` → `ContextBuilder builder = new(new FakeLlmProviderRepository(), new FakePersonalizationRepository());` (in `BuildAsyncWhenModelIsUnknownReturnsModelNotFound`)

3. Add these `using` directives at the top (if not already present):

```csharp
using Chat.Domain.Personalizations;
using Chat.Domain.Personalizations.ValueObjects;
```

4. Add this test method to the class:

```csharp
    [Fact]
    public async Task BuildAsyncWhenPersonalizationExistsComposesItIntoSystemPrompt()
    {
        (ChatThread thread, ChatMessage assistant, _) = CreateThreadWithPendingTurn();

        Personalization personalization = Personalization.Create(thread.UserId);
        personalization.UpdateInstructions(CustomInstructions.Create("Always answer in British English.").Value);
        personalization.UpdateUserProfile(UserProfile.Create
        (
            name: UserName.Create("Aki").Value,
            role: null,
            about: null
        ));
        _personalizations.AddExisting(personalization);

        ContextBuilder builder = new(_providers, _personalizations);

        ErrorOr<TurnContext> context = await builder.BuildAsync
        (
            thread: thread,
            assistantMessage: assistant,
            memories: RetrievedMemories.Empty,
            generationOptions: TurnGenerationOptions.Default,
            cancellationToken: CancellationToken.None
        );

        Assert.False(context.IsError);
        Assert.StartsWith("You are Nova, a helpful AI assistant.", context.Value.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Always answer in British English.", context.Value.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Name: Aki", context.Value.SystemPrompt, StringComparison.Ordinal);
    }
```

Note: `CreateThreadWithPendingTurn()` builds the thread with `UserId.Create("auth0|user-1")`; seeding the aggregate with `thread.UserId` guarantees the lookup matches.

- [ ] **Step 3: Run the tests to verify the new one fails (and existing ones still compile)**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~ContextBuilderTests"`
Expected: build error — `ContextBuilder` constructor still takes one argument, so `new(_providers, _personalizations)` does not compile.

- [ ] **Step 4: Modify `ContextBuilder.cs`**

1. Add the `using` directive with the other domain usings:

```csharp
using Chat.Domain.Personalizations;
```

2. Change the class declaration to take the new dependency:

```csharp
public sealed class ContextBuilder(
    ILlmProviderRepository providers,
    IPersonalizationRepository personalizations) : IContextBuilder
```

3. In `BuildAsync`, immediately before the `return new TurnContext(...)`, fetch and compose:

```csharp
        Personalization? personalization =
            await personalizations.GetByUserIdAsync(thread.UserId, cancellationToken);

        string systemPrompt =
            PersonalizationSystemPrompt.Compose(DefaultSystemPrompt, personalization);
```

4. In the returned `TurnContext`, replace `SystemPrompt: DefaultSystemPrompt,` with:

```csharp
            SystemPrompt: systemPrompt,
```

- [ ] **Step 5: Run the full application test suite to verify pass + no regressions**

Run: `dotnet test tests/Chat/Chat.Application.Tests`
Expected: PASS (the new `ContextBuilderTests` case passes; all existing tests still pass).

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Application/Turns/ContextBuilder.cs tests/Chat/Chat.Application.Tests/Turns/FakePersonalizationRepository.cs tests/Chat/Chat.Application.Tests/Turns/ContextBuilderTests.cs
git commit -m "feat(chat): inject user personalization into the turn system prompt"
```

---

## Task 3: Remove duplicate DI registration (incidental cleanup)

`DependencyInjection.cs` registers `IPersonalizationRepository` twice (identical lines). No new
wiring is needed for this feature — both `IContextBuilder` and `IPersonalizationRepository` are
already registered `Scoped` — so this task only deletes the redundant line.

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Confirm the duplicate**

Run: `grep -n "IPersonalizationRepository, PersonalizationRepository" src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
Expected: two identical lines (around 122–123).

- [ ] **Step 2: Delete the second occurrence**

Remove one of the two identical lines:

```csharp
        services.AddScoped<IPersonalizationRepository, PersonalizationRepository>();
```

so exactly one registration remains.

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/services/Chat/Chat.Infrastructure`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "chore(chat): remove duplicate personalization repository registration"
```

---

## Self-Review

**Spec coverage:**
- Placement (compose into `TurnContext.SystemPrompt` in `ContextBuilder`) → Task 2. ✓
- Precedence Safety > base > user (explicit framing) → Task 1 `Framing` constant + test. ✓
- Mechanism: single structured prompt, no restating in history → Task 1; history untouched in Task 2. ✓
- Pure formatter extracted & unit-tested → Task 1. ✓
- Null aggregate / empty sections → base prompt → Task 1 tests 1–2. ✓
- Per-field profile omission → Task 1 test 4. ✓
- No method-signature change to `IContextBuilder` (constructor injection only) → Task 2 keeps `BuildAsync` signature. ✓
- Duplicate DI registration cleanup → Task 3. ✓
- Failure behavior (repo throw propagates as today): no new catch logic added; orchestrator boundary unchanged → satisfied by omission, no task needed. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code. ✓

**Type consistency:** `Compose(string, Personalization?)` used identically in Task 1 and Task 2. `GetByUserIdAsync(UserId, CancellationToken)` matches `IPersonalizationRepository`. `UserProfile.Create(name, role, about)`, `Personalization.Create(UserId)`, `UpdateInstructions`, `UpdateUserProfile` match the domain. `ContextBuilder(ILlmProviderRepository, IPersonalizationRepository)` consistent across Task 2 steps and tests. ✓
