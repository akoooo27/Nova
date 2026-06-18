# Chat Thread Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a ChatGPT-style sparse `PATCH /chats/{chatId}` endpoint that updates chat thread metadata and returns the full updated metadata state.

**Architecture:** Metadata changes go through one FastEndpoints endpoint, one `Mediator` command, and `ChatThread` aggregate methods. The request is presence-aware so omitted fields remain unchanged, while explicit `null` and unknown fields are rejected. Visibility is separate from archive: hidden chats get `HiddenAt` and become eligible for a cleanup worker after 30 days; archived chats are retained unless explicitly hidden.

**Tech Stack:** .NET 10, FastEndpoints, Mediator.SourceGenerator / Mediator.Abstractions, ErrorOr, EF Core 10 + Npgsql, FluentValidation, existing clean architecture layers.

**Spec:** `docs/superpowers/specs/2026-06-15-chat-thread-update-design.md`

**Testing note:** `AGENTS.md` says not to write, modify, or expand tests unless the user explicitly requests test work. This plan uses build/manual verification only. Before adding tests for any task, ask the user for approval.

**Command note:** `AGENTS.md` requires elevated permission before running `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet run`, EF migrations, or similar .NET commands.

---

## File Structure

**New files**
- `src/services/Chat/Chat.Application/Chats/Commands/UpdateChatThread/UpdateChatThreadField.cs` — small presence-aware field wrapper used by the command.
- `src/services/Chat/Chat.Application/Chats/Commands/UpdateChatThread/UpdateChatThreadCommand.cs` — command carrying only the update intent.
- `src/services/Chat/Chat.Application/Chats/Commands/UpdateChatThread/UpdateChatThreadCommandValidator.cs` — route id and non-empty patch validation.
- `src/services/Chat/Chat.Application/Chats/Commands/UpdateChatThread/UpdateChatThreadHandler.cs` — loads the authenticated user's chat, applies present fields, saves, returns metadata.
- `src/services/Chat/Chat.Application/Chats/Results/ChatThreadResult.cs` — full chat metadata result returned by the handler.
- `src/services/Chat/Chat.Application/Chats/Results/ChatThreadResultMapper.cs` — maps `ChatThread` to `ChatThreadResult`.
- `src/services/Chat/Chat.Api/Endpoints/Chats/UpdateChatThread/Endpoint.cs` — `PATCH /chats/{chatId}` endpoint and sparse JSON parser.
- `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatThreadResponse.cs` — API response DTO and mapper.
- `src/services/Chat/Chat.Application/Chats/Commands/CleanupHiddenChats/CleanupHiddenChatsCommand.cs` — cleanup command for hidden chats.
- `src/services/Chat/Chat.Application/Chats/Commands/CleanupHiddenChats/CleanupHiddenChatsHandler.cs` — computes hidden cutoff and calls repository.

**Modified files**
- `src/services/Chat/Chat.Domain/Chats/ChatThread.cs` — add visibility state and aggregate metadata methods.
- `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs` — add hidden-chat bulk delete method.
- `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatThreadConfiguration.cs` — map `IsVisible` and `HiddenAt`, add cleanup index.
- `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs` — implement hidden-chat bulk delete.
- EF-generated migration file under `src/services/Chat/Chat.Infrastructure/Database/Migrations/` named `*ChatThreadVisibility.cs` — migration for `is_visible`, `hidden_at`, and cleanup index.
- `src/workers/Chat.CleanupWorker/TemporaryChatCleanupJob.cs` — extend the existing scheduled job once that worker exists.

---

## Task 1: Add Visibility State and Aggregate Methods

**Files:**
- Modify: `src/services/Chat/Chat.Domain/Chats/ChatThread.cs`

- [ ] **Step 1: Add properties**

In `ChatThread`, add these properties after `IsTemporary`:

```csharp
public bool IsVisible { get; private set; }

public DateTimeOffset? HiddenAt { get; private set; }
```

- [ ] **Step 2: Initialize new chats as visible**

In the private constructor used by `Create`, after `IsTemporary = isTemporary;`, add:

```csharp
IsVisible = true;
```

The EF materialization constructor remains unchanged.

- [ ] **Step 3: Add metadata methods**

Add these methods near the existing `Pin`, `Unpin`, `Archive`, and `Unarchive` methods:

