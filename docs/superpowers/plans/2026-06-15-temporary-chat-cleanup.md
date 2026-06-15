# Temporary Chat Cleanup (Hangfire) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the project's first background job — a Hangfire recurring task that deletes temporary chats whose last activity (`UpdatedAt`) is older than a configurable retention window (default 30 days) — with a Dashboard exposed behind the BFF and gated by an Auth0 permission.

**Architecture:** Cleanup logic is a plain application service in `Chat.Application` (`ITemporaryChatCleaner` → `IChatRepository.DeleteExpiredTemporaryChatsAsync`, a batched EF `ExecuteDeleteAsync`, DB cascade removes messages) — not a Mediator command, since it is system-triggered maintenance with no validation/authorization/user input. A new `Chat.CleanupWorker` (WebApplication, TurnWorker pattern) hosts the Hangfire server (PostgreSQL storage, `hangfire` schema on `chat-db`) and the Dashboard at `/admin/hangfire`. The BFF reverse-proxies `/admin/hangfire` to the internal worker, enforcing an authorization policy (Auth0 `permissions` claim) and injecting a gateway-secret header that the worker validates as defense-in-depth.

**Tech Stack:** .NET 10, .NET Aspire 13.4, Hangfire (Core/AspNetCore/PostgreSql), EF Core 10 + Npgsql, Mediator (registered for the `ChatDbContext` domain-events dispatcher), Duende.BFF + YARP, Auth0.

**Spec:** `docs/superpowers/specs/2026-06-15-temporary-chat-cleanup-design.md`

