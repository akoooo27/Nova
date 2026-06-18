# Temporary Chat Cleanup (Hangfire) — Design Spec

**Goal:** Add the project's first background job: a recurring task that **deletes temporary chats whose last activity (`UpdatedAt`) is older than a configurable retention window** (default **30 days**). The job runs on [Hangfire](https://www.hangfire.io/) with PostgreSQL storage, hosted in a new `Chat.CleanupWorker`. A Hangfire Dashboard (background/recurring/failed jobs + history) is exposed **behind the BFF** and gated by an Auth0 permission.

**Builds on:** `docs/superpowers/specs/2026-06-14-temporary-chat-design.md`, which introduced the immutable `IsTemporary` flag and explicitly deferred retention to "a separate background job (runs nightly; deletes temporary threads, messages follow via cascade)." This spec is that job. It also extends the BFF reverse-proxy surface from the existing chat-api routing.

---

## 1. Scope

**In scope**

- Hangfire wired into the solution with PostgreSQL storage (dedicated `hangfire` schema on `chat-db`).
- A correctly configured Hangfire **server/worker** in a new `Chat.CleanupWorker` project.
- A **recurring** job that deletes expired temporary chats.
- A **configurable** retention window and schedule.
- A Hangfire **Dashboard** (background jobs, recurring jobs, failed jobs, history) **secured behind the BFF** via an Auth0 permission, with a defense-in-depth gateway secret at the worker.

**Out of scope (YAGNI)**

- A per-chat `ExpiresAt` column. "Expiry" is defined purely as the retention window over `UpdatedAt`.
- Cross-service `ChatDeleted` integration events / outbox messages (nothing consumes one today).
- Redis turn-stream cleanup.
- Read-gating of temporary chats (separate concern, no such endpoints exist yet).

---

## 2. Binding rules inherited from the codebase

These constrain the decisions below.

1. **Clean architecture dependency direction.** `Infrastructure → Application → Domain`. The Application cleanup service must not depend on Infrastructure options or on Hangfire. Hangfire is a scheduling/infrastructure concern only.
2. **Mediator commands are for request-shaped use cases.** Writes driven by user intent go through Mediator. This job is system-triggered maintenance with no validation/authorization/user input, so it uses a plain application **service** instead — consistent with the existing `ChatTurnOrchestrator`/`ContextBuilder` services the turn worker calls directly. (Going through Mediator would also force an `ErrorOr` return purely to satisfy the `TResponse : IErrorOr` pipeline constraint.)
3. **Repositories own data access.** Bulk deletion (and its batching) is a method on `IChatRepository`, not ad-hoc SQL in the calling service.
4. **Time comes from `IDateTimeProvider`.** The cutoff is computed from `IDateTimeProvider.UtcNow`, never `DateTimeOffset.UtcNow` directly — keeps the service unit-testable.
5. **Workers follow the `Chat.TurnWorker` pattern.** A `WebApplication` with `AddServiceDefaults()`, an Npgsql data source + `ChatDbContext`, `AddApplication()`, a worker-specific infrastructure registration, and `MapDefaultEndpoints()`.
6. **BFF is the single auth-termination point.** Browser-facing surfaces authenticate at the BFF (Auth0 OIDC cookie) and are reverse-proxied to internal services.
7. **Central package management.** New packages are pinned in `Directory.Packages.props`.

---

## 3. Architecture

### 3.1 Domain — `IChatRepository`

Add one method:

```csharp
Task<int> DeleteExpiredTemporaryChatsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
```

Returns the number of chats deleted (for logging/observability). No domain entity changes — retention is a policy applied to existing state (`IsTemporary`, `UpdatedAt`).

### 3.2 Infrastructure — `ChatRepository`

Implement with a **batched** EF Core set-based delete — select up to `batchSize` expired chat ids (oldest first), delete that bounded set, and loop until a partial batch is returned:

```csharp
public async Task<int> DeleteExpiredTemporaryChatsAsync(
    DateTimeOffset olderThan,
    CancellationToken cancellationToken = default)
{
    const int batchSize = 1000;
    int totalDeleted = 0;

    while (true)
    {
        List<ChatId> batch = await db.ChatThreads
            .Where(chat => chat.IsTemporary && chat.UpdatedAt < olderThan)
            .OrderBy(chat => chat.UpdatedAt)
            .Take(batchSize)
            .Select(chat => chat.Id)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            break;
        }

        totalDeleted += await db.ChatThreads
            .Where(chat => batch.Contains(chat.Id) && chat.IsTemporary && chat.UpdatedAt < olderThan)
            .ExecuteDeleteAsync(cancellationToken);

        if (batch.Count < batchSize)
        {
            break;
        }
    }

    return totalDeleted;
}
```

