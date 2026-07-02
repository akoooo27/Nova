# Chat Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build authenticated chat-history search backed by PostgreSQL full-text search: a generated `tsvector` column on `chat_messages`, a GIN index, one Dapper reader, and `GET /me/chats/search` returning chat-level results with snippets.

**Architecture:** No write-path changes, no new services, no messaging. PostgreSQL maintains `chat_messages.search_vector` as a stored generated column in the same transaction as every message write, so search is always fresh. `Chat.Api` exposes `GET /me/chats/search`; a `Mediator` query handler resolves the authenticated user and calls a Dapper reader that ranks, groups by chat, paginates, and extracts snippets in a single database round trip. The `search_vector` column is deliberately invisible to the domain model and EF Core.

**Tech Stack:** .NET 10, FastEndpoints, Mediator.SourceGenerator / Mediator.Abstractions, FluentValidation, Dapper, PostgreSQL (tsvector/GIN), xUnit.

> **Revision note (2026-07-02):** This plan replaces the earlier Elasticsearch-based plan (search worker, debounce job table, outbox indexing requests, backfill, PostgreSQL validation layer). See the revision note in the design spec for the rationale.

---

## Implementation Notes

- Follow existing project conventions in `AGENTS.md`.
- Do not replace `Mediator` with MediatR.
- Ask for elevated permissions before any `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet run`, or similar .NET command.
- Tests are approved for this feature.
- Use `apply_patch` for manual file edits.
- The design spec is `docs/superpowers/specs/2026-06-24-chat-search-design.md`.
- **Domain isolation is a hard constraint:** do not add `search_vector` to `ChatMessage`, `ChatDbContext`, or any EF configuration. The column exists only in the database (raw-SQL migration) and in the Dapper reader's SQL. Do not "fix" the migration by generating it from the model.
- **Text search configuration consistency:** every `to_tsvector`, `websearch_to_tsquery`, and `ts_headline` call must use the `'simple'` configuration. A mismatched configuration silently stops matching the indexed expression.
- Mirror the `GetChats` feature (`Chat.Application/Chats/Queries/GetChats`, `Chat.Api/Endpoints/Chats/GetChats`, `Chat.Infrastructure/Chats/Readers/ChatListReader.cs`) for file layout, naming, and style.

---

## File Structure

### Application

- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQuery.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQueryValidator.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsHandler.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/IChatSearchReader.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchReadModel.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchResultReadModel.cs`
- Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchSnippetReadModel.cs`
- Modify `src/services/Chat/Chat.Application/Chats/ChatLimits.cs` (add `MaxSearchQueryLength`)

### Infrastructure

- Create `src/services/Chat/Chat.Infrastructure/Database/Migrations/20260702090000_ChatMessageSearchVector.cs`
- Create `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatSearchReader.cs`
- Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs` (register reader in `AddReaders()`)

### Api

- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Endpoint.cs` (includes the `Request` record, matching `GetChats`)
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Response.cs`
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatResultResponse.cs`
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatSnippetResponse.cs`
- Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/ResponseMapper.cs`

### Tests

- Create `tests/Chat/Chat.Application.Tests/Chats/FakeChatSearchReader.cs`
- Create `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsQueryValidatorTests.cs`
- Create `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsHandlerTests.cs`

---