**Conventions for every commit in this plan:** conventional title + a short description body; **no `Co-Authored-By` trailer** (per the user's saved preference).

**Testing note:** `AGENTS.md` forbids writing/modifying tests without explicit user approval. Tasks 1–7 therefore verify via `dotnet build`. Task 8 (the unit test) is **optional and must not be executed without the user's go-ahead**.

---

## File Structure

**New files**
- `src/workers/Chat.CleanupWorker/Chat.CleanupWorker.csproj` — worker project (Exe, ASP.NET + Aspire + Hangfire).
- `src/workers/Chat.CleanupWorker/appsettings.json` — logging + `TemporaryChatCleanup` defaults.
- `src/workers/Chat.CleanupWorker/TemporaryChatCleanupOptions.cs` — `RetentionPeriod` + `Cron`.
- `src/workers/Chat.CleanupWorker/TemporaryChatCleanupJob.cs` — Hangfire job; calls `ITemporaryChatCleaner`.
- `src/workers/Chat.CleanupWorker/HangfireDashboardGatewayFilter.cs` — `IDashboardAuthorizationFilter` validating the gateway secret.
- `src/workers/Chat.CleanupWorker/DependencyInjection.cs` — `AddTemporaryChatCleanup`, `MapTemporaryChatCleanupDashboard`, `UseTemporaryChatCleanupRecurringJob`.
- `src/workers/Chat.CleanupWorker/Program.cs` — composition root.
- `src/services/Chat/Chat.Application/Chats/Cleanup/ITemporaryChatCleaner.cs` — cleanup service interface.
- `src/services/Chat/Chat.Application/Chats/Cleanup/TemporaryChatCleaner.cs` — implementation (computes cutoff, calls the repository).
- `src/services/BFF/RemoteApis/HangfireDashboardProxyConfiguration.cs` — YARP route/cluster + policy constants.
- *(Task 8, optional)* `tests/Chat/Chat.Application.Tests/Chats/FakeChatRepository.cs`, `tests/Chat/Chat.Application.Tests/Chats/TemporaryChatCleanerTests.cs`

**Modified files**
- `Directory.Packages.props` — Hangfire package versions.
- `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs` — delete method.
- `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs` — implementation.
- `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs` — `AddCleanupWorkerInfrastructure`.
- `Nova.slnx` — add the worker project.
- `Nova.AppHost/Nova.AppHost.csproj` — ProjectReference to the worker.
- `Nova.AppHost/AppHost.cs` — gateway-secret parameter, worker registration, BFF wiring.
- `src/services/BFF/Program.cs` — authorization policy, route registration, OIDC permissions enrichment.

---

## Task 1: Add Hangfire packages (central package management)

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add the Hangfire package versions**

In `Directory.Packages.props`, inside the `<ItemGroup>`, add this block immediately after the `<!-- Infrastructure -->` packages (just before the `<!-- Api -->` comment):

```xml
    <!-- Background Jobs -->
    <PackageVersion Include="Hangfire.Core" Version="1.8.21" />
    <PackageVersion Include="Hangfire.AspNetCore" Version="1.8.21" />
    <PackageVersion Include="Hangfire.PostgreSql" Version="1.20.12" />
```

- [ ] **Step 2: Verify restore succeeds**

Run: `dotnet restore Nova.slnx`
Expected: `Restored ...` with no `NU1*` version-conflict errors.

If restore reports a version that does not exist, replace it with the latest stable on nuget.org for that package id (Hangfire.Core/AspNetCore share a version; Hangfire.PostgreSql is versioned independently). If it reports an Npgsql conflict, confirm the chosen `Hangfire.PostgreSql` version's Npgsql dependency allows Npgsql 10 (the solution pins Npgsql 10 via Aspire.Npgsql); pick a `Hangfire.PostgreSql` version whose dependency range includes 10.x.

- [ ] **Step 3: Commit**

```bash
git add Directory.Packages.props
git commit -m "chore(deps): add Hangfire packages for background jobs" -m "Pin Hangfire.Core/AspNetCore/PostgreSql centrally for the cleanup worker."
```

---

## Task 2: Repository bulk-delete for expired temporary chats (Domain + Infrastructure)

**Files:**
- Modify: `src/services/Chat/Chat.Domain/Chats/IChatRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs`

- [ ] **Step 1: Add the method to the repository interface**

In `IChatRepository.cs`, add a third member after `void Add(ChatThread chat);`:

```csharp
    void Add(ChatThread chat);

    Task<int> DeleteExpiredTemporaryChatsAsync
    (
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default
    );
```

- [ ] **Step 2: Implement it in `ChatRepository`**

In `ChatRepository.cs`, add this method after `Add`. It deletes in **bounded batches** (select ids oldest-first, then delete that set) so the job never opens one huge transaction that contends with live chat traffic:

```csharp
    public void Add(ChatThread chat)
    {
        db.ChatThreads.Add(chat);
    }

    public async Task<int> DeleteExpiredTemporaryChatsAsync
    (
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default
    )
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

Notes:
- `ExecuteDeleteAsync` and `ToListAsync` live in `Microsoft.EntityFrameworkCore` (already imported); `ChatId` is already imported in `ChatRepository.cs` (used by `GetByIdAsync`).
- Each batch `DELETE` cascades to `chat_messages` via the existing `ON DELETE CASCADE` FK; no `SaveChanges` is involved.
- The id-select-then-delete shape is deliberate: PostgreSQL has no `DELETE … LIMIT`, so `Take` cannot be translated directly onto `ExecuteDelete`. The predicate is repeated in the delete so a chat updated (made active) between select and delete is not removed.
- If `batch.Contains(chat.Id)` fails to translate over the value-converted `ChatId`, project ids to their primitive instead (`.Select(chat => chat.Id.Value)` → `List<Guid>`, compared against `chat.Id.Value`).

- [ ] **Step 3: Verify build**

Run: `dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Domain/Chats/IChatRepository.cs src/services/Chat/Chat.Infrastructure/Chats/Repositories/ChatRepository.cs
git commit -m "feat(chat): add batched delete for expired temporary chats" -m "Delete temporary chats older than a cutoff in bounded batches (id-select then ExecuteDeleteAsync); messages cascade at the database."
```

---

## Task 3: Application cleanup service

**Files:**
- Create: `src/services/Chat/Chat.Application/Chats/Cleanup/ITemporaryChatCleaner.cs`
- Create: `src/services/Chat/Chat.Application/Chats/Cleanup/TemporaryChatCleaner.cs`

- [ ] **Step 1: Create the service interface**

Create `ITemporaryChatCleaner.cs`:

```csharp
namespace Chat.Application.Chats.Cleanup;

public interface ITemporaryChatCleaner
{
    Task<int> DeleteExpiredAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Create the implementation**

Create `TemporaryChatCleaner.cs`:

```csharp
using Chat.Domain.Chats;

using SharedKernel;

namespace Chat.Application.Chats.Cleanup;

internal sealed class TemporaryChatCleaner(
    IChatRepository chats,
    IDateTimeProvider dateTimeProvider) : ITemporaryChatCleaner
{
    public Task<int> DeleteExpiredAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default)
    {
        DateTimeOffset cutoff = dateTimeProvider.UtcNow - retentionPeriod;

        return chats.DeleteExpiredTemporaryChatsAsync(cutoff, cancellationToken);
    }
}
```

(A plain service — not a Mediator command — because this is system-triggered maintenance with no validation/authorization/user input, and Hangfire is the error/retry boundary. Matches the existing `ChatTurnOrchestrator` service pattern. `IDateTimeProvider` is in `SharedKernel`; `IChatRepository` in `Chat.Domain.Chats`.)

- [ ] **Step 3: Verify build**

Run: `dotnet build src/services/Chat/Chat.Application/Chat.Application.csproj`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/services/Chat/Chat.Application/Chats/Cleanup/
git commit -m "feat(chat): add temporary chat cleanup service" -m "ITemporaryChatCleaner computes the cutoff from IDateTimeProvider and the retention period, then delegates to the repository."
```

---

## Task 4: Cleanup worker infrastructure registration

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add `AddCleanupWorkerInfrastructure`**

In `DependencyInjection.cs`, add this public method directly after `AddTurnWorkerInfrastructure` (it reuses the existing private `AddDatabaseServices` and `AddSharedInfrastructure`):

```csharp
    public static IServiceCollection AddCleanupWorkerInfrastructure(this IServiceCollection services) =>
        services
            .AddSharedInfrastructure()
            .AddDatabaseServices()
            .AddScoped<ITemporaryChatCleaner, TemporaryChatCleaner>();
```

Add `using Chat.Application.Chats.Cleanup;` to the file's `using` directives. This registers `IDateTimeProvider`, `IChatRepository`, `IUnitOfWork`, the domain-events dispatcher needed to construct `ChatDbContext`, and the cleanup service (registered here the same way `ChatTurnOrchestrator` is for the turn worker). No messaging, Redis, or turn pipeline.

- [ ] **Step 2: Verify build**

Run: `dotnet build src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "feat(chat): add cleanup worker infrastructure registration" -m "Minimal DI (shared infra + database services) for the cleanup worker, free of messaging and caching."
```

---

## Task 5: `Chat.CleanupWorker` project (Hangfire server + Dashboard)

**Files:**
- Create: `src/workers/Chat.CleanupWorker/Chat.CleanupWorker.csproj`
- Create: `src/workers/Chat.CleanupWorker/appsettings.json`
- Create: `src/workers/Chat.CleanupWorker/TemporaryChatCleanupOptions.cs`
- Create: `src/workers/Chat.CleanupWorker/HangfireDashboardGatewayFilter.cs`
- Create: `src/workers/Chat.CleanupWorker/TemporaryChatCleanupJob.cs`
- Create: `src/workers/Chat.CleanupWorker/DependencyInjection.cs`
- Create: `src/workers/Chat.CleanupWorker/Program.cs`
- Modify: `Nova.slnx`
- Modify: `Nova.AppHost/Nova.AppHost.csproj`

- [ ] **Step 1: Create the project file**

Create `src/workers/Chat.CleanupWorker/Chat.CleanupWorker.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />

    <PackageReference Include="Aspire.Npgsql" />
    <PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="EFCore.NamingConventions" />

    <PackageReference Include="Hangfire.Core" />
    <PackageReference Include="Hangfire.AspNetCore" />
    <PackageReference Include="Hangfire.PostgreSql" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\Nova.ServiceDefaults\Nova.ServiceDefaults.csproj" />
    <ProjectReference Include="..\..\services\Chat\Chat.Application\Chat.Application.csproj" />
    <ProjectReference Include="..\..\services\Chat\Chat.Infrastructure\Chat.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create `appsettings.json`**

Create `src/workers/Chat.CleanupWorker/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Hangfire": "Information"
    }
  },
  "TemporaryChatCleanup": {
    "RetentionPeriod": "30.00:00:00",
    "Cron": "0 3 * * *"
  }
}
```

- [ ] **Step 3: Create `TemporaryChatCleanupOptions`**

Create `src/workers/Chat.CleanupWorker/TemporaryChatCleanupOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace Chat.CleanupWorker;

public sealed class TemporaryChatCleanupOptions
{
    public const string SectionName = "TemporaryChatCleanup";

    /// <summary>How long after its last update a temporary chat is kept before deletion.</summary>
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromDays(30);

    /// <summary>Cron expression (UTC) controlling how often the cleanup runs.</summary>
    [Required]
    public string Cron { get; init; } = "0 3 * * *";
}
```

- [ ] **Step 4: Create the Dashboard gateway filter**

Create `src/workers/Chat.CleanupWorker/HangfireDashboardGatewayFilter.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

using Hangfire.Dashboard;

using Microsoft.AspNetCore.Http;

namespace Chat.CleanupWorker;

/// <summary>
/// Defense-in-depth: only allow Dashboard requests that carry the shared secret the BFF injects.
/// The BFF is the primary auth gate; this rejects anything reaching the worker directly.
/// </summary>
internal sealed class HangfireDashboardGatewayFilter(string expectedSecret) : IDashboardAuthorizationFilter
{
    public const string HeaderName = "X-Hangfire-Gateway";

    public bool Authorize(DashboardContext context)
    {
        HttpContext httpContext = context.GetHttpContext();

        string? provided = httpContext.Request.Headers[HeaderName];

        if (string.IsNullOrEmpty(provided) || string.IsNullOrEmpty(expectedSecret))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expectedSecret));
    }
}
```

- [ ] **Step 5: Create the Hangfire job**

Create `src/workers/Chat.CleanupWorker/TemporaryChatCleanupJob.cs`:

```csharp
using Chat.Application.Chats.Cleanup;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.CleanupWorker;

public sealed class TemporaryChatCleanupJob(
    ITemporaryChatCleaner cleaner,
    IOptions<TemporaryChatCleanupOptions> options,
    ILogger<TemporaryChatCleanupJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TimeSpan retention = options.Value.RetentionPeriod;

        int deleted = await cleaner.DeleteExpiredAsync(retention, cancellationToken);

        logger.LogInformation(
            "Temporary chat cleanup deleted {Count} chats (retention {Retention}).",
            deleted,
            retention);
    }
}
```

(No `ErrorOr`/Mediator: the job calls the service directly. An exception from cleanup propagates out of `RunAsync`, which is exactly how Hangfire records a failed run and applies its retry policy — visible in the Dashboard.)

- [ ] **Step 6: Create the worker DI / wiring extensions**

Create `src/workers/Chat.CleanupWorker/DependencyInjection.cs`:

```csharp
using Hangfire;
using Hangfire.PostgreSql;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chat.CleanupWorker;

public static class DependencyInjection
{
    private const string DashboardPath = "/admin/hangfire";
    private const string RecurringJobId = "temporary-chat-cleanup";

    public static WebApplicationBuilder AddTemporaryChatCleanup(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<TemporaryChatCleanupOptions>()
            .Bind(builder.Configuration.GetSection(TemporaryChatCleanupOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.RetentionPeriod > TimeSpan.Zero, "RetentionPeriod must be positive.")
            .ValidateOnStart();

        string connectionString = builder.Configuration.GetConnectionString("chat-db")
            ?? throw new InvalidOperationException("Connection string 'chat-db' is required.");

        builder.Services.AddHangfire(configuration => configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                postgres => postgres.UseNpgsqlConnection(connectionString),
                new PostgreSqlStorageOptions { SchemaName = "hangfire" }));

        builder.Services.AddHangfireServer(options => options.WorkerCount = 2);

        builder.Services.AddScoped<TemporaryChatCleanupJob>();

        return builder;
    }

    public static WebApplication MapTemporaryChatCleanupDashboard(this WebApplication app)
    {
        string gatewaySecret = app.Configuration["HangfireDashboard:GatewaySecret"]
            ?? throw new InvalidOperationException("'HangfireDashboard:GatewaySecret' is required.");

        app.MapHangfireDashboard(DashboardPath, new DashboardOptions
        {
            Authorization = [new HangfireDashboardGatewayFilter(gatewaySecret)],
            DisplayStorageConnectionString = false
        });

        return app;
    }

    public static WebApplication UseTemporaryChatCleanupRecurringJob(this WebApplication app)
    {
        IRecurringJobManager recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        TemporaryChatCleanupOptions options = app.Services
            .GetRequiredService<IOptions<TemporaryChatCleanupOptions>>().Value;

        recurringJobs.AddOrUpdate<TemporaryChatCleanupJob>(
            RecurringJobId,
            job => job.RunAsync(CancellationToken.None),
            options.Cron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

        return app;
    }
}
```

(`DashboardOptions`, `MapHangfireDashboard`, and `context.GetHttpContext()` come from `Hangfire`/`Hangfire.Dashboard` in `Hangfire.AspNetCore`. Hangfire's ASP.NET Core integration resolves `TemporaryChatCleanupJob` from a DI scope at execution and substitutes a real `CancellationToken` for `CancellationToken.None`.)

- [ ] **Step 7: Create `Program.cs`**

Create `src/workers/Chat.CleanupWorker/Program.cs`:

```csharp
using Chat.Application;
using Chat.CleanupWorker;
using Chat.Infrastructure;
using Chat.Infrastructure.Database;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDataSource("chat-db");

builder.Services.AddDbContext<ChatDbContext>((sp, options) =>
{
    NpgsqlDataSource dataSource = sp.GetRequiredService<NpgsqlDataSource>();

    options
        .UseNpgsql(dataSource)
        .UseSnakeCaseNamingConvention();
});

builder.EnrichNpgsqlDbContext<ChatDbContext>();

builder.Services.AddApplication();
builder.Services.AddCleanupWorkerInfrastructure();

builder.AddTemporaryChatCleanup();

WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapTemporaryChatCleanupDashboard();
app.UseTemporaryChatCleanupRecurringJob();

await app.RunAsync();
```

- [ ] **Step 8: Register the project in the solution**

In `Nova.slnx`, add the worker inside the existing `/src/workers/` folder block (alongside the migration workers):

```xml
  <Folder Name="/src/workers/">
    <Project Path="src/workers/BFF.MigrationWorker/BFF.MigrationWorker.csproj" />
    <Project Path="src/workers/Chat.CleanupWorker/Chat.CleanupWorker.csproj" />
    <Project Path="src/workers/Chat.MigrationWorker/Chat.MigrationWorker.csproj" />
    <Project Path="src/workers/IdentityIngress.MigrationWorker/IdentityIngress.MigrationWorker.csproj" />
  </Folder>
```

- [ ] **Step 9: Add the AppHost ProjectReference**

In `Nova.AppHost/Nova.AppHost.csproj`, add to the `<ItemGroup>` of `<ProjectReference>`s (so Aspire generates `Projects.Chat_CleanupWorker`):

```xml
    <ProjectReference Include="..\src\workers\Chat.CleanupWorker\Chat.CleanupWorker.csproj" />
```

- [ ] **Step 10: Verify build**

Run: `dotnet build src/workers/Chat.CleanupWorker/Chat.CleanupWorker.csproj`
Expected: `Build succeeded.` with 0 errors.

If the compiler reports `UsePostgreSqlStorage` / `UseNpgsqlConnection` signature mismatches, the installed `Hangfire.PostgreSql` major version differs from the v1.20 API used here — check the package's README for the storage-configuration call shape and adjust Step 6 accordingly (keep `SchemaName = "hangfire"`).

- [ ] **Step 11: Commit**

```bash
git add src/workers/Chat.CleanupWorker/ Nova.slnx Nova.AppHost/Nova.AppHost.csproj
git commit -m "feat(chat): add Chat.CleanupWorker with Hangfire dashboard" -m "Worker hosts the Hangfire server (PostgreSQL storage, hangfire schema) and the Dashboard at /admin/hangfire, plus the recurring temporary-chat cleanup job."
```

---

## Task 6: AppHost orchestration

**Files:**
- Modify: `Nova.AppHost/AppHost.cs`

- [ ] **Step 1: Add the gateway-secret parameter**

In `AppHost.cs`, next to the other secret parameters (e.g., directly after the `exaApiKey` / `postHogProjectApiKey` declarations), add:

```csharp
IResourceBuilder<ParameterResource> hangfireGatewaySecret =
    builder.AddParameter("hangfire-gateway-secret", secret: true);
```

- [ ] **Step 2: Register the cleanup worker**

Directly after the `builder.AddProject<Projects.Chat_TurnWorker>("chat-turn-worker")...` block, add:

```csharp
IResourceBuilder<ProjectResource> chatCleanupWorker = builder
    .AddProject<Projects.Chat_CleanupWorker>("chat-cleanup-worker")
    .WithHttpEndpoint(port: 7300, name: "http")
    .WithEnvironment("HangfireDashboard__GatewaySecret", hangfireGatewaySecret)
    .WithReference(chatDb)
    .WaitForCompletion(chatMigrations);
```

- [ ] **Step 3: Wire the BFF to reach the Dashboard**

Replace the final `bff` configuration block:

```csharp
bff
    .WithEnvironment("ChatApi__Address", chatApi.GetEndpoint("https"))
    .WithReference(chatApi)
    .WaitFor(chatApi);
```

with:

```csharp
bff
    .WithEnvironment("ChatApi__Address", chatApi.GetEndpoint("https"))
    .WithEnvironment("HangfireDashboard__Address", chatCleanupWorker.GetEndpoint("http"))
    .WithEnvironment("HangfireDashboard__GatewaySecret", hangfireGatewaySecret)
    .WithReference(chatApi)
    .WithReference(chatCleanupWorker)
    .WaitFor(chatApi);
```

(The BFF does **not** `WaitFor` the worker — the Dashboard is non-critical to BFF startup. `WithReference` enables service discovery; the explicit `HangfireDashboard__Address` is what the YARP cluster uses.)

- [ ] **Step 4: Verify build**

Run: `dotnet build Nova.AppHost/Nova.AppHost.csproj`
Expected: `Build succeeded.` (`Projects.Chat_CleanupWorker` resolves from the Task 5 ProjectReference).

- [ ] **Step 5: Commit**

```bash
git add Nova.AppHost/AppHost.cs
git commit -m "feat(apphost): wire chat cleanup worker and gateway secret" -m "Register chat-cleanup-worker with an internal HTTP endpoint and pass the dashboard address + shared gateway secret to the BFF."
```

---

## Task 7: BFF — proxy the Dashboard, gate it on an Auth0 permission

**Files:**
- Create: `src/services/BFF/RemoteApis/HangfireDashboardProxyConfiguration.cs`
- Modify: `src/services/BFF/Program.cs`

- [ ] **Step 1: Create the proxy configuration**

Create `src/services/BFF/RemoteApis/HangfireDashboardProxyConfiguration.cs`:

```csharp
using Yarp.ReverseProxy.Configuration;

namespace BFF.RemoteApis;

internal static class HangfireDashboardProxyConfiguration
{
    public const string PolicyName = "HangfireDashboard";
    public const string PermissionClaimType = "permissions";

    private const string AddressConfigurationKey = "HangfireDashboard:Address";
    private const string GatewaySecretConfigurationKey = "HangfireDashboard:GatewaySecret";
    private const string RequiredPermissionConfigurationKey = "HangfireDashboard:RequiredPermission";

    private const string DefaultAddress = "http://localhost:7300";
    private const string DefaultRequiredPermission = "jobs:read";
    private const string GatewayHeaderName = "X-Hangfire-Gateway";

    private const string RouteId = "hangfire-dashboard";
    private const string ClusterId = "hangfire-dashboard";
    private const string DestinationId = "hangfire-dashboard";

    public static string GetRequiredPermission(IConfiguration configuration) =>
        configuration[RequiredPermissionConfigurationKey] ?? DefaultRequiredPermission;

    private static string GetAddress(IConfiguration configuration) =>
        configuration[AddressConfigurationKey] ?? DefaultAddress;

    private static string GetGatewaySecret(IConfiguration configuration) =>
        configuration[GatewaySecretConfigurationKey] ?? string.Empty;

    public static RouteConfig CreateRoute(IConfiguration configuration) =>
        new RouteConfig
        {
            RouteId = RouteId,
            ClusterId = ClusterId,
            AuthorizationPolicy = PolicyName,
            Match = new RouteMatch
            {
                Path = "/admin/hangfire/{**catch-all}",
            },
            Transforms =
            [
                new Dictionary<string, string>
                {
                    ["RequestHeader"] = GatewayHeaderName,
                    ["Set"] = GetGatewaySecret(configuration),
                },
            ],
        };

    public static ClusterConfig CreateCluster(IConfiguration configuration) =>
        new ClusterConfig
        {
            ClusterId = ClusterId,
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                [DestinationId] = new DestinationConfig
                {
                    Address = GetAddress(configuration),
                },
            },
        };
}
```

(No `PathPattern` transform: the path `/admin/hangfire/...` is forwarded unchanged so it lines up with the worker's Dashboard mount. The route is a plain YARP route — not a Duende remote-API route — so the BFF antiforgery middleware, which only acts on BFF-marked endpoints, does not block the Dashboard's POSTs. The `{**catch-all}` template also matches the bare `/admin/hangfire`.)

- [ ] **Step 2: Add `using` directives to `Program.cs`**

In `src/services/BFF/Program.cs`, add these to the existing `using` block:

```csharp
using System.Security.Claims;

using Microsoft.IdentityModel.JsonWebTokens;
```

- [ ] **Step 3: Register the authorization policy**

Replace the existing line:

```csharp
builder.Services.AddAuthorization();
```

with:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(HangfireDashboardProxyConfiguration.PolicyName, policy =>
        policy
            .RequireAuthenticatedUser()
            .RequireClaim(
                HangfireDashboardProxyConfiguration.PermissionClaimType,
                HangfireDashboardProxyConfiguration.GetRequiredPermission(builder.Configuration)));
});
```

- [ ] **Step 4: Add the route + cluster to the reverse proxy**

In the `AddReverseProxy().LoadFromMemory(...)` call, add the Hangfire route and cluster (note the existing `using BFF.RemoteApis;` already covers the new type):

```csharp
builder.Services
    .AddReverseProxy()
    .LoadFromMemory
    (
        [
            ChatApiProxyConfiguration.CreateRoute(),
            HangfireDashboardProxyConfiguration.CreateRoute(builder.Configuration),
            ..FrontendProxyConfiguration.CreateRoutes()
        ],
        [
            ChatApiProxyConfiguration.CreateCluster(ChatApiProxyConfiguration.GetAddress(builder.Configuration)),
            HangfireDashboardProxyConfiguration.CreateCluster(builder.Configuration),
            ..FrontendProxyConfiguration.CreateClusters(
                FrontendProxyConfiguration.GetFrontendAddress(builder.Configuration))
        ]
    )
    .AddBffExtensions();
```

- [ ] **Step 5: Enrich the principal with `permissions` at sign-in**

Auth0 RBAC `permissions` ride on the access token, not the cookie principal. In the `AddOptions<OpenIdConnectOptions>(...).Configure<IOptions<Auth0Options>>((oidc, wrapper) => { ... })` block, add an `OnTokenValidated` handler next to the existing `OnRedirectToIdentityProvider` assignment (just before the block closes):

```csharp
        oidc.Events.OnRedirectToIdentityProvider = ctx =>
        {
            ctx.ProtocolMessage.SetParameter("audience", auth0.Audience);
            return Task.CompletedTask;
        };

        oidc.Events.OnTokenValidated = ctx =>
        {
            string? accessToken = ctx.TokenEndpointResponse?.AccessToken;

            if (!string.IsNullOrEmpty(accessToken) && ctx.Principal?.Identity is ClaimsIdentity identity)
            {
                JsonWebToken token = new(accessToken);

                foreach (Claim permission in token.Claims.Where(claim => claim.Type == "permissions"))
                {
                    if (!identity.HasClaim("permissions", permission.Value))
                    {
                        identity.AddClaim(new Claim("permissions", permission.Value));
                    }
                }
            }

            return Task.CompletedTask;
        };
```

- [ ] **Step 6: Verify build**

Run: `dotnet build src/services/BFF/BFF.csproj`
Expected: `Build succeeded.`

- [ ] **Step 7: Verify the whole solution builds**

Run: `dotnet build Nova.slnx`
Expected: `Build succeeded.` across all projects.

- [ ] **Step 8: Commit**

```bash
git add src/services/BFF/RemoteApis/HangfireDashboardProxyConfiguration.cs src/services/BFF/Program.cs
git commit -m "feat(bff): proxy hangfire dashboard with permission gate" -m "Add a YARP route for /admin/hangfire behind a permissions-claim policy, inject the worker gateway secret, and copy Auth0 permissions into the cookie principal at sign-in."
```

---

## Task 8: Cleanup service unit test (OPTIONAL — requires explicit user approval per `AGENTS.md`)

> **Do NOT execute this task without the user explicitly approving test work.** The code is provided so it is ready to drop in if approved.

**Files:**
- Create: `tests/Chat/Chat.Application.Tests/Chats/FakeChatRepository.cs`
- Create: `tests/Chat/Chat.Application.Tests/Chats/TemporaryChatCleanerTests.cs`

- [ ] **Step 1: Create the fake repository**

Create `tests/Chat/Chat.Application.Tests/Chats/FakeChatRepository.cs`:

```csharp
using Chat.Domain.Chats;
using Chat.Domain.Chats.ValueObjects;
using Chat.Domain.Shared;

namespace Chat.Application.Tests.Chats;

internal sealed class FakeChatRepository : IChatRepository
{
    public DateTimeOffset? LastDeleteCutoff { get; private set; }

    public int DeleteResult { get; set; }

    public Task<ChatThread?> GetByIdAsync(ChatId id, UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ChatThread?>(null);

    public void Add(ChatThread chat)
    {
    }

    public Task<int> DeleteExpiredTemporaryChatsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        LastDeleteCutoff = olderThan;
        return Task.FromResult(DeleteResult);
    }
}
```

- [ ] **Step 2: Write the test**

Create `tests/Chat/Chat.Application.Tests/Chats/TemporaryChatCleanerTests.cs` (reuses the existing `FakeDateTimeProvider`):

```csharp
using Chat.Application.Chats.Cleanup;
using Chat.Application.Tests.FavoriteModels;

namespace Chat.Application.Tests.Chats;

public sealed class TemporaryChatCleanerTests
{
    [Fact]
    public async Task DeleteExpiredAsync_uses_now_minus_retention_and_returns_count()
    {
        DateTimeOffset now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        FakeDateTimeProvider clock = new(now);
        FakeChatRepository chats = new() { DeleteResult = 7 };

        TemporaryChatCleaner cleaner = new(chats, clock);

        int deleted = await cleaner.DeleteExpiredAsync(TimeSpan.FromDays(30), CancellationToken.None);

        Assert.Equal(7, deleted);
        Assert.Equal(now.AddDays(-30), chats.LastDeleteCutoff);
    }
}
```

- [ ] **Step 3: Run the test (verify it passes)**

Run: `dotnet test tests/Chat/Chat.Application.Tests/Chat.Application.Tests.csproj --filter "FullyQualifiedName~TemporaryChatCleanerTests"`
Expected: 1 passed.

If the compiler reports `TemporaryChatCleaner` is inaccessible, confirm `Chat.Application` exposes internals to the test assembly (it already does for the existing handler tests, e.g. `CreateChatHandlerTests`).

- [ ] **Step 4: Commit**

```bash
git add tests/Chat/Chat.Application.Tests/Chats/
git commit -m "test(chat): cover temporary chat cleanup service" -m "Assert the cutoff is now minus the retention period and the deleted count is returned."
```

---

## Task 9: Integration verification (manual)

> Runs the full Aspire app. Requires the existing local secrets (postgres/redis/rabbitmq/auth0) to already be configured, plus the new gateway secret.

- [ ] **Step 1: Set the gateway-secret parameter for local dev**

Run:

```bash
dotnet user-secrets set "Parameters:hangfire-gateway-secret" "dev-hangfire-gateway-secret" --project Nova.AppHost
```

- [ ] **Step 2: Run the AppHost**

Run: `dotnet run --project Nova.AppHost`
Expected: the Aspire dashboard lists `chat-cleanup-worker` as Running (after `chat-migrations` completes) and `bff` as Running.

- [ ] **Step 3: Confirm the recurring job registered**

In the Aspire dashboard, open the `chat-cleanup-worker` logs.
Expected: no startup exceptions; Hangfire server started. The `hangfire` schema is created in `chat-db`.

- [ ] **Step 4: Confirm the Dashboard is gated and reachable through the BFF**

- Hitting the worker endpoint directly (`http://localhost:7300/admin/hangfire`) without the gateway header → **rejected** (the filter denies it).
- Browsing to the BFF at `/admin/hangfire` while unauthenticated → redirected to Auth0 login.
- After logging in as a user **with the `jobs:read` permission** (assign it in Auth0, or temporarily change `HangfireDashboard:RequiredPermission` for a smoke test) → the Dashboard renders, showing the `temporary-chat-cleanup` recurring job under **Recurring Jobs**.
- Logged in **without** the permission → `403`.

- [ ] **Step 5: Smoke-test the deletion (optional)**

Trigger the recurring job from the Dashboard ("Trigger now"), or temporarily set `TemporaryChatCleanup:Cron` to `* * * * *` (every minute). With a temporary chat whose `updated_at` is older than the retention window present in `chat-db`, confirm the chat row and its `chat_messages` are gone after the job runs, and the worker logs `Temporary chat cleanup deleted N chats`.

- [ ] **Step 6: Stop the app**

Stop the AppHost (Ctrl+C). No commit (verification only).

---

## Self-Review

**Spec coverage:**
- Add Hangfire → Task 1. PostgreSQL storage → Task 5 (`UsePostgreSqlStorage`, `hangfire` schema). Server/worker configured → Task 5 (`AddHangfireServer`). Recurring cleanup job → Tasks 2–5. Configurable interval/retention → Task 5 (`TemporaryChatCleanupOptions`, validated). Batched deletion → Task 2. Dashboard (jobs/recurring/failed/history) → Task 5 (`MapHangfireDashboard`). Secured Dashboard → Tasks 5 (gateway filter) + 6 (secret wiring) + 7 (BFF permission policy). Clean architecture/microservices → cleanup service in Application, worker isolated, Infrastructure stays Hangfire-free. All spec sections map to tasks.

**Placeholder scan:** No TBD/TODO; every code step has complete code; version/API risks have concrete fallback instructions rather than placeholders.

**Type consistency:** `DeleteExpiredTemporaryChatsAsync(DateTimeOffset, CancellationToken)` is identical across interface (T2), impl (T2), and fake (T8). `ITemporaryChatCleaner.DeleteExpiredAsync(TimeSpan, CancellationToken)` matches its impl `TemporaryChatCleaner` (T3), the DI registration (T4), the job's call (T5), and the test (T8). Header name `X-Hangfire-Gateway` matches between worker filter (T5) and BFF transform (T7). `PolicyName = "HangfireDashboard"` is used by both the policy registration and the route (T7). Config keys (`HangfireDashboard:Address|GatewaySecret|RequiredPermission`, `TemporaryChatCleanup:RetentionPeriod|Cron`) are consistent across worker, BFF, and AppHost env (`HangfireDashboard__*`). Endpoint port `7300` matches between AppHost (T6) and the BFF default address (T7).