- **Batched, not one giant statement.** Each iteration deletes at most `batchSize` (1000) chats in its own small transaction, so the job never holds locks across the whole expired set — important because `chats`/`chat_messages` take concurrent writes from the chat-api and turn-worker. The id-select-then-delete shape is required because PostgreSQL has no `DELETE … LIMIT`, so `Take` cannot be applied directly to `ExecuteDelete`. The predicate is re-checked in the delete so a chat updated (made active) between select and delete is skipped.
- **Messages are removed by the database**, via the existing `fk_chat_messages_chats_chat_id` `ON DELETE CASCADE` (verified in migration `20260610184542_ChatTree`). The self-referential `parent_message_id` FK is `NO ACTION`, which PostgreSQL checks at statement end; because the entire message tree of each deleted chat is removed in the same cascaded statement, the constraint is satisfied.

**Trade-off — `ExecuteDeleteAsync` bypasses the change tracker**, so no domain events and no MassTransit outbox message are produced. This is intentional: it avoids materializing thousands of aggregates + messages, and nothing currently reacts to a chat deletion. If a `ChatDeleted` integration event is ever needed, revisit (load-in-batches + `SaveChanges`, or raise an event explicitly).

**Batch size** is a fixed `1000` in the repository (a data-access tuning detail, not business policy). It can be promoted to configuration later if needed; it is intentionally *not* part of the Application service surface.

### 3.3 Application — temporary-chat cleanup service

A plain application **service** (not a Mediator command) under `Chat.Application/Chats/Cleanup/`. This is a system-triggered maintenance operation with no user input, validation, or authorization, so the Mediator pipeline would add only ceremony — and Hangfire is already the error/retry boundary, so an `ErrorOr` return buys nothing. This mirrors the existing application-service precedent (`ChatTurnOrchestrator`, `ContextBuilder`), which the turn worker calls directly rather than through Mediator.

```csharp
public interface ITemporaryChatCleaner
{
    Task<int> DeleteExpiredAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default);
}
```

```csharp
internal sealed class TemporaryChatCleaner(
    IChatRepository chats,
    IDateTimeProvider dateTimeProvider) : ITemporaryChatCleaner
{
    public Task<int> DeleteExpiredAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default) =>
        chats.DeleteExpiredTemporaryChatsAsync(dateTimeProvider.UtcNow - retentionPeriod, cancellationToken);
}
```

- The **retention window is passed in**, supplied by the worker from bound configuration. This keeps the Application layer free of Infrastructure options and Hangfire while remaining the single place that turns "retention" into a "cutoff" (computed from `IDateTimeProvider`, so it is unit-testable).
- Returns a plain `int` (deleted count). Exceptions propagate to Hangfire, which records the failed run and retries.

### 3.4 Worker — `Chat.CleanupWorker` (new project)

Built out from the empty `src/workers/Chat.CleanupWorker` placeholder, following the `Chat.TurnWorker` shape. It is the **only** project that references Hangfire.

**`Program.cs` (outline):**

```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("chat-db");

builder.Services.AddDbContext<ChatDbContext>((sp, options) =>
{
    NpgsqlDataSource dataSource = sp.GetRequiredService<NpgsqlDataSource>();
    options.UseNpgsql(dataSource).UseSnakeCaseNamingConvention();
});
builder.EnrichNpgsqlDbContext<ChatDbContext>();

builder.Services.AddApplication();
builder.Services.AddCleanupWorkerInfrastructure();   // shared infra + DB services (IChatRepository, IDateTimeProvider)
builder.Services.AddTemporaryChatCleanup(builder.Configuration);  // options + Hangfire storage/server + dashboard auth filter

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapTemporaryChatCleanupDashboard();   // MapHangfireDashboard("/admin/hangfire", ...)
app.UseTemporaryChatCleanupRecurringJob(); // IRecurringJobManager.AddOrUpdate(...) from options

await app.RunAsync();
```

**Pieces inside the worker:**