## Task 1: Add Search Vector Migration

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Database/Migrations/20260702090000_ChatMessageSearchVector.cs`

The column is intentionally absent from the EF model, so the migration is written by hand — repo precedent: `20260614161000_ChatPinArchive.cs` (hand-written, no `.Designer.cs`). Because EF migrations diff model-to-model, later generated migrations will never emit operations for this column.

- [ ] **Step 1: Create the migration**

If the latest migration in `src/services/Chat/Chat.Infrastructure/Database/Migrations/` has an id greater than `20260702090000`, pick a current UTC timestamp instead and use it consistently in the file name, `[Migration]` attribute, and class name references.

Create `src/services/Chat/Chat.Infrastructure/Database/Migrations/20260702090000_ChatMessageSearchVector.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chat.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ChatDbContext))]
    [Migration("20260702090000_ChatMessageSearchVector")]
    public partial class ChatMessageSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                alter table chat_messages
                    add column search_vector tsvector
                    generated always as (to_tsvector('simple', coalesce(content, ''))) stored;
                """);

            migrationBuilder.Sql(
                "create index ix_chat_messages_search_vector on chat_messages using gin (search_vector);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop index if exists ix_chat_messages_search_vector;");

            migrationBuilder.Sql("alter table chat_messages drop column if exists search_vector;");
        }
    }
}
```

Match the using/namespace style of `20260614161000_ChatPinArchive.cs` exactly (including the `Chat.Infrastructure.Database` using if present there).

Notes:

- `to_tsvector('simple'::regconfig, ...)` with a literal configuration is immutable, which is required for a generated column.
- `coalesce(content, '')` covers messages with `NULL` content; they produce an empty vector and never match.
- Adding a stored generated column rewrites the table once at migration time and backfills every existing row — this **is** the backfill; no separate tooling exists or is needed.
- Do NOT touch `ChatDbContext`, `ChatMessageConfiguration`, or `ChatMessage`.

- [ ] **Step 2: Build**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Database/Migrations
git commit -m "feat(chat): add message search vector column"
```

---

## Task 2: Add Search Query Application Contract

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQuery.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQueryValidator.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsHandler.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/IChatSearchReader.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchResultReadModel.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchSnippetReadModel.cs`
- Modify: `src/services/Chat/Chat.Application/Chats/ChatLimits.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsQueryValidatorTests.cs`
- Test: `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsHandlerTests.cs`
- Create fake: `tests/Chat/Chat.Application.Tests/Chats/FakeChatSearchReader.cs`

- [ ] **Step 1: Write validator tests**

Create `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsQueryValidatorTests.cs`:

```csharp
using Chat.Application.Chats;
using Chat.Application.Chats.Queries.SearchChats;

using FluentValidation.Results;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class SearchChatsQueryValidatorTests
{
    private readonly SearchChatsQueryValidator _validator = new();

    [Fact]
    public void ValidateAcceptsValidQuery()
    {
        SearchChatsQuery query = new(Query: "memory bug", IsArchived: false, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRejectsBlankQuery(string text)
    {
        SearchChatsQuery query = new(Query: text, IsArchived: false, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Query));
    }

    [Fact]
    public void ValidateRejectsOverlongQuery()
    {
        string text = new('a', ChatLimits.MaxSearchQueryLength + 1);
        SearchChatsQuery query = new(Query: text, IsArchived: false, Limit: 20, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Query));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateRejectsOutOfRangeLimit(int limit)
    {
        SearchChatsQuery query = new(Query: "memory", IsArchived: false, Limit: limit, Offset: 0);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Limit));
    }

    [Fact]
    public void ValidateRejectsNegativeOffset()
    {
        SearchChatsQuery query = new(Query: "memory", IsArchived: false, Limit: 20, Offset: -1);

        ValidationResult result = _validator.Validate(query);

        Assert.Contains(result.Errors, failure => failure.PropertyName == nameof(SearchChatsQuery.Offset));
    }
}
```

- [ ] **Step 2: Write handler tests**

Create `tests/Chat/Chat.Application.Tests/Chats/FakeChatSearchReader.cs`, mirroring `FakeChatListReader.cs`:

```csharp
using Chat.Application.Chats.Queries.SearchChats;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Chats;

internal sealed class FakeChatSearchReader(ChatSearchReadModel readModel) : IChatSearchReader
{
    public UserId? RequestedUserId { get; private set; }

    public string? RequestedQuery { get; private set; }

    public bool? RequestedIsArchived { get; private set; }

    public int? RequestedLimit { get; private set; }

    public int? RequestedOffset { get; private set; }

    public int SearchCallCount { get; private set; }

    public Task<ChatSearchReadModel> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        RequestedUserId = userId;
        RequestedQuery = query;
        RequestedIsArchived = isArchived;
        RequestedLimit = limit;
        RequestedOffset = offset;
        SearchCallCount++;

        return Task.FromResult(readModel);
    }
}
```

Create `tests/Chat/Chat.Application.Tests/Chats/Queries/SearchChatsHandlerTests.cs`:

```csharp
using Chat.Application.Chats.Queries.SearchChats;
using Chat.Application.Tests.FavoriteModels;
using Chat.Domain.Shared;

using ErrorOr;

namespace Chat.Application.Tests.Chats.Queries;

public sealed class SearchChatsHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleSearchesForCurrentUser(bool isArchived)
    {
        UserId userId = UserId.FromDatabase("auth0|user-1");
        ChatSearchReadModel readModel = new(Chats: [], Total: 0, Limit: 20, Offset: 0);
        FakeChatSearchReader reader = new(readModel);
        SearchChatsHandler handler = new
        (
            userContext: new FakeUserContext(userId.Value),
            reader: reader
        );

        ErrorOr<ChatSearchReadModel> result = await handler.Handle
        (
            new SearchChatsQuery(Query: "  memory bug  ", IsArchived: isArchived, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.False(result.IsError);
        Assert.Same(readModel, result.Value);
        Assert.Equal(userId, reader.RequestedUserId);
        Assert.Equal("memory bug", reader.RequestedQuery);
        Assert.Equal(isArchived, reader.RequestedIsArchived);
        Assert.Equal(20, reader.RequestedLimit);
        Assert.Equal(0, reader.RequestedOffset);
        Assert.Equal(1, reader.SearchCallCount);
    }

    [Fact]
    public async Task HandleReturnsErrorAndSkipsReaderWhenUserIdMissing()
    {
        ChatSearchReadModel readModel = new(Chats: [], Total: 0, Limit: 20, Offset: 0);
        FakeChatSearchReader reader = new(readModel);
        SearchChatsHandler handler = new
        (
            userContext: new FakeUserContext(string.Empty),
            reader: reader
        );

        ErrorOr<ChatSearchReadModel> result = await handler.Handle
        (
            new SearchChatsQuery(Query: "memory bug", IsArchived: false, Limit: 20, Offset: 0),
            CancellationToken.None
        );

        Assert.True(result.IsError);
        Assert.Equal(0, reader.SearchCallCount);
    }
}
```

- [ ] **Step 3: Run focused tests and verify failures**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "SearchChatsQueryValidatorTests|SearchChatsHandlerTests"
```

Expected: fails because query types do not exist.

- [ ] **Step 4: Add the query limit constant**

Modify `src/services/Chat/Chat.Application/Chats/ChatLimits.cs` — add:

```csharp
public const int MaxSearchQueryLength = 256;
```

- [ ] **Step 5: Add read models and reader interface**

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchSnippetReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchSnippetReadModel
(
    Guid MessageId,
    string Role,
    string Text
);
```

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchResultReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchResultReadModel
(
    Guid Id,
    string Title,
    bool IsPinned,
    DateTimeOffset? PinnedAt,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int MatchCount,
    IReadOnlyList<ChatSearchSnippetReadModel> Snippets
);
```

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/ChatSearchReadModel.cs`:

```csharp
namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record ChatSearchReadModel
(
    IReadOnlyList<ChatSearchResultReadModel> Chats,
    int Total,
    int Limit,
    int Offset
);
```

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/IChatSearchReader.cs`:

```csharp
using Chat.Domain.Shared;

namespace Chat.Application.Chats.Queries.SearchChats;

public interface IChatSearchReader
{
    Task<ChatSearchReadModel> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 6: Add query record and validator**

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.Chats.Queries.SearchChats;

public sealed record SearchChatsQuery
(
    string Query,
    bool IsArchived,
    int Limit,
    int Offset
) : IQuery<ErrorOr<ChatSearchReadModel>>;
```

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsQueryValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.Chats.Queries.SearchChats;

internal sealed class SearchChatsQueryValidator : AbstractValidator<SearchChatsQuery>
{
    public SearchChatsQueryValidator()
    {
        RuleFor(x => x.Query)
            .Must(query => !string.IsNullOrWhiteSpace(query))
            .WithMessage("Search query is required.")
            .MaximumLength(ChatLimits.MaxSearchQueryLength);

        RuleFor(x => x.Limit)
            .InclusiveBetween(ChatLimits.MinQueryLimit, ChatLimits.MaxQueryLimit);

        RuleFor(x => x.Offset)
            .GreaterThanOrEqualTo(0);
    }
}
```

- [ ] **Step 7: Add handler**

Create `src/services/Chat/Chat.Application/Chats/Queries/SearchChats/SearchChatsHandler.cs`, mirroring `GetChatsHandler`:

```csharp
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.Chats.Queries.SearchChats;

internal sealed class SearchChatsHandler(IUserContext userContext, IChatSearchReader reader)
    : IQueryHandler<SearchChatsQuery, ErrorOr<ChatSearchReadModel>>
{
    public async ValueTask<ErrorOr<ChatSearchReadModel>> Handle(SearchChatsQuery query, CancellationToken cancellationToken)
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        return await reader.SearchAsync
        (
            userId: userIdResult.Value,
            query: query.Query.Trim(),
            isArchived: query.IsArchived,
            limit: query.Limit,
            offset: query.Offset,
            cancellationToken: cancellationToken
        );
    }
}
```

- [ ] **Step 8: Run focused tests**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "SearchChatsQueryValidatorTests|SearchChatsHandlerTests"
```

Expected: tests pass.

- [ ] **Step 9: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats tests/Chat/Chat.Application.Tests/Chats
git commit -m "feat(chat): add search query contract"
```

---

## Task 3: Add Dapper Search Reader

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatSearchReader.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add reader**

Create `src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatSearchReader.cs`, following the `ChatListReader` pattern (one multi-statement command, `QueryMultipleAsync`):

```csharp
using Chat.Application.Chats.Queries.SearchChats;
using Chat.Domain.Shared;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.Chats.Readers;

internal sealed class ChatSearchReader(NpgsqlDataSource dataSource) : IChatSearchReader
{
    private const string Sql = """
                               with search_query as (
                                   select websearch_to_tsquery('simple', @Query) as query
                               )
                               select count(*)::int
                               from chats
                               cross join search_query
                               where chats.user_id = @UserId
                                 and chats.is_temporary = false
                                 and chats.is_archived = @IsArchived
                                 and (
                                       to_tsvector('simple', chats.title) @@ search_query.query
                                       or exists
                                       (
                                           select 1
                                           from chat_messages messages
                                           where messages.chat_id = chats.id
                                             and messages.status = 'Completed'
                                             and messages.search_vector @@ search_query.query
                                       )
                                     );

                               with search_query as (
                                   select websearch_to_tsquery('simple', @Query) as query
                               ),
                               message_stats as (
                                   select
                                        messages.chat_id,
                                        count(*)::int                                            as match_count,
                                        max(ts_rank(messages.search_vector, search_query.query)) as best_rank
                                   from chat_messages messages
                                   cross join search_query
                                   join chats on chats.id = messages.chat_id
                                   where chats.user_id = @UserId
                                     and chats.is_temporary = false
                                     and chats.is_archived = @IsArchived
                                     and messages.status = 'Completed'
                                     and messages.search_vector @@ search_query.query
                                   group by messages.chat_id
                               ),
                               page as (
                                   select
                                        chats.id,
                                        chats.title,
                                        chats.pinned_at,
                                        chats.is_archived,
                                        chats.created_at,
                                        chats.updated_at,
                                        coalesce(message_stats.match_count, 0) as match_count,
                                        (2.0 * ts_rank(to_tsvector('simple', chats.title), search_query.query))
                                            + coalesce(message_stats.best_rank, 0)
                                            + (0.05 * least(coalesce(message_stats.match_count, 0), 20)) as score
                                   from chats
                                   cross join search_query
                                   left join message_stats on message_stats.chat_id = chats.id
                                   where chats.user_id = @UserId
                                     and chats.is_temporary = false
                                     and chats.is_archived = @IsArchived
                                     and (
                                           message_stats.chat_id is not null
                                           or to_tsvector('simple', chats.title) @@ search_query.query
                                         )
                                   order by score desc, chats.updated_at desc, chats.id desc
                                   limit @Limit offset @Offset
                               )
                               select
                                    page.id             as "Id",
                                    page.title          as "Title",
                                    page.pinned_at      as "PinnedAt",
                                    page.is_archived    as "IsArchived",
                                    page.created_at     as "CreatedAt",
                                    page.updated_at     as "UpdatedAt",
                                    page.match_count    as "MatchCount",
                                    snippets.message_id as "MessageId",
                                    snippets.role       as "Role",
                                    snippets.snippet    as "Snippet"
                               from page
                               cross join search_query
                               left join lateral
                               (
                                   select
                                        messages.id   as message_id,
                                        messages.role as role,
                                        ts_rank(messages.search_vector, search_query.query) as snippet_rank,
                                        ts_headline
                                        (
                                            'simple',
                                            messages.content,
                                            search_query.query,
                                            'MaxFragments=1, MaxWords=18, MinWords=6, StartSel="", StopSel=""'
                                        ) as snippet
                                   from chat_messages messages
                                   where messages.chat_id = page.id
                                     and messages.status = 'Completed'
                                     and messages.search_vector @@ search_query.query
                                   order by snippet_rank desc, messages.created_at desc, messages.id desc
                                   limit 3
                               ) snippets on true
                               order by page.score desc, page.updated_at desc, page.id desc, snippets.snippet_rank desc nulls last;
                               """;

    public async Task<ChatSearchReadModel> SearchAsync
    (
        UserId userId,
        string query,
        bool isArchived,
        int limit,
        int offset,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            commandText: Sql,
            parameters: new
            {
                UserId = userId.Value,
                Query = query,
                IsArchived = isArchived,
                Limit = limit,
                Offset = offset
            },
            cancellationToken: cancellationToken
        );

        await using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(command);

        int total = await grid.ReadSingleAsync<int>();
        ChatRow[] rows = (await grid.ReadAsync<ChatRow>()).ToArray();

        ChatSearchResultReadModel[] chats = rows
            .GroupBy(row => row.Id)
            .Select(group =>
            {
                ChatRow first = group.First();

                return new ChatSearchResultReadModel
                (
                    Id: first.Id,
                    Title: first.Title,
                    IsPinned: first.PinnedAt is not null,
                    PinnedAt: first.PinnedAt,
                    IsArchived: first.IsArchived,
                    CreatedAt: first.CreatedAt,
                    UpdatedAt: first.UpdatedAt,
                    MatchCount: first.MatchCount,
                    Snippets: group
                        .Where(row => row.MessageId is not null)
                        .Select(row => new ChatSearchSnippetReadModel
                        (
                            MessageId: row.MessageId!.Value,
                            Role: row.Role!,
                            Text: row.Snippet ?? string.Empty
                        ))
                        .ToArray()
                );
            })
            .ToArray();

        return new ChatSearchReadModel(chats, total, limit, offset);
    }

    private sealed record ChatRow
    (
        Guid Id,
        string Title,
        DateTimeOffset? PinnedAt,
        bool IsArchived,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        int MatchCount,
        Guid? MessageId,
        string? Role,
        string? Snippet
    );
}
```

Notes:

- The page statement returns one row per chat/snippet pair (up to 3 per chat; exactly one all-null snippet row for title-only matches). `GroupBy` preserves first-seen order, and the SQL orders snippet rows within each chat by rank, so no re-sorting is needed in C#.
- `count(*)::int` casts avoid `bigint`-to-`int` mapping friction in Dapper.
- Both statements must use identical filter predicates so `total` agrees with the page contents.

- [ ] **Step 2: Register reader**

Modify `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`. Add the using:

```csharp
using Chat.Application.Chats.Queries.SearchChats;
```

(if not already covered) and inside `AddReaders()` add:

```csharp
services.AddScoped<IChatSearchReader, ChatSearchReader>();
```

- [ ] **Step 3: Build**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/Chats/Readers/ChatSearchReader.cs src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): add postgres chat search reader"
```

---

## Task 4: Add Search Endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Response.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatResultResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatSnippetResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/ResponseMapper.cs`

Mirror the `GetChats` endpoint style exactly (`Request` record in `Endpoint.cs`, response classes with `required init` properties).

- [ ] **Step 1: Add response types**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatSnippetResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed class SearchChatSnippetResponse
{
    public required Guid MessageId { get; init; }

    public required string Role { get; init; }

    public required string Text { get; init; }
}
```

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/SearchChatResultResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed class SearchChatResultResponse
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required bool IsPinned { get; init; }

    public required DateTimeOffset? PinnedAt { get; init; }

    public required bool IsArchived { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required int MatchCount { get; init; }

    public required IReadOnlyCollection<SearchChatSnippetResponse> Snippets { get; init; }
}
```

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Response.cs`:

```csharp
namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed class Response
{
    public required IReadOnlyCollection<SearchChatResultResponse> Items { get; init; }

    public required int Total { get; init; }

    public required int Limit { get; init; }

    public required int Offset { get; init; }
}
```

- [ ] **Step 2: Add mapper**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/ResponseMapper.cs`:

```csharp
using Chat.Application.Chats.Queries.SearchChats;

namespace Chat.Api.Endpoints.Chats.SearchChats;

internal static class ResponseMapper
{
    public static Response ToResponse(ChatSearchReadModel readModel) => new()
    {
        Items = readModel.Chats
            .Select(ToResponse)
            .ToList(),
        Total = readModel.Total,
        Limit = readModel.Limit,
        Offset = readModel.Offset
    };

    private static SearchChatResultResponse ToResponse(ChatSearchResultReadModel chat) => new()
    {
        Id = chat.Id,
        Title = chat.Title,
        IsPinned = chat.IsPinned,
        PinnedAt = chat.PinnedAt,
        IsArchived = chat.IsArchived,
        CreatedAt = chat.CreatedAt,
        UpdatedAt = chat.UpdatedAt,
        MatchCount = chat.MatchCount,
        Snippets = chat.Snippets
            .Select(ToResponse)
            .ToList()
    };

    private static SearchChatSnippetResponse ToResponse(ChatSearchSnippetReadModel snippet) => new()
    {
        MessageId = snippet.MessageId,
        Role = snippet.Role,
        Text = snippet.Text
    };
}
```

- [ ] **Step 3: Add endpoint**

Create `src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints;
using Chat.Application.Chats;
using Chat.Application.Chats.Queries.SearchChats;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.Chats.SearchChats;

internal sealed record Request
(
    [property: QueryParam] string? Query,
    [property: QueryParam] bool IsArchived,
    [property: QueryParam] int? Limit,
    [property: QueryParam] int? Offset
);

internal sealed class Endpoint(ISender sender) : Endpoint<Request, Response>
{
    public const string RouteName = "Chat.Chats.Search";

    public override void Configure()
    {
        Get("/me/chats/search");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Search Chats")
                .WithDescription("Searches the authenticated user's chat history and returns chat-level results with matching snippets.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.Chats);
        });
    }

    public override async Task HandleAsync(Request request, CancellationToken ct)
    {
        SearchChatsQuery query = new
        (
            Query: request.Query ?? string.Empty,
            IsArchived: request.IsArchived,
            Limit: request.Limit ?? ChatLimits.DefaultQueryLimit,
            Offset: request.Offset ?? ChatLimits.DefaultQueryOffset
        );

        ErrorOr<ChatSearchReadModel> result = await sender.Send(query, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}
```

A missing `query` parameter binds to `null`, becomes an empty string, and is rejected by the validator with `400` — no endpoint-level validation needed. The literal `search` segment takes routing precedence over any `/me/chats/{id}` parameter route.

- [ ] **Step 4: Build Chat.Api**

Ask for elevated permissions, then run:

```bash
dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj
```

Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/services/Chat/Chat.Api/Endpoints/Chats/SearchChats
git commit -m "feat(chat): add search endpoint"
```

---

## Task 5: Final Verification

**Files:**
- No new files unless fixes are required.

- [ ] **Step 1: Run application tests**

Ask for elevated permissions, then run:

```bash
dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 2: Build solution**

Ask for elevated permissions, then run:

```bash
dotnet build Nova.slnx
```

Expected: build succeeds.

- [ ] **Step 3: Local Aspire smoke test**

Ask for elevated permissions, then run:

```bash
dotnet run --project Nova.AppHost/Nova.AppHost.csproj
```

Expected:

- AppHost starts; `Chat.MigrationWorker` applies the `ChatMessageSearchVector` migration cleanly.
- `Chat.Api` starts with no new configuration requirements (search adds none).

- [ ] **Step 4: Manual search behavior check**

Using local API tooling against `Chat.Api`:

```http
GET /v1/me/chats/search?query=memory&isArchived=false&limit=20&offset=0
```

Expected:

- Blank or missing query returns `400`.
- Empty result returns `200` with `items: []` and `total: 0`.
- After sending a message containing a unique word, searching that word immediately returns the chat (no indexing delay).
- Results contain at most 3 snippets per chat; snippet text is plain (no `<b>` or other markup — if markup appears, the `ts_headline` `StartSel=""`/`StopSel=""` options need fixing).
- A chat whose title matches but whose messages do not is returned with `matchCount: 0` and `snippets: []`.
- `isArchived=true` returns only archived chats; temporary chats never appear.
- A query with only punctuation (e.g. `?query=%2B%2B`) returns `200` with empty items, not an error.

Stop the AppHost after verifying.

- [ ] **Step 5: Commit final fixes**

If final verification required fixes:

```bash
git status --short
git add src/services/Chat tests/Chat
git commit -m "fix(chat): stabilize search implementation"
```

If no fixes were required, do not create an empty commit.

---

## Self-Review Checklist

- [ ] Spec coverage: search endpoint, archive filter, temporary chats excluded, `Completed`-only messages, ≤3 plain snippets, matchCount, title-only matches returned, exact totals, immediate freshness.
- [ ] Domain isolation: no changes to `ChatMessage`, `ChatDbContext`, or any EF configuration; `search_vector` exists only in the migration and the reader SQL.
- [ ] Configuration consistency: `'simple'` used in the generated column, both query statements' `to_tsvector`/`websearch_to_tsquery` calls, and `ts_headline`.
- [ ] Count and page statements use identical filter predicates.
- [ ] Placeholder scan: no committed code contains `TODO`, `TBD`, `NotImplementedException`, or pseudocode from this plan.
- [ ] Project constraints: `Mediator` package (not MediatR), FastEndpoints style, migration follows the hand-written `ChatPinArchive` pattern (no `.Designer.cs`).
- [ ] Test permission: tests are included because the user explicitly approved them.
