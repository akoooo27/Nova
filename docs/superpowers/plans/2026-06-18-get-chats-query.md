# Get Chats Query Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `GET /me/chats` endpoint that returns the authenticated user's chats (metadata only) as an offset-paginated page with a total count, pinned chats first then by recency.

**Architecture:** A CQRS read slice mirroring the existing `GetFavoriteModels` query: `GetChatsQuery` → `ValidationBehavior` → `GetChatsHandler` resolves the `UserId` from `IUserContext` and delegates to `IChatListReader`; a Dapper `ChatListReader` runs a count + page query (`QueryMultipleAsync`) against the `chats` table; a FastEndpoints endpoint maps query params in and a list response out.

**Tech Stack:** .NET, `Mediator` (source-generated, not MediatR), FastEndpoints, FluentValidation, Dapper 2.1.79 over `NpgsqlDataSource`, ErrorOr, xUnit.

## Global Constraints

- Use the `Mediator` package family (`IQuery`/`IQueryHandler`); do **not** introduce MediatR.
- Use FastEndpoints for HTTP; do **not** use ASP.NET Core controllers.
- Read side uses Dapper over `NpgsqlDataSource`; the write side / EF Core is not involved.
- `chats` columns are snake_case: `id`, `user_id`, `title`, `pinned_at`, `is_archived`, `is_temporary`, `created_at`, `updated_at`.
- The list filter is server-fixed: `is_temporary = false AND is_archived = false`. Not client-controllable.
- Ordering is fixed: `(pinned_at is null), pinned_at desc, updated_at desc, id desc`.
- Pagination: `limit` default 20, range [1, 100]; `offset` default 0, `>= 0`.
- No schema change, no new migration, no new index.
- Tests are included in this plan (handler + validator) per explicit user request; do not add reader integration tests (the repo has no Infrastructure test project).
- Commit messages follow the repo convention `feat(chat): ...` / `test(chat): ...` with no co-author trailer.

---

### Task 1: Application contract + query validation

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/ChatSummaryReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/ChatListReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/GetChatsQuery.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/IChatListReader.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/GetChatsQueryValidator.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatsQueryValidatorTests.cs`

**Interfaces:**
- Consumes: nothing (foundational slice).
- Produces:
  - `GetChatsQuery(int Limit, int Offset) : IQuery<ErrorOr<ChatListReadModel>>`
  - `ChatListReadModel(IReadOnlyList<ChatSummaryReadModel> Items, int Total, int Limit, int Offset)`
  - `ChatSummaryReadModel(Guid Id, string Title, bool IsPinned, DateTimeOffset? PinnedAt, bool IsArchived, bool IsTemporary, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)`
  - `IChatListReader.GetAsync(UserId userId, int limit, int offset, CancellationToken cancellationToken) -> Task<ChatListReadModel>`
  - `GetChatsQueryValidator : AbstractValidator<GetChatsQuery>`

- [ ] **Step 1: Write the failing validator tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatsQueryValidatorTests.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChats;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class GetChatsQueryValidatorTests
{
    private readonly GetChatsQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsInRangeLimitAndOffset()
    {
        GetChatsQuery query = new(Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateRejectsOutOfRangeLimit(int limit)
    {
        GetChatsQuery query = new(Limit: limit, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(GetChatsQuery.Limit));
    }

    [Fact]
    public void ValidateRejectsNegativeOffset()
    {
        GetChatsQuery query = new(Limit: 20, Offset: -1);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(GetChatsQuery.Offset));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetChatsQueryValidatorTests"`
Expected: FAIL — build error, `GetChatsQuery` and `GetChatsQueryValidator` do not exist yet.

- [ ] **Step 3: Create the read models**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChats/ChatSummaryReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChats;

public sealed record ChatSummaryReadModel
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    bool IsTemporary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
```

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChats/ChatListReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.GetChats;

public sealed record ChatListReadModel
(
    IReadOnlyList<ChatSummaryReadModel> Items,
    int Total,
    int Limit,
    int Offset
);
```

- [ ] **Step 4: Create the query and reader interface**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChats/GetChatsQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.GetChats;

public sealed record GetChatsQuery(int Limit, int Offset) : IQuery<ErrorOr<ChatListReadModel>>;
```

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChats/IChatListReader.cs`:

```csharp
using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.GetChats;

public interface IChatListReader
{
    Task<ChatListReadModel> GetAsync(UserId userId, int limit, int offset, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Create the validator**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChats/GetChatsQueryValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.Chats.Queries.GetChats;

internal sealed class GetChatsQueryValidator : AbstractValidator<GetChatsQuery>
{
    public GetChatsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100);

        RuleFor(x => x.Offset)
            .GreaterThanOrEqualTo(0);
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetChatsQueryValidatorTests"`
Expected: PASS (4 test cases: 1 valid, 2 limit, 1 offset).

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Queries/GetChats tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatsQueryValidatorTests.cs
git commit -m "feat(chat): add get chats query contract and validation"
```

---

### Task 2: Query handler

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Queries/GetChats/GetChatsHandler.cs`
- Create: `tests/Chat/Chat.Application.Tests/Chats/FakeChatListReader.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatsHandlerTests.cs`

**Interfaces:**
- Consumes: `GetChatsQuery`, `ChatListReadModel`, `ChatSummaryReadModel`, `IChatListReader` (Task 1); `IUserContext` (`Shared.Application.Authentication`); `UserId` (`Chat.Domain.Shared`); reuses `FakeUserContext` from `Chat.Application.Tests.FavoriteModels`.
- Produces: `GetChatsHandler(IUserContext userContext, IChatListReader reader)` implementing `IQueryHandler<GetChatsQuery, ErrorOr<ChatListReadModel>>`.

- [ ] **Step 1: Create the fake reader**

Create `tests/Chat/Chat.Application.Tests/Chats/FakeChatListReader.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChats;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Chats;

internal sealed class FakeChatListReader(ChatListReadModel readModel) : IChatListReader
{
    public UserId? RequestedUserId { get; private set; }

    public int? RequestedLimit { get; private set; }

    public int? RequestedOffset { get; private set; }

    public int GetCallCount { get; private set; }

    public Task<ChatListReadModel> GetAsync(UserId userId, int limit, int offset, CancellationToken cancellationToken)
    {
        RequestedUserId = userId;
        RequestedLimit = limit;
        RequestedOffset = offset;
        GetCallCount++;

        return Task.FromResult(readModel);
    }
}
```