- **`TemporaryChatCleanupOptions`** (section `TemporaryChatCleanup`): `RetentionPeriod` (`TimeSpan`, default `30.00:00:00`) and `Cron` (`string`, default `0 3 * * *` = daily 03:00 UTC). Bound with `ValidateDataAnnotations().ValidateOnStart()`.
- **`TemporaryChatCleanupJob`** — thin adapter resolving `ITemporaryChatCleaner` + `IOptions<TemporaryChatCleanupOptions>` + logger; calls `DeleteExpiredAsync(options.RetentionPeriod)` and logs the returned count. Exceptions are left to propagate so Hangfire marks the run failed. Recurring job id: `temporary-chat-cleanup`, registered via `IRecurringJobManager.AddOrUpdate<TemporaryChatCleanupJob>(...)` with the configured cron in UTC.
- **`HangfireDashboardGatewayFilter : IDashboardAuthorizationFilter`** — defense-in-depth: validates the gateway secret header injected by the BFF; rejects otherwise.

**Infrastructure registration:** add `AddCleanupWorkerInfrastructure(this IServiceCollection)` to `Chat.Infrastructure/DependencyInjection.cs`, mirroring `AddTurnWorkerInfrastructure` but minimal — `AddSharedInfrastructure()` (gives `IDateTimeProvider`) + `AddDatabaseServices()` (gives `IChatRepository`, `IUnitOfWork`, domain-events dispatcher needed to construct `ChatDbContext`) + `AddScoped<ITemporaryChatCleaner, TemporaryChatCleaner>()` (the same place `ChatTurnOrchestrator` is registered for the turn worker). No messaging, Redis, or turn pipeline. `AddApplication()` is still called in the worker (it registers Mediator, which the domain-events dispatcher depends on), but cleanup no longer flows through Mediator. Hangfire wiring (`AddTemporaryChatCleanup`) lives in the worker project, not in `Chat.Infrastructure`, so the Infrastructure assembly stays free of the Hangfire dependency.

### 3.5 Storage

`Hangfire.PostgreSql` against the `chat-db` connection string, configured with `SchemaName = "hangfire"` and schema auto-preparation. Hangfire's bookkeeping tables live in their own schema, isolated from application tables. The Hangfire server runs with a small worker count (this deployable runs a single lightweight recurring job).

### 3.6 BFF — Dashboard proxy + authorization

Add a **plain YARP route** (modeled on `FrontendProxyConfiguration`, not the Duende remote-API machinery — the Dashboard is browser-navigated, same-origin under the BFF, and ships its own CSRF tokens):

- New `HangfireDashboardProxyConfiguration` with route `/admin/hangfire/{**catch-all}` → a `hangfire-dashboard` cluster whose destination is the worker address. **No path rewrite** (the worker mounts the Dashboard at the same `/admin/hangfire` prefix so generated links line up). The route carries `AuthorizationPolicy = "HangfireDashboard"` and a request-header transform that sets the gateway-secret header.
- **Authorization policy** `HangfireDashboard`: `RequireAuthenticatedUser()` + `RequireClaim("permissions", <configured permission>)` (default `jobs:read`).
- **Permissions enrichment:** Auth0 RBAC `permissions` arrive on the **access token**, not the cookie principal. Add `OnTokenValidated` to the BFF OIDC options to copy `permissions` claims from the access token into the principal at sign-in, so the cookie-backed policy can evaluate them. (The existing `PermissionsClaimsEnricher`, which reads the access token for `/bff/user`, is unaffected.)
- The route is registered alongside the existing routes in `LoadFromMemory`; it is **not** marked with `.WithAntiforgeryCheck()`, so the BFF antiforgery middleware (which acts on BFF-marked endpoints) does not block the Dashboard's POSTs.

**BFF config** (`HangfireDashboard` section): `Address` (worker endpoint), `RequiredPermission` (default `jobs:read`), `GatewaySecret`.

### 3.7 AppHost (Aspire) wiring

- Add `hangfire-gateway-secret` as a secret `AddParameter`.
- Register the worker:
  ```csharp
  builder.AddProject<Projects.Chat_CleanupWorker>("chat-cleanup-worker")
      .WithHttpEndpoint(name: "http")
      .WithReference(chatDb)
      .WaitForCompletion(chatMigrations)
      .WithEnvironment("HangfireDashboard__GatewaySecret", hangfireGatewaySecret);
  ```
- Wire the BFF to reach it:
  ```csharp
  bff
      .WithEnvironment("HangfireDashboard__Address", cleanupWorker.GetEndpoint("http"))
      .WithEnvironment("HangfireDashboard__GatewaySecret", hangfireGatewaySecret)
      .WithReference(cleanupWorker)
      .WaitFor(cleanupWorker);
  ```
  (`RequiredPermission` can stay defaulted or be supplied via config.)

The worker's HTTP endpoint is internal — reached by the BFF over Aspire service discovery. Production exposure is controlled by the deployment topology; the gateway secret is the in-app backstop.

---

## 4. Data flow