```csharp
public void Rename(ChatTitle title) =>
    Title = title;

public void Pin(DateTimeOffset pinnedAt) =>
    PinnedAt ??= pinnedAt;

public void Unpin() =>
    PinnedAt = null;

public void Archive() =>
    IsArchived = true;

public void Unarchive() =>
    IsArchived = false;

public void Hide(DateTimeOffset hiddenAt)
{
    if (!IsVisible)
    {
        return;
    }

    IsVisible = false;
    HiddenAt = hiddenAt;
}

public void Show()
{
    IsVisible = true;
    HiddenAt = null;
}
```

If `Pin`, `Unpin`, `Archive`, or `Unarchive` already exist, keep the existing behavior and add only `Rename`, `Hide`, and `Show`.

- [ ] **Step 4: Verify domain build**

Run with elevated permission:

```bash
dotnet build src/services/Chat/Chat.Domain/Chat.Domain.csproj
```

Expected: `Build succeeded.` with 0 errors.

---

## Task 2: Persist Visibility State

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Configurations/ChatThreadConfiguration.cs`
- Create: EF-generated `src/services/Chat/Chat.Infrastructure/Database/Migrations/*ChatThreadVisibility.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

- [ ] **Step 1: Add EF mapping**

In `ChatThreadConfiguration`, after the `IsTemporary` mapping, add:

```csharp
builder.Property(x => x.IsVisible)
    .IsRequired();

builder.Property(x => x.HiddenAt);
```

After the existing `builder.HasIndex(x => new { x.UserId, x.UpdatedAt, x.Id })...` block, add:

```csharp
builder.HasIndex(x => x.HiddenAt)
    .HasFilter("is_visible = false AND hidden_at IS NOT NULL")
    .HasDatabaseName("ix_chats_hidden_cleanup");
```

- [ ] **Step 2: Create migration**

Run with elevated permission:

```bash
dotnet ef migrations add ChatThreadVisibility \
  --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj \
  --startup-project src/workers/Chat.MigrationWorker/Chat.MigrationWorker.csproj \
  --context ChatDbContext \
  --output-dir Database/Migrations
```

Expected: a migration file under `src/services/Chat/Chat.Infrastructure/Database/Migrations/` and an updated `ChatDbContextModelSnapshot.cs`.

- [ ] **Step 3: Verify migration content**

The migration `Up` method must include:

```csharp
migrationBuilder.AddColumn<DateTimeOffset>(
    name: "hidden_at",
    table: "chats",
    type: "timestamp with time zone",
    nullable: true);

migrationBuilder.AddColumn<bool>(
    name: "is_visible",
    table: "chats",
    type: "boolean",
    nullable: false,
    defaultValue: true);

migrationBuilder.CreateIndex(
    name: "ix_chats_hidden_cleanup",
    table: "chats",
    column: "hidden_at",
    filter: "is_visible = false AND hidden_at IS NOT NULL");
```

The migration `Down` method must drop the index, then drop `hidden_at` and `is_visible`.

- [ ] **Step 4: Verify infrastructure build**

Run with elevated permission:

```bash
dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj
```

Expected: `Build succeeded.` with 0 errors.

---

## Task 3: Add Chat Metadata Result

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Results/ChatThreadResult.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Results/ChatThreadResultMapper.cs`

- [ ] **Step 1: Add result record**

Create `ChatThreadResult.cs`:

```csharp
namespace Chat.Application.Chats.Results;

public sealed record ChatThreadResult
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsVisible,
    DateTimeOffset? HiddenAt,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
```

- [ ] **Step 2: Add mapper**

Create `ChatThreadResultMapper.cs`:

```csharp
using Chat.Domain.Chats;

namespace Chat.Application.Chats.Results;

public static class ChatThreadResultMapper
{
    public static ChatThreadResult ToResult(this ChatThread thread) => new
    (
        Id: thread.Id.Value,
        Title: thread.Title.Value,
        IsPinned: thread.IsPinned,
        PinnedAt: thread.PinnedAt,
        IsArchived: thread.IsArchived,
        IsVisible: thread.IsVisible,
        HiddenAt: thread.HiddenAt,
        IsTemporary: thread.IsTemporary,
        CreatedAt: thread.CreatedAt,
        UpdatedAt: thread.UpdatedAt
    );
}
```

- [ ] **Step 3: Verify application build**

Run with elevated permission:

```bash
dotnet build src/services/Chat/Chat.Application/Chat.Application.csproj
```

Expected: `Build succeeded.` with 0 errors.

---

## Task 4: Add Update Command and Handler

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Commands/UpdateChatThread/UpdateChatThreadField.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/UpdateChatThread/UpdateChatThreadCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/UpdateChatThread/UpdateChatThreadCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/UpdateChatThread/UpdateChatThreadHandler.cs`

- [ ] **Step 1: Add presence-aware field wrapper**

Create `UpdateChatThreadField.cs`:

```csharp
namespace Chat.Application.Chats.Commands.UpdateChatThread;

public readonly record struct UpdateChatThreadField<T>
{
    private UpdateChatThreadField(bool isPresent, T value)
    {
        IsPresent = isPresent;
        Value = value;
    }

    public bool IsPresent { get; }

    public T Value { get; }

    public static UpdateChatThreadField<T> Missing => new(false, default!);

    public static UpdateChatThreadField<T> Present(T value) => new(true, value);
}
```

- [ ] **Step 2: Add command**

Create `UpdateChatThreadCommand.cs`:

```csharp
using Chat.Application.Chats.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.UpdateChatThread;

public sealed record UpdateChatThreadCommand
(
    Guid ChatId,
    UpdateChatThreadField<string> Title,
    UpdateChatThreadField<bool> IsPinned,
    UpdateChatThreadField<bool> IsArchived,
    UpdateChatThreadField<bool> IsVisible
) : ICommand<ErrorOr<ChatThreadResult>>
{
    public bool HasChanges =>
        Title.IsPresent ||
        IsPinned.IsPresent ||
        IsArchived.IsPresent ||
        IsVisible.IsPresent;
}
```

- [ ] **Step 3: Add command validator**

Create `UpdateChatThreadCommandValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.Chats.Commands.UpdateChatThread;

internal sealed class UpdateChatThreadCommandValidator : AbstractValidator<UpdateChatThreadCommand>
{
    public UpdateChatThreadCommandValidator()
    {
        RuleFor(x => x.ChatId)
            .NotEmpty();

        RuleFor(x => x.HasChanges)
            .Equal(true)
            .WithMessage("At least one chat field must be supplied.");
    }
}
```

- [ ] **Step 4: Add handler**

Create `UpdateChatThreadHandler.cs`:

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.Chats.Errors;
using Chat.Application.Chats.Results;
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.Chats.Commands.UpdateChatThread;

internal sealed class UpdateChatThreadHandler(
    IUserContext userContext,
    IChatRepository chats,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<UpdateChatThreadCommand, ErrorOr<ChatThreadResult>>
{
    public async ValueTask<ErrorOr<ChatThreadResult>> Handle
    (
        UpdateChatThreadCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<ChatId> chatIdResult = ChatId.Create(command.ChatId);
        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (chatIdResult.IsError)
        {
            errors.AddRange(chatIdResult.Errors);
        }

        ChatTitle? title = null;

        if (command.Title.IsPresent)
        {
            ErrorOr<ChatTitle> titleResult = ChatTitle.Create(command.Title.Value);

            if (titleResult.IsError)
            {
                errors.AddRange(titleResult.Errors);
            }
            else
            {
                title = titleResult.Value;
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        ChatId chatId = chatIdResult.Value;
        UserId userId = userIdResult.Value;

        ChatThread? thread = await chats.GetByIdAsync
        (
            id: chatId,
            userId: userId,
            cancellationToken: cancellationToken
        );

        if (thread is null)
        {
            return ChatOperationErrors.ChatNotFound(chatId);
        }

        DateTimeOffset now = dateTimeProvider.UtcNow;

        if (title is not null)
        {
            thread.Rename(title);
        }

        if (command.IsPinned.IsPresent)
        {
            if (command.IsPinned.Value)
            {
                thread.Pin(now);
            }
            else
            {
                thread.Unpin();
            }
        }

        if (command.IsArchived.IsPresent)
        {
            if (command.IsArchived.Value)
            {
                thread.Archive();
            }
            else
            {
                thread.Unarchive();
            }
        }

        if (command.IsVisible.IsPresent)
        {
            if (command.IsVisible.Value)
            {
                thread.Show();
            }
            else
            {
                thread.Hide(now);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return thread.ToResult();
    }
}
```

- [ ] **Step 5: Verify application build**

Run with elevated permission:

```bash
dotnet build src/services/Chat/Chat.Application/Chat.Application.csproj
```

Expected: `Build succeeded.` with 0 errors.

---

## Task 5: Add API Response DTO

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/Responses/ChatThreadResponse.cs`

- [ ] **Step 1: Add response record and mapper**

Create `ChatThreadResponse.cs`:

```csharp
using Chat.Application.Chats.Results;

namespace Chat.Api.Endpoints.Chats.Responses;

internal sealed record ChatThreadResponse
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsVisible,
    DateTimeOffset? HiddenAt,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
)
{
    public static ChatThreadResponse From(ChatThreadResult result) => new
    (
        Id: result.Id,
        Title: result.Title,
        IsPinned: result.IsPinned,
        PinnedAt: result.PinnedAt,
        IsArchived: result.IsArchived,
        IsVisible: result.IsVisible,
        HiddenAt: result.HiddenAt,
        IsTemporary: result.IsTemporary,
        CreatedAt: result.CreatedAt,
        UpdatedAt: result.UpdatedAt
    );
}
```

- [ ] **Step 2: Verify API build**

Run with elevated permission:

```bash
dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj
```

Expected: `Build succeeded.` with 0 errors.

---

## Task 6: Add Sparse PATCH Endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/UpdateChatThread/Endpoint.cs`

- [ ] **Step 1: Add endpoint with raw JSON parsing**

Create `Endpoint.cs`:

```csharp
using System.Text.Json;

using Chat.Api.Endpoints.Chats.Responses;
using Chat.Application.Chats.Commands.UpdateChatThread;
using Chat.Application.Chats.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.UpdateChatThread;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<ChatThreadResponse>
{
    public const string RouteName = "Chat.Chats.Update";

    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "title",
        "isPinned",
        "isArchived",
        "isVisible"
    };

    public override void Configure()
    {
        Patch("/chats/{chatId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Update Chat")
                .WithDescription("Updates chat metadata using a sparse patch and returns the full updated chat state.")
                .Produces<ChatThreadResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<UpdateChatThreadPatch> patchResult = await ParsePatchAsync(HttpContext.Request.Body, ct);

        if (patchResult.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(patchResult));
            return;
        }

        UpdateChatThreadPatch patch = patchResult.Value;

        UpdateChatThreadCommand command = new
        (
            ChatId: Route<Guid>("chatId"),
            Title: patch.Title,
            IsPinned: patch.IsPinned,
            IsArchived: patch.IsArchived,
            IsVisible: patch.IsVisible
        );

        ErrorOr<ChatThreadResult> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.OkAsync(ChatThreadResponse.From(result.Value), ct);
    }

    private static async Task<ErrorOr<UpdateChatThreadPatch>> ParsePatchAsync
    (
        Stream body,
        CancellationToken cancellationToken
    )
    {
        JsonDocument document;

        try
        {
            document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Error.Validation
            (
                code: "ChatPatch.InvalidJson",
                description: "Request body must be a valid JSON object."
            );
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Error.Validation
                (
                    code: "ChatPatch.InvalidShape",
                    description: "Request body must be a JSON object."
                );
            }

            UpdateChatThreadField<string> title = UpdateChatThreadField<string>.Missing;
            UpdateChatThreadField<bool> isPinned = UpdateChatThreadField<bool>.Missing;
            UpdateChatThreadField<bool> isArchived = UpdateChatThreadField<bool>.Missing;
            UpdateChatThreadField<bool> isVisible = UpdateChatThreadField<bool>.Missing;
            int suppliedFields = 0;

            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!AllowedProperties.Contains(property.Name))
                {
                    return Error.Validation
                    (
                        code: "ChatPatch.UnknownField",
                        description: $"Unknown chat patch field '{property.Name}'."
                    );
                }

                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    return Error.Validation
                    (
                        code: "ChatPatch.NullValue",
                        description: $"Chat patch field '{property.Name}' cannot be null."
                    );
                }

                suppliedFields++;

                switch (property.Name)
                {
                    case "title":
                        if (property.Value.ValueKind != JsonValueKind.String)
                        {
                            return InvalidType(property.Name, "string");
                        }

                        title = UpdateChatThreadField<string>.Present(property.Value.GetString()!);
                        break;

                    case "isPinned":
                        if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                        {
                            return InvalidType(property.Name, "boolean");
                        }

                        isPinned = UpdateChatThreadField<bool>.Present(property.Value.GetBoolean());
                        break;

                    case "isArchived":
                        if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                        {
                            return InvalidType(property.Name, "boolean");
                        }

                        isArchived = UpdateChatThreadField<bool>.Present(property.Value.GetBoolean());
                        break;

                    case "isVisible":
                        if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                        {
                            return InvalidType(property.Name, "boolean");
                        }

                        isVisible = UpdateChatThreadField<bool>.Present(property.Value.GetBoolean());
                        break;
                }
            }

            if (suppliedFields == 0)
            {
                return Error.Validation
                (
                    code: "ChatPatch.Empty",
                    description: "At least one chat field must be supplied."
                );
            }

            return new UpdateChatThreadPatch(title, isPinned, isArchived, isVisible);
        }
    }

    private static Error InvalidType(string fieldName, string expectedType) =>
        Error.Validation
        (
            code: "ChatPatch.InvalidType",
            description: $"Chat patch field '{fieldName}' must be a {expectedType}."
        );

    private sealed record UpdateChatThreadPatch
    (
        UpdateChatThreadField<string> Title,
        UpdateChatThreadField<bool> IsPinned,
        UpdateChatThreadField<bool> IsArchived,
        UpdateChatThreadField<bool> IsVisible
    );
}
```

- [ ] **Step 2: Verify API build**

Run with elevated permission:

```bash
dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj
```

Expected: `Build succeeded.` with 0 errors.

---

## Task 7: Add Hidden-Chat Cleanup Command and Repository Method

**Files:**
- Modify: `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CleanupHiddenChats/CleanupHiddenChatsCommand.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Commands/CleanupHiddenChats/CleanupHiddenChatsHandler.cs`

- [ ] **Step 1: Add repository method**

In `IChatRepository.cs`, add:

```csharp
    Task<int> DeleteHiddenChatsAsync
    (
        DateTimeOffset hiddenBefore,
        CancellationToken cancellationToken = default
    );
```

- [ ] **Step 2: Implement repository method**

In `ChatRepository.cs`, add:

```csharp
    public async Task<int> DeleteHiddenChatsAsync
    (
        DateTimeOffset hiddenBefore,
        CancellationToken cancellationToken = default
    )
    {
        return await db.ChatThreads
            .Where(chat => !chat.IsVisible && chat.HiddenAt < hiddenBefore)
            .ExecuteDeleteAsync(cancellationToken);
    }
```

This predicate intentionally does not reference `IsArchived`. Visible archived chats are retained.

- [ ] **Step 3: Add cleanup command**

Create `CleanupHiddenChatsCommand.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Commands.CleanupHiddenChats;

public sealed record CleanupHiddenChatsCommand(TimeSpan RetentionPeriod)
    : ICommand<ErrorOr<int>>;
```

- [ ] **Step 4: Add cleanup handler**

Create `CleanupHiddenChatsHandler.cs`:

```csharp
using Chat.Domain.Chats;

using ErrorOr;

using Mediator;

using SharedKernel;

namespace Chat.Application.Chats.Commands.CleanupHiddenChats;

internal sealed class CleanupHiddenChatsHandler(
    IChatRepository chats,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<CleanupHiddenChatsCommand, ErrorOr<int>>
{
    public async ValueTask<ErrorOr<int>> Handle
    (
        CleanupHiddenChatsCommand command,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset hiddenBefore = dateTimeProvider.UtcNow - command.RetentionPeriod;

        int deletedCount = await chats.DeleteHiddenChatsAsync(hiddenBefore, cancellationToken);

        return deletedCount;
    }
}
```

- [ ] **Step 5: Verify application and infrastructure builds**

Run with elevated permission:

```bash
dotnet build src/services/Chat/Chat.Application/Chat.Application.csproj
dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj
```

Expected: both builds end with `Build succeeded.` and 0 errors.

---

## Task 8: Wire Hidden Cleanup into the Cleanup Worker

**Files:**
- Modify: `src/workers/Chat.CleanupWorker/TemporaryChatCleanupJob.cs`

- [ ] **Step 1: Confirm prerequisite**

Before this task, confirm the cleanup worker from `docs/superpowers/plans/2026-06-15-temporary-chat-cleanup.md` exists in the worktree:

```bash
rg --files src/workers src/services/Chat | rg "Chat.CleanupWorker|TemporaryChatCleanupJob|TemporaryChatCleanupOptions"
```

Expected: the command prints worker files such as `src/workers/Chat.CleanupWorker/TemporaryChatCleanupJob.cs`.

If it prints nothing, stop this task. Complete `docs/superpowers/plans/2026-06-15-temporary-chat-cleanup.md` first, then rerun the prerequisite command above.

- [ ] **Step 2: Extend the cleanup job**

In `TemporaryChatCleanupJob`, after sending `CleanupExpiredTemporaryChatsCommand`, also send:

```csharp
ErrorOr<int> hiddenResult = await sender.Send
(
    new CleanupHiddenChatsCommand(options.RetentionPeriod),
    cancellationToken
);

if (hiddenResult.IsError)
{
    logger.LogWarning
    (
        "Hidden chat cleanup failed with errors: {Errors}",
        hiddenResult.Errors
    );

    return;
}

logger.LogInformation("Deleted {DeletedCount} hidden chats.", hiddenResult.Value);
```

Add:

```csharp
using Chat.Application.Chats.Commands.CleanupHiddenChats;
```

- [ ] **Step 3: Verify cleanup worker build**

Run with elevated permission:

```bash
dotnet build src/workers/Chat.CleanupWorker/Chat.CleanupWorker.csproj
```

Expected: `Build succeeded.` with 0 errors.

---

## Task 9: Manual API Verification

**Files:**
- No file changes.

- [ ] **Step 1: Start Chat API**

Run with elevated permission using the project’s normal local startup path, for example:

```bash
dotnet run --project src/services/Chat/Chat.Api/Chat.Api.csproj
```

Expected: Chat API starts and logs the listening URL.

- [ ] **Step 2: Verify sparse rename request shape**

Send a request with a valid authenticated user token and an existing chat id:

```bash
CHAT_API_URL="https://localhost:7001"
CHAT_ID="00000000-0000-0000-0000-000000000000"
TOKEN="replace-with-valid-bearer-token"

curl -i \
  -X PATCH "$CHAT_API_URL/v1/chats/$CHAT_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "title": "SSs" }'
```

Expected: `200 OK`; response contains `"title":"SSs"` and leaves `isPinned`, `isArchived`, and `isVisible` unchanged.

- [ ] **Step 3: Verify archive does not hide**

```bash
curl -i \
  -X PATCH "$CHAT_API_URL/v1/chats/$CHAT_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "isArchived": true }'
```

Expected: `200 OK`; response contains `"isArchived":true`, `"isVisible":true`, and `"hiddenAt":null` unless the chat had already been hidden earlier.

- [ ] **Step 4: Verify hide starts deletion clock**

```bash
curl -i \
  -X PATCH "$CHAT_API_URL/v1/chats/$CHAT_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "isVisible": false }'
```

Expected: `200 OK`; response contains `"isVisible":false` and a non-null `"hiddenAt"`.

- [ ] **Step 5: Verify explicit null is rejected**

```bash
curl -i \
  -X PATCH "$CHAT_API_URL/v1/chats/$CHAT_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "title": null }'
```

Expected: `400 Bad Request` with error code `ChatPatch.NullValue`.

- [ ] **Step 6: Verify unknown fields are rejected**

```bash
curl -i \
  -X PATCH "$CHAT_API_URL/v1/chats/$CHAT_ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{ "pinned": true }'
```

Expected: `400 Bad Request` with error code `ChatPatch.UnknownField`.

---

## Task 10: Ask Before Test Work

**Files:**
- No file changes unless the user approves tests.

- [ ] **Step 1: Ask for test approval**

Ask:

```text
Do you want me to add focused tests for the chat thread update flow now?
```

- [ ] **Step 2: If approved, add focused tests**

Only after approval, add tests covering:

- `ChatThread.Rename`, `Hide`, and `Show`.
- Archive/visibility independence.
- Handler sparse updates and full-state result.
- Endpoint parser behavior for omitted fields, explicit nulls, unknown fields, and empty body.
- Hidden cleanup predicate does not delete visible archived chats.

Use the existing test project patterns in:

- `tests/Chat/Chat.Domain.Tests/Chats/ChatThreadTests.cs`
- `tests/Chat/Chat.Application.Tests/Turns/SendMessageHandlerTests.cs`
- `tests/Chat/Chat.Application.Tests/Turns/FakeChatRepository.cs`

---

## Implementation Order

1. Domain state and methods.
2. Persistence mapping and migration.
3. Application result DTO.
4. Application command/handler.
5. API response DTO.
6. FastEndpoints sparse PATCH endpoint.
7. Hidden cleanup command and repository method.
8. Cleanup worker integration after the existing cleanup worker plan is implemented.
9. Manual API verification.
10. Ask before adding tests.