- [ ] **Step 2: Write the failing handler tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatsHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class GetChatsHandlerTests
{
    [Fact]
    public async Task HandleReadsChatsForCurrentUserWithPaging()
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        ChatListReadModel readModel = new
        (
            Items:
            [
                new ChatSummaryReadModel
                (
                    Id: Guid.CreateVersion7(),
                    Title: "მენეჯმენტი თავი #17",
                    IsPinned: false,
                    PinnedAt: null,
                    IsArchived: false,
                    IsTemporary: false,
                    CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-10),
                    UpdatedAt: DateTimeOffset.UtcNow
                )
            ],
            Total: 1,
            Limit: 20,
            Offset: 0
        );
        FakeChatListReader reader = new(readModel);
        GetChatsHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<ChatListReadModel> result = await handler.Handle
        (
            new GetChatsQuery(Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal(20, reader.RequestedLimit);
        Assert.Equal(0, reader.RequestedOffset);
        Assert.Equal(1, reader.GetCallCount);
    }

    [Fact]
    public async Task HandleReturnsErrorAndSkipsReaderWhenUserIdMissing()
    {
        ChatListReadModel readModel = new([], Total: 0, Limit: 20, Offset: 0);
        FakeChatListReader reader = new(readModel);
        GetChatsHandler handler = new
        (
            userContext: new FakeUserContext(string.Empty),
            reader: reader
        );

        ErrorOr<ChatListReadModel> result = await handler.Handle
        (
            new GetChatsQuery(Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(0, reader.GetCallCount);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetChatsHandlerTests"`
Expected: FAIL — build error, `GetChatsHandler` does not exist yet.

- [ ] **Step 4: Implement the handler**

Create `src/services/Chat/Chat.Application/Chats/Queries/GetChats/GetChatsHandler.cs`:

```csharp
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.GetChats;

internal sealed class GetChatsHandler(IUserContext userContext, IChatListReader reader)
    : IQueryHandler<GetChatsQuery, ErrorOr<ChatListReadModel>>
{
    public async ValueTask<ErrorOr<ChatListReadModel>> Handle
    (
        GetChatsQuery query,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        return await reader.GetAsync(userIdResult.Value, query.Limit, query.Offset, cancellationToken);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Chat/Chat.Application.Tests --filter "FullyQualifiedName~GetChatsHandlerTests"`
Expected: PASS (2 test cases).

- [ ] **Step 6: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Queries/GetChats/GetChatsHandler.cs tests/Chat/Chat.Application.Tests/Chats/FakeChatListReader.cs tests/Chat/Chat.Application.Tests/Chats/Queries/GetChatsHandlerTests.cs
git commit -m "feat(chat): add get chats query handler"
```

---

### Task 3: Dapper reader + DI registration

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatListReader.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs` (the `AddReaders` method, ~line 146-156, and the `using` block)

**Interfaces:**
- Consumes: `IChatListReader`, `ChatListReadModel`, `ChatSummaryReadModel` (Task 1); `NpgsqlDataSource`; `UserId`.
- Produces: `ChatListReader : IChatListReader` registered as scoped, so the API host resolves the handler's reader dependency.

No unit test (the repo has no Infrastructure test project and does not unit-test Dapper readers). Verification is a successful build; runtime behavior is exercised in Task 4's manual smoke.

- [ ] **Step 1: Implement the Dapper reader**

Create `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatListReader.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChats;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatListReader(NpgsqlDataSource dataSource) : IChatListReader
{
    private const string Sql = """
                               select count(*)
                               from chats
                               where user_id = @UserId
                                 and is_temporary = false
                                 and is_archived = false;

                               select
                                    id           as "Id",
                                    title        as "Title",
                                    pinned_at    as "PinnedAt",
                                    is_archived  as "IsArchived",
                                    is_temporary as "IsTemporary",
                                    created_at   as "CreatedAt",
                                    updated_at   as "UpdatedAt"
                                from chats
                                where user_id = @UserId
                                  and is_temporary = false
                                  and is_archived = false
                                order by (pinned_at is null), pinned_at desc, updated_at desc, id desc
                                limit @Limit offset @Offset;
                               """;

    public async Task<ChatListReadModel> GetAsync
    (
        UserId userId,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            Sql,
            new { UserId = userId.Value, Limit = limit, Offset = offset },
            cancellationToken: cancellationToken
        );

        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        int total = await grid.ReadSingleAsync<int>();
        ChatRow[] rows = (await grid.ReadAsync<ChatRow>()).ToArray();

        ChatSummaryReadModel[] items = rows
            .Select(row => new ChatSummaryReadModel
            (
                Id: row.Id,
                Title: row.Title,
                IsPinned: row.PinnedAt is not null,
                PinnedAt: row.PinnedAt,
                IsArchived: row.IsArchived,
                IsTemporary: row.IsTemporary,
                CreatedAt: row.CreatedAt,
                UpdatedAt: row.UpdatedAt
            ))
            .ToArray();

        return new ChatListReadModel(items, total, limit, offset);
    }

    private sealed record ChatRow
    (
        Guid Id,
        string Title,
        DateTimeOffset? PinnedAt,
        bool IsArchived,
        bool IsTemporary,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt
    );
}
```

- [ ] **Step 2: Register the reader in DI**

In `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`, add these two `using` directives to the existing `using` block (keep alphabetical grouping consistent with neighbors):

```csharp
using Chat.Application.Chats.Queries.GetChats;
using Chat.Infrastructure.Chats.Readers;
```

Then in the `AddReaders` method, add the registration next to the existing `IFavoriteModelsReader` line:

```csharp
    private static IServiceCollection AddReaders(this IServiceCollection services)
    {
        services.AddScoped<PublicModelCatalogDapperReader>();
        services.AddScoped<IPublicModelCatalogReader, CachedPublicModelCatalogReader>();

        services.AddScoped<IManagedModelCatalogReader, ManagedModelCatalogDapperReader>();

        services.AddScoped<IFavoriteModelsReader, FavoriteModelsReader>();

        services.AddScoped<IChatListReader, ChatListReader>();

        return services;
    }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Nova.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatListReader.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): add chat list dapper reader"
```

---

### Task 4: API endpoint, response, and mapper

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChats/Response.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChats/ChatListItemResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChats/ResponseMapper.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/GetChats/Endpoint.cs`

**Interfaces:**
- Consumes: `GetChatsQuery`, `ChatListReadModel`, `ChatSummaryReadModel` (Task 1); `ISender`; `CustomResults`, `CustomTags`; FastEndpoints `Send` API.
- Produces: `GET /v1/me/chats` returning `Response(Items, Total, Limit, Offset)`; route name `Chat.Chats.List`.

No unit test (consistent with the repo — endpoints are not unit-tested). Verification is a successful build plus a manual smoke call against the running AppHost.

- [ ] **Step 1: Create the item response**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChats/ChatListItemResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.GetChats;

internal sealed class ChatListItemResponse
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required bool IsPinned { get; init; }

    public required DateTimeOffset? PinnedAt { get; init; }

    public required bool IsArchived { get; init; }

    public required bool IsTemporary { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
```

- [ ] **Step 2: Create the list response**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChats/Response.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.GetChats;

internal sealed class Response
{
    public required IReadOnlyCollection<ChatListItemResponse> Items { get; init; }

    public required int Total { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }
}
```

- [ ] **Step 3: Create the response mapper**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChats/ResponseMapper.cs`:

```csharp
using Chat.Application.Chats.Queries.GetChats;

namespace Chat.Api.Endpoints.Chats.GetChats;

internal static class ResponseMapper
{
    public static Response ToResponse(ChatListReadModel readModel) => new()
    {
        Items = readModel.Items
            .Select(ToResponse)
            .ToList(),
        Total = readModel.Total,
        Limit = readModel.Limit,
        Offset = readModel.Offset
    };

    private static ChatListItemResponse ToResponse(ChatSummaryReadModel item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        IsPinned = item.IsPinned,
        PinnedAt = item.PinnedAt,
        IsArchived = item.IsArchived,
        IsTemporary = item.IsTemporary,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt
    };
}
```

- [ ] **Step 4: Create the endpoint**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/GetChats/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints;
using Chat.Application.Chats.Queries.GetChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.GetChats;

internal sealed record Request
(
    [property: QueryParam] int? Limit,
    [property: QueryParam] int? Offset
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request, Response>
{
    public const string RouteName = "Chat.Chats.List";

    private const int DefaultLimit = 20;
    private const int DefaultOffset = 0;

    public override void Configure()
    {
        Get("/me/chats");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("List Chats")
                .WithDescription("Lists the authenticated user's chats (metadata only), pinned first then most recently active.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        GetChatsQuery query = new
        (
            Limit: request.Limit ?? DefaultLimit,
            Offset: request.Offset ?? DefaultOffset
        );

        ErrorOr<ChatListReadModel> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build Nova.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Manual smoke test**

Start the app host:

Run: `dotnet run --project Nova.AppHost`

Then call the endpoint with a valid bearer token for a user who has chats (replace `$TOKEN` and the Chat API base port as shown in the Aspire dashboard):

```bash
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:<chat-api-port>/v1/me/chats?limit=20&offset=0" | jq
```

Expected: `200 OK` with a body shaped like:

```json
{
  "items": [
    {
      "id": "…",
      "title": "…",
      "isPinned": false,
      "pinnedAt": null,
      "isArchived": false,
      "isTemporary": false,
      "createdAt": "…",
      "updatedAt": "…"
    }
  ],
  "total": 29,
  "limit": 20,
  "offset": 0
}
```

Verify: pinned chats (if any) appear first; archived and temporary chats are absent; `total` reflects the same filter and does not change with `offset`. Also confirm `?limit=0` returns `400 Bad Request`.

- [ ] **Step 7: Commit**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats/GetChats
git commit -m "feat(chat): add list chats endpoint"
```

---

## Notes for the implementer

- `[Fact]`, `[Theory]`, `[InlineData]`, and `Assert` are available via `global using Xunit;` in `tests/Chat/Chat.Application.Tests/GlobalUsings.cs` — do not add a `using Xunit;`.
- `FakeUserContext` already exists in `Chat.Application.Tests.FavoriteModels`; reuse it via that `using` (the existing `UpdateChatHandlerTests` does the same). Do not create a second `FakeUserContext`.
- `GetChatsQuery` references `ChatListReadModel`, and `IChatListReader` returns it, so all of Task 1's read models, query, and interface must be created together for the project to compile.
- The handler returns `userIdResult.Errors` (a list) — matching `GetFavoriteModelsHandler` exactly; `ValidationBehavior` and `CustomResults.Problem` turn errors into the right HTTP status.
- Use `ReadSingleAsync<int>()` for the count grid (it is always exactly one row) before `ReadAsync<ChatRow>()` for the page; read order must match the SQL statement order (count first, page second).