**Cleanup (scheduled):**
```
Hangfire cron (TemporaryChatCleanup:Cron)
  → TemporaryChatCleanupJob.RunAsync
  → ITemporaryChatCleaner.DeleteExpiredAsync(RetentionPeriod)
  → cutoff = IDateTimeProvider.UtcNow - RetentionPeriod
  → IChatRepository.DeleteExpiredTemporaryChatsAsync(cutoff)
  → loop: DELETE batches of ≤1000 chats WHERE is_temporary AND updated_at < cutoff  (DB cascade → chat_messages)
  → returns total deleted count → logged (exceptions → Hangfire failed run + retry)
```

**Dashboard (interactive):**
```
Browser → https://<bff>/admin/hangfire
  → BFF: Auth0 cookie auth + HangfireDashboard policy (permissions claim)
  → YARP proxy to internal worker, injecting gateway-secret header
  → worker IDashboardAuthorizationFilter validates secret
  → Hangfire Dashboard renders (jobs / recurring / failed / history)
```

---

## 5. Security model (defense in depth)

| Layer | Control |
|---|---|
| BFF | Auth0 OIDC cookie session; `HangfireDashboard` policy requires the configured `permissions` claim before proxying. |
| Network | Worker Dashboard endpoint is internal-only; not published publicly. |
| Worker | `IDashboardAuthorizationFilter` validates the BFF-injected gateway secret; direct hits without it are rejected. |

The Dashboard is left write-enabled (trigger/requeue/delete) since access is admin-gated. Flipping it to read-only is a one-line `DashboardOptions.IsReadOnlyFunc` change if desired.

---

## 6. Configuration summary

| Setting | Location | Default |
|---|---|---|
| `TemporaryChatCleanup:RetentionPeriod` | CleanupWorker | `30.00:00:00` (30 days) |
| `TemporaryChatCleanup:Cron` | CleanupWorker | `0 3 * * *` (daily 03:00 UTC) |
| `HangfireDashboard:Address` | BFF | worker endpoint (Aspire) |
| `HangfireDashboard:RequiredPermission` | BFF | `jobs:read` |
| `HangfireDashboard:GatewaySecret` | BFF + CleanupWorker | Aspire `hangfire-gateway-secret` |

---

## 7. Packages (central, `Directory.Packages.props`)

- `Hangfire.Core`
- `Hangfire.AspNetCore` (server hosting + Dashboard middleware)
- `Hangfire.PostgreSql` (storage)

**Risk to verify at implementation:** `Hangfire.PostgreSql` must restore and run against the solution's Npgsql 10 stack. Confirm the chosen version's Npgsql dependency is compatible; adjust the pinned version if needed.

---

## 8. Testing

`TemporaryChatCleaner` is a clean unit test target (fake `IChatRepository` capturing the cutoff + a fixed `IDateTimeProvider`), asserting `cutoff == now - RetentionPeriod` and that the repository result is returned. (Batching is a repository/integration concern, verified against a real database, not in this unit test.) Per `AGENTS.md`, no tests will be written or modified without explicit user approval; this will be raised before implementation.

---

## 9. Affected files

**New**
- `src/workers/Chat.CleanupWorker/Chat.CleanupWorker.csproj`
- `src/workers/Chat.CleanupWorker/Program.cs`
- `src/workers/Chat.CleanupWorker/appsettings.json`
- `src/workers/Chat.CleanupWorker/TemporaryChatCleanupOptions.cs`
- `src/workers/Chat.CleanupWorker/TemporaryChatCleanupJob.cs`
- `src/workers/Chat.CleanupWorker/HangfireDashboardGatewayFilter.cs`
- `src/workers/Chat.CleanupWorker/DependencyInjection.cs` (`AddTemporaryChatCleanup`, dashboard + recurring-job mapping)
- `src/services/Chat/Chat.Application/Chats/Cleanup/ITemporaryChatCleaner.cs`
- `src/services/Chat/Chat.Application/Chats/Cleanup/TemporaryChatCleaner.cs`
- `src/services/BFF/RemoteApis/HangfireDashboardProxyConfiguration.cs`

**Modified**
- `Directory.Packages.props` (Hangfire packages)
- `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs` (delete method)
- `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs` (impl)
- `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs` (`AddCleanupWorkerInfrastructure`)
- `src/services/BFF/Program.cs` (route registration, authorization policy, OIDC permissions enrichment)
- `Nova.AppHost/AppHost.cs` (worker project, gateway-secret parameter, BFF wiring)
- `Nova.slnx` (add `Chat.CleanupWorker`)
