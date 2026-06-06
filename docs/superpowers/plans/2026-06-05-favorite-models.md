# Favorite Models Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let authenticated users idempotently add, remove, and query favorite LLM models while retaining disabled favorites in query results.

**Architecture:** A minimal `FavoriteModel` aggregate stores the user/model association and creation time. Mediator handlers coordinate authenticated identity and model availability, while infrastructure uses EF Core for schema mapping, PostgreSQL conflict handling for concurrent writes, and Dapper for the joined favorite-model projection. FastEndpoints exposes desired-state `PUT`/`DELETE` operations and a flat `GET` result.

**Tech Stack:** .NET 10, `Mediator.SourceGenerator` / `Mediator.Abstractions`, FastEndpoints, EF Core, Npgsql, Dapper, ErrorOr, PostgreSQL.

---

## Constraints

- Do not add or modify tests in this pass.
- Do not commit changes in this pass.
- Do not replace the existing `Mediator` package family with MediatR.
- Keep `LlmModel` as an entity owned by the `LlmProvider` aggregate.
- Ask for elevated permission before running any `dotnet` command.
- Implement this after, or on top of, the approved model-catalog ordering changes.

## File Structure

- Create: `src/services/Chat/Chat.Domain/Users/ValueObjects/UserId.cs`
  - Reusable validated authenticated-user identity.
- Create: `src/services/Chat/Chat.Domain/FavoriteModels/ValueObjects/FavoriteModelId.cs`
  - UUID v7 aggregate identifier.
- Create: `src/services/Chat/Chat.Domain/FavoriteModels/FavoriteModel.cs`
  - Minimal favorite association aggregate root.
- Create: `src/services/Chat/Chat.Domain/FavoriteModels/IFavoriteModelRepository.cs`
  - Aggregate loading, conflict-safe insertion, and tracked removal operations.
- Create: `src/services/Chat/Chat.Application/Abstractions/ModelCatalog/ILlmModelAvailabilityReader.cs`
  - Lightweight model existence and enabled-state lookup.
- Create: `src/services/Chat/Chat.Application/Abstractions/FavoriteModels/IFavoriteModelsReader.cs`
  - Favorite projection reader contract.
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Errors/FavoriteModelOperationErrors.cs`
  - Missing and disabled model errors.
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Commands/AddFavoriteModel/*`
  - Idempotent add command and handler.
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Commands/RemoveFavoriteModel/*`
  - Idempotent remove command and handler.
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Queries/GetFavoriteModels/*`
  - Flat favorite-model read models, query, and handler.
- Create: `src/services/Chat/Chat.Infrastructure/FavoriteModels/Configurations/FavoriteModelConfiguration.cs`
  - EF mapping, unique constraint, and model foreign key.
- Create: `src/services/Chat/Chat.Infrastructure/FavoriteModels/Repositories/FavoriteModelRepository.cs`
  - PostgreSQL-backed idempotent aggregate persistence.
- Create: `src/services/Chat/Chat.Infrastructure/FavoriteModels/Readers/LlmModelAvailabilityDapperReader.cs`
  - Lightweight catalog availability lookup.
- Create: `src/services/Chat/Chat.Infrastructure/FavoriteModels/Readers/FavoriteModelsDapperReader.cs`
  - Joined favorite/model/provider projection.
- Modify: `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`
  - Register the favorite aggregate with EF Core.
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`
  - Register repository and readers.
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/AddFavoriteModel/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/RemoveFavoriteModel/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/GetFavoriteModels/*`
- Modify: `src/services/Chat/Chat.Api/Endpoints/CustomTags.cs`
  - Add the Favorite Models OpenAPI tag.
- Create: `src/services/Chat/Chat.Infrastructure/Database/Migrations/<timestamp>_FavoriteModels.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

---

### Task 1: Add Domain Identity Types and Aggregate

**Files:**
- Create: `src/services/Chat/Chat.Domain/Users/ValueObjects/UserId.cs`
- Create: `src/services/Chat/Chat.Domain/FavoriteModels/ValueObjects/FavoriteModelId.cs`
- Create: `src/services/Chat/Chat.Domain/FavoriteModels/FavoriteModel.cs`
- Create: `src/services/Chat/Chat.Domain/FavoriteModels/IFavoriteModelRepository.cs`

- [ ] **Step 1: Create the reusable user ID value object**

Create `src/services/Chat/Chat.Domain/Users/ValueObjects/UserId.cs`:

```csharp
using ErrorOr;

namespace Chat.Domain.Users.ValueObjects;

public sealed record UserId
{
    public const int MaxLength = 256;

    public string Value { get; }

    private UserId(string value)
    {
        Value = value;
    }

    public static ErrorOr<UserId> Create(string? value)
    {
        string? trimmed = value?.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Error.Validation
            (
                code: "UserId.Required",
                description: "User id is required."
            );
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation
            (
                code: "UserId.TooLong",
                description: $"User id cannot exceed {MaxLength} characters."
            );
        }

        return new UserId(trimmed);
    }

    public static UserId FromDatabase(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > MaxLength)
            throw new DomainException("Database contained an invalid user id.");

        return new UserId(value);
    }

    public override string ToString() => Value;
}
```

- [ ] **Step 2: Create the aggregate identifier**

Create `src/services/Chat/Chat.Domain/FavoriteModels/ValueObjects/FavoriteModelId.cs`:

```csharp
using ErrorOr;

namespace Chat.Domain.FavoriteModels.ValueObjects;

public sealed record FavoriteModelId
{
    public Guid Value { get; }

    private FavoriteModelId(Guid value)
    {
        Value = value;
    }

    public static FavoriteModelId New() => new(Guid.CreateVersion7());

    public static ErrorOr<FavoriteModelId> Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            return Error.Validation
            (
                code: "FavoriteModelId.Empty",
                description: "Favorite model id cannot be empty."
            );
        }

        return new FavoriteModelId(value);
    }

    public static FavoriteModelId FromDatabase(Guid value)
    {
        if (value == Guid.Empty)
            throw new DomainException("Database contained an empty favorite model id.");

        return new FavoriteModelId(value);
    }

    public override string ToString() => Value.ToString();
}
```

- [ ] **Step 3: Create the aggregate root**

Create `src/services/Chat/Chat.Domain/FavoriteModels/FavoriteModel.cs`:

```csharp
using Chat.Domain.FavoriteModels.ValueObjects;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Users.ValueObjects;

using SharedKernel;

namespace Chat.Domain.FavoriteModels;

public sealed class FavoriteModel : AggregateRoot<FavoriteModelId>
{
    public UserId UserId { get; private set; }

    public LlmModelId LlmModelId { get; private set; }

    public DateTimeOffset FavoritedAt { get; private set; }

    private FavoriteModel
    (
        FavoriteModelId id,
        UserId userId,
        LlmModelId llmModelId,
        DateTimeOffset favoritedAt
    ) : base(id)
    {
        UserId = userId;
        LlmModelId = llmModelId;
        FavoritedAt = favoritedAt;
    }

    public static FavoriteModel Create
    (
        UserId userId,
        LlmModelId llmModelId,
        DateTimeOffset favoritedAt
    ) => new
    (
        id: FavoriteModelId.New(),
        userId: userId,
        llmModelId: llmModelId,
        favoritedAt: favoritedAt
    );
}
```

- [ ] **Step 4: Create the repository contract**

Create `src/services/Chat/Chat.Domain/FavoriteModels/IFavoriteModelRepository.cs`:

```csharp
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Users.ValueObjects;

namespace Chat.Domain.FavoriteModels;

public interface IFavoriteModelRepository
{
    Task<FavoriteModel?> GetAsync
    (
        UserId userId,
        LlmModelId llmModelId,
        CancellationToken cancellationToken = default
    );

    Task AddIfMissingAsync
    (
        FavoriteModel favoriteModel,
        CancellationToken cancellationToken = default
    );

    void Remove(FavoriteModel favoriteModel);
}
```

- [ ] **Step 5: Inspect the domain changes**

Run:

```bash
git diff -- src/services/Chat/Chat.Domain
```

Expected: one reusable `UserId`, one UUID v7 favorite ID, a four-property aggregate, and a repository that loads and removes `FavoriteModel` without depending on `LlmModel` itself.

---

### Task 2: Add Application Contracts and Errors

**Files:**
- Create: `src/services/Chat/Chat.Application/Abstractions/ModelCatalog/ILlmModelAvailabilityReader.cs`
- Create: `src/services/Chat/Chat.Application/Abstractions/FavoriteModels/IFavoriteModelsReader.cs`
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Errors/FavoriteModelOperationErrors.cs`
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Queries/GetFavoriteModels/FavoriteModelProviderReadModel.cs`
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Queries/GetFavoriteModels/FavoriteLlmModelReadModel.cs`
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Queries/GetFavoriteModels/FavoriteModelsReadModel.cs`

- [ ] **Step 1: Define model availability states and reader**

Create `src/services/Chat/Chat.Application/Abstractions/ModelCatalog/ILlmModelAvailabilityReader.cs`:

```csharp
using Chat.Domain.ModelCatalog.ValueObjects;

namespace Chat.Application.Abstractions.ModelCatalog;

public enum LlmModelAvailability
{
    NotFound,
    Disabled,
    Enabled
}

public interface ILlmModelAvailabilityReader
{
    Task<LlmModelAvailability> GetAsync
    (
        LlmModelId llmModelId,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 2: Define the favorite query read models**

Create `FavoriteModelProviderReadModel.cs`:

```csharp
namespace Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

public sealed record FavoriteModelProviderReadModel
(
    Guid Id,
    string Name,
    string Slug,
    string? LogoKey
);
```

Create `FavoriteLlmModelReadModel.cs`:

```csharp
namespace Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

public sealed record FavoriteLlmModelReadModel
(
    Guid Id,
    string ExternalModelId,
    string Name,
    string Description,
    int ContextWindow,
    bool SupportsVision,
    bool SupportsReasoning,
    bool SupportsToolCalling,
    bool IsEnabled,
    DateTimeOffset FavoritedAt,
    FavoriteModelProviderReadModel Provider
);
```

Create `FavoriteModelsReadModel.cs`:

```csharp
namespace Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

public sealed record FavoriteModelsReadModel
(
    IReadOnlyList<FavoriteLlmModelReadModel> Models
);
```

- [ ] **Step 3: Define the favorite projection reader**

Create `src/services/Chat/Chat.Application/Abstractions/FavoriteModels/IFavoriteModelsReader.cs`:

```csharp
using Chat.Application.FavoriteModels.Queries.GetFavoriteModels;
using Chat.Domain.Users.ValueObjects;

namespace Chat.Application.Abstractions.FavoriteModels;

public interface IFavoriteModelsReader
{
    Task<FavoriteModelsReadModel> GetAsync
    (
        UserId userId,
        CancellationToken cancellationToken
    );
}
```

- [ ] **Step 4: Define operation errors**

Create `src/services/Chat/Chat.Application/FavoriteModels/Errors/FavoriteModelOperationErrors.cs`:

```csharp
using Chat.Domain.ModelCatalog.ValueObjects;

using ErrorOr;

namespace Chat.Application.FavoriteModels.Errors;

public static class FavoriteModelOperationErrors
{
    public static Error ModelNotFound(LlmModelId llmModelId) =>
        Error.NotFound
        (
            code: "FavoriteModel.ModelNotFound",
            description: $"No LLM model found with ID '{llmModelId.Value}'."
        );

    public static Error ModelDisabled(LlmModelId llmModelId) =>
        Error.Conflict
        (
            code: "FavoriteModel.ModelDisabled",
            description: $"LLM model '{llmModelId.Value}' is disabled and cannot be favorited."
        );
}
```

- [ ] **Step 5: Inspect the contracts**

Run:

```bash
git diff -- src/services/Chat/Chat.Application/Abstractions src/services/Chat/Chat.Application/FavoriteModels
```

Expected: availability distinguishes missing, disabled, and enabled models; the query contract exposes the LLM model ID rather than the favorite aggregate ID.

---

### Task 3: Add Mediator Commands and Query

**Files:**
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Commands/AddFavoriteModel/AddFavoriteModelCommand.cs`
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Commands/AddFavoriteModel/AddFavoriteModelHandler.cs`
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Commands/RemoveFavoriteModel/RemoveFavoriteModelCommand.cs`
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Commands/RemoveFavoriteModel/RemoveFavoriteModelHandler.cs`
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Queries/GetFavoriteModels/GetFavoriteModelsQuery.cs`
- Create: `src/services/Chat/Chat.Application/FavoriteModels/Queries/GetFavoriteModels/GetFavoriteModelsHandler.cs`

- [ ] **Step 1: Add the idempotent add command**

Create `AddFavoriteModelCommand.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.FavoriteModels.Commands.AddFavoriteModel;

public sealed record AddFavoriteModelCommand(Guid LlmModelId)
    : ICommand<ErrorOr<Success>>;
```

Create `AddFavoriteModelHandler.cs`:

```csharp
using Chat.Application.Abstractions.ModelCatalog;
using Chat.Application.FavoriteModels.Errors;
using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Users.ValueObjects;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

using SharedKernel;

namespace Chat.Application.FavoriteModels.Commands.AddFavoriteModel;

internal sealed class AddFavoriteModelHandler
(
    IUserContext userContext,
    IFavoriteModelRepository favoriteModels,
    ILlmModelAvailabilityReader modelAvailability,
    IDateTimeProvider dateTimeProvider
) : ICommandHandler<AddFavoriteModelCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle
    (
        AddFavoriteModelCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (modelIdResult.IsError)
        {
            errors.AddRange(modelIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        UserId userId = userIdResult.Value;
        LlmModelId llmModelId = modelIdResult.Value;

        FavoriteModel? existingFavorite = await favoriteModels.GetAsync
        (
            userId,
            llmModelId,
            cancellationToken
        );

        if (existingFavorite is not null)
        {
            return Result.Success;
        }

        LlmModelAvailability availability = await modelAvailability.GetAsync(llmModelId, cancellationToken);

        if (availability == LlmModelAvailability.NotFound)
        {
            return FavoriteModelOperationErrors.ModelNotFound(llmModelId);
        }

        if (availability == LlmModelAvailability.Disabled)
        {
            return FavoriteModelOperationErrors.ModelDisabled(llmModelId);
        }

        FavoriteModel favoriteModel = FavoriteModel.Create
        (
            userId: userId,
            llmModelId: llmModelId,
            favoritedAt: dateTimeProvider.UtcNow
        );

        await favoriteModels.AddIfMissingAsync(favoriteModel, cancellationToken);

        return Result.Success;
    }
}
```

- [ ] **Step 2: Add the idempotent remove command**

Create `RemoveFavoriteModelCommand.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.FavoriteModels.Commands.RemoveFavoriteModel;

public sealed record RemoveFavoriteModelCommand(Guid LlmModelId)
    : ICommand<ErrorOr<Success>>;
```

Create `RemoveFavoriteModelHandler.cs`:

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Users.ValueObjects;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.FavoriteModels.Commands.RemoveFavoriteModel;

internal sealed class RemoveFavoriteModelHandler
(
    IUserContext userContext,
    IFavoriteModelRepository favoriteModels,
    IUnitOfWork unitOfWork
) : ICommandHandler<RemoveFavoriteModelCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle
    (
        RemoveFavoriteModelCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);
        ErrorOr<LlmModelId> modelIdResult = LlmModelId.Create(command.LlmModelId);

        List<Error> errors = [];

        if (userIdResult.IsError)
        {
            errors.AddRange(userIdResult.Errors);
        }

        if (modelIdResult.IsError)
        {
            errors.AddRange(modelIdResult.Errors);
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        FavoriteModel? favoriteModel = await favoriteModels.GetAsync
        (
            userIdResult.Value,
            modelIdResult.Value,
            cancellationToken
        );

        if (favoriteModel is null)
        {
            return Result.Success;
        }

        favoriteModels.Remove(favoriteModel);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
```

- [ ] **Step 3: Add the authenticated favorite query**

Create `GetFavoriteModelsQuery.cs`:

```csharp
using ErrorOr;

using Mediator;

namespace Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

public sealed record GetFavoriteModelsQuery
    : IQuery<ErrorOr<FavoriteModelsReadModel>>;
```

Create `GetFavoriteModelsHandler.cs`:

```csharp
using Chat.Application.Abstractions.FavoriteModels;
using Chat.Domain.Users.ValueObjects;

using ErrorOr;

using Mediator;

using Shared.Application.Authentication;

namespace Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

internal sealed class GetFavoriteModelsHandler
(
    IUserContext userContext,
    IFavoriteModelsReader reader
) : IQueryHandler<GetFavoriteModelsQuery, ErrorOr<FavoriteModelsReadModel>>
{
    public async ValueTask<ErrorOr<FavoriteModelsReadModel>> Handle
    (
        GetFavoriteModelsQuery query,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<UserId> userIdResult = UserId.Create(userContext.UserId);

        if (userIdResult.IsError)
        {
            return userIdResult.Errors;
        }

        return await reader.GetAsync(userIdResult.Value, cancellationToken);
    }
}
```

- [ ] **Step 4: Inspect the application flow**

Run:

```bash
git diff -- src/services/Chat/Chat.Application/FavoriteModels
```

Expected: existing favorites short-circuit before availability validation, disabled new favorites return conflict, and removal loads and removes the aggregate through the repository.

---

### Task 4: Add EF Mapping and Idempotent Repository

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/FavoriteModels/Configurations/FavoriteModelConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/FavoriteModels/Repositories/FavoriteModelRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs`

- [ ] **Step 1: Map the aggregate and relational constraints**

Create `FavoriteModelConfiguration.cs`:

```csharp
using Chat.Domain.FavoriteModels;
using Chat.Domain.FavoriteModels.ValueObjects;
using Chat.Domain.ModelCatalog.Entities;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Users.ValueObjects;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Chat.Infrastructure.FavoriteModels.Configurations;

internal sealed class FavoriteModelConfiguration : IEntityTypeConfiguration<FavoriteModel>
{
    public void Configure(EntityTypeBuilder<FavoriteModel> builder)
    {
        builder.ToTable("favorite_models");

        builder.HasKey(favorite => favorite.Id);

        builder.Property(favorite => favorite.Id)
            .HasConversion
            (
                id => id.Value,
                value => FavoriteModelId.FromDatabase(value)
            )
            .ValueGeneratedNever();

        builder.Property(favorite => favorite.UserId)
            .HasConversion
            (
                id => id.Value,
                value => UserId.FromDatabase(value)
            )
            .HasMaxLength(UserId.MaxLength)
            .IsRequired();

        builder.Property(favorite => favorite.LlmModelId)
            .HasConversion
            (
                id => id.Value,
                value => LlmModelId.FromDatabase(value)
            )
            .IsRequired();

        builder.Property(favorite => favorite.FavoritedAt)
            .IsRequired();

        builder.HasIndex(favorite => new { favorite.UserId, favorite.LlmModelId })
            .IsUnique();

        builder.HasOne<LlmModel>()
            .WithMany()
            .HasForeignKey(favorite => favorite.LlmModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(favorite => favorite.DomainEvents);
    }
}
```

The navigationless foreign key is an infrastructure constraint only. It does not make `LlmModel` independently mutable or move it out of the `LlmProvider` aggregate.

- [ ] **Step 2: Register the aggregate in the DbContext**

In `ChatDbContext.cs`, add:

```csharp
using Chat.Domain.FavoriteModels;
```

Then add below `LlmProviders`:

```csharp
internal DbSet<FavoriteModel> FavoriteModels => Set<FavoriteModel>();
```

- [ ] **Step 3: Implement conflict-safe repository operations**

Create `FavoriteModelRepository.cs`:

```csharp
using Chat.Domain.FavoriteModels;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Users.ValueObjects;
using Chat.Infrastructure.Database;

using Microsoft.EntityFrameworkCore;

namespace Chat.Infrastructure.FavoriteModels.Repositories;

internal sealed class FavoriteModelRepository(ChatDbContext db) : IFavoriteModelRepository
{
    public async Task<FavoriteModel?> GetAsync
    (
        UserId userId,
        LlmModelId llmModelId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.FavoriteModels.SingleOrDefaultAsync
        (
            favorite => favorite.UserId == userId && favorite.LlmModelId == llmModelId,
            cancellationToken
        );
    }

    public async Task AddIfMissingAsync
    (
        FavoriteModel favoriteModel,
        CancellationToken cancellationToken = default
    )
    {
        await db.Database.ExecuteSqlInterpolatedAsync
        (
            $"""
             insert into favorite_models (id, user_id, llm_model_id, favorited_at)
             values
             (
                 {favoriteModel.Id.Value},
                 {favoriteModel.UserId.Value},
                 {favoriteModel.LlmModelId.Value},
                 {favoriteModel.FavoritedAt}
             )
             on conflict (user_id, llm_model_id) do nothing;
             """,
            cancellationToken
        );
    }

    public void Remove(FavoriteModel favoriteModel)
    {
        db.FavoriteModels.Remove(favoriteModel);
    }
}
```

`ON CONFLICT DO NOTHING` is the final concurrency guard. The application does not need to reference Npgsql exception types, and equivalent concurrent `PUT` requests all finish successfully.

- [ ] **Step 4: Inspect persistence changes**

Run:

```bash
git diff -- src/services/Chat/Chat.Infrastructure/FavoriteModels src/services/Chat/Chat.Infrastructure/Database/ChatDbContext.cs
```

Expected: EF owns schema metadata and foreign keys; normal operations load and track the aggregate, while insertion retains atomic conflict handling for concurrent duplicate requests.

---

### Task 5: Add Availability and Favorite Projection Readers

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/FavoriteModels/Readers/LlmModelAvailabilityDapperReader.cs`
- Create: `src/services/Chat/Chat.Infrastructure/FavoriteModels/Readers/FavoriteModelsDapperReader.cs`

- [ ] **Step 1: Implement the lightweight availability reader**

Create `LlmModelAvailabilityDapperReader.cs`:

```csharp
using Chat.Application.Abstractions.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.FavoriteModels.Readers;

internal sealed class LlmModelAvailabilityDapperReader(NpgsqlDataSource dataSource)
    : ILlmModelAvailabilityReader
{
    private const string Sql = """
                               select is_enabled
                               from llm_models
                               where id = @LlmModelId;
                               """;

    public async Task<LlmModelAvailability> GetAsync
    (
        LlmModelId llmModelId,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            Sql,
            new { LlmModelId = llmModelId.Value },
            cancellationToken: cancellationToken
        );

        bool? isEnabled = await connection.QuerySingleOrDefaultAsync<bool?>(command);

        return isEnabled switch
        {
            true => LlmModelAvailability.Enabled,
            false => LlmModelAvailability.Disabled,
            null => LlmModelAvailability.NotFound
        };
    }
}
```

- [ ] **Step 2: Implement the joined favorite query**

Create `FavoriteModelsDapperReader.cs`:

```csharp
using Chat.Application.Abstractions.FavoriteModels;
using Chat.Application.FavoriteModels.Queries.GetFavoriteModels;
using Chat.Domain.Users.ValueObjects;

using Dapper;

using Npgsql;

namespace Chat.Infrastructure.FavoriteModels.Readers;

internal sealed class FavoriteModelsDapperReader(NpgsqlDataSource dataSource)
    : IFavoriteModelsReader
{
    private const string Sql = """
                               select
                                   m.id as "Id",
                                   m.external_model_id as "ExternalModelId",
                                   m.name as "Name",
                                   m.description as "Description",
                                   m.context_window as "ContextWindow",
                                   m.supports_vision as "SupportsVision",
                                   m.supports_reasoning as "SupportsReasoning",
                                   m.supports_tool_calling as "SupportsToolCalling",
                                   m.is_enabled as "IsEnabled",
                                   f.favorited_at as "FavoritedAt",
                                   p.id as "ProviderId",
                                   p.name as "ProviderName",
                                   p.slug as "ProviderSlug",
                                   p.logo_key as "ProviderLogoKey"
                               from favorite_models f
                               inner join llm_models m on m.id = f.llm_model_id
                               inner join llm_providers p on p.id = m.provider_id
                               where f.user_id = @UserId
                               order by m.name, m.id;
                               """;

    public async Task<FavoriteModelsReadModel> GetAsync
    (
        UserId userId,
        CancellationToken cancellationToken
    )
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);

        CommandDefinition command = new
        (
            Sql,
            new { UserId = userId.Value },
            cancellationToken: cancellationToken
        );

        IReadOnlyList<FavoriteRow> rows = (await connection.QueryAsync<FavoriteRow>(command)).AsList();

        FavoriteLlmModelReadModel[] models = rows
            .Select(row => new FavoriteLlmModelReadModel
            (
                Id: row.Id,
                ExternalModelId: row.ExternalModelId,
                Name: row.Name,
                Description: row.Description,
                ContextWindow: row.ContextWindow,
                SupportsVision: row.SupportsVision,
                SupportsReasoning: row.SupportsReasoning,
                SupportsToolCalling: row.SupportsToolCalling,
                IsEnabled: row.IsEnabled,
                FavoritedAt: row.FavoritedAt,
                Provider: new FavoriteModelProviderReadModel
                (
                    Id: row.ProviderId,
                    Name: row.ProviderName,
                    Slug: row.ProviderSlug,
                    LogoKey: row.ProviderLogoKey
                )
            ))
            .ToArray();

        return new FavoriteModelsReadModel(models);
    }

    private sealed record FavoriteRow
    (
        Guid Id,
        string ExternalModelId,
        string Name,
        string Description,
        int ContextWindow,
        bool SupportsVision,
        bool SupportsReasoning,
        bool SupportsToolCalling,
        bool IsEnabled,
        DateTimeOffset FavoritedAt,
        Guid ProviderId,
        string ProviderName,
        string ProviderSlug,
        string? ProviderLogoKey
    );
}
```

The query intentionally has no `m.is_enabled` filter. Disabled favorites remain visible, and provider `is_featured` is neither selected nor used for ordering.

- [ ] **Step 3: Inspect SQL behavior**

Run:

```bash
git diff -- src/services/Chat/Chat.Infrastructure/FavoriteModels/Readers
```

Expected: availability is a scalar lookup; favorites are globally ordered by model name and ID and include nested provider data.

---

### Task 6: Register Infrastructure Services

**Files:**
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add favorite namespaces**

Add these imports:

```csharp
using Chat.Application.Abstractions.FavoriteModels;
using Chat.Domain.FavoriteModels;
using Chat.Infrastructure.FavoriteModels.Readers;
using Chat.Infrastructure.FavoriteModels.Repositories;
```

- [ ] **Step 2: Register the repository**

In `AddDatabaseServices`, add:

```csharp
services.AddScoped<IFavoriteModelRepository, FavoriteModelRepository>();
```

- [ ] **Step 3: Register both readers**

In `AddReaders`, add:

```csharp
services.AddScoped<ILlmModelAvailabilityReader, LlmModelAvailabilityDapperReader>();
services.AddScoped<IFavoriteModelsReader, FavoriteModelsDapperReader>();
```

- [ ] **Step 4: Inspect registrations**

Run:

```bash
git diff -- src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
```

Expected: all new handler dependencies resolve through scoped infrastructure registrations.

---

### Task 7: Add FastEndpoints Write Operations

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/AddFavoriteModel/Endpoint.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/RemoveFavoriteModel/Endpoint.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/CustomTags.cs`

- [ ] **Step 1: Add the OpenAPI tag**

Add to `CustomTags`:

```csharp
public const string FavoriteModels = "Favorite Models";
```

- [ ] **Step 2: Add the desired-state PUT endpoint**

Create `AddFavoriteModel/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints;
using Chat.Application.FavoriteModels.Commands.AddFavoriteModel;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.FavoriteModels.AddFavoriteModel;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.FavoriteModels.Add";

    public override void Configure()
    {
        Put("/me/favorite-models/{modelId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Add Favorite Model")
                .WithDescription("Adds an enabled LLM model to the authenticated user's favorites.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .WithTags(CustomTags.FavoriteModels);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        AddFavoriteModelCommand command = new(Route<Guid>("modelId"));

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
```

- [ ] **Step 3: Add the desired-state DELETE endpoint**

Create `RemoveFavoriteModel/Endpoint.cs`:

```csharp
using Chat.Api.Endpoints;
using Chat.Application.FavoriteModels.Commands.RemoveFavoriteModel;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.FavoriteModels.RemoveFavoriteModel;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest
{
    public const string RouteName = "Chat.FavoriteModels.Remove";

    public override void Configure()
    {
        Delete("/me/favorite-models/{modelId}");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Remove Favorite Model")
                .WithDescription("Removes an LLM model from the authenticated user's favorites.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.FavoriteModels);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        RemoveFavoriteModelCommand command = new(Route<Guid>("modelId"));

        ErrorOr<Success> result = await sender.Send(command, ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.NoContentAsync(ct);
    }
}
```

- [ ] **Step 4: Inspect the write endpoints**

Run:

```bash
git diff -- src/services/Chat/Chat.Api/Endpoints/FavoriteModels src/services/Chat/Chat.Api/Endpoints/CustomTags.cs
```

Expected: no toggle route exists; repeated `PUT` and `DELETE` both return `204 No Content`.

---

### Task 8: Add the Favorite Query Endpoint

**Files:**
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/GetFavoriteModels/Response.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/GetFavoriteModels/FavoriteLlmModelResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/GetFavoriteModels/ProviderResponse.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/GetFavoriteModels/ResponseMapper.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/FavoriteModels/GetFavoriteModels/Endpoint.cs`

- [ ] **Step 1: Create API response contracts**

Create `Response.cs`:

```csharp
namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal sealed class Response
{
    public required List<FavoriteLlmModelResponse> Models { get; init; }
}
```

Create `FavoriteLlmModelResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal sealed class FavoriteLlmModelResponse
{
    public required Guid Id { get; init; }

    public required string ExternalModelId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required int ContextWindow { get; init; }

    public required bool SupportsVision { get; init; }

    public required bool SupportsReasoning { get; init; }

    public required bool SupportsToolCalling { get; init; }

    public required bool IsEnabled { get; init; }

    public required DateTimeOffset FavoritedAt { get; init; }

    public required ProviderResponse Provider { get; init; }
}
```

Create `ProviderResponse.cs`:

```csharp
namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal sealed class ProviderResponse
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required string Slug { get; init; }

    public string? LogoKey { get; init; }
}
```

- [ ] **Step 2: Map application read models to API contracts**

Create `ResponseMapper.cs`:

```csharp
using Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal static class ResponseMapper
{
    public static Response ToResponse(FavoriteModelsReadModel readModel) => new()
    {
        Models = readModel.Models
            .Select(ToResponse)
            .ToList()
    };

    private static FavoriteLlmModelResponse ToResponse(FavoriteLlmModelReadModel model) => new()
    {
        Id = model.Id,
        ExternalModelId = model.ExternalModelId,
        Name = model.Name,
        Description = model.Description,
        ContextWindow = model.ContextWindow,
        SupportsVision = model.SupportsVision,
        SupportsReasoning = model.SupportsReasoning,
        SupportsToolCalling = model.SupportsToolCalling,
        IsEnabled = model.IsEnabled,
        FavoritedAt = model.FavoritedAt,
        Provider = new ProviderResponse
        {
            Id = model.Provider.Id,
            Name = model.Provider.Name,
            Slug = model.Provider.Slug,
            LogoKey = model.Provider.LogoKey
        }
    };
}
```

- [ ] **Step 3: Add the authenticated GET endpoint**

Create `Endpoint.cs`:

```csharp
using Chat.Api.Endpoints;
using Chat.Application.FavoriteModels.Queries.GetFavoriteModels;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Infrastructure;

namespace Chat.Api.Endpoints.FavoriteModels.GetFavoriteModels;

internal sealed class Endpoint(ISender sender) : EndpointWithoutRequest<Response>
{
    public const string RouteName = "Chat.FavoriteModels.Get";

    public override void Configure()
    {
        Get("/me/favorite-models");
        Version(1);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Get Favorite Models")
                .WithDescription("Gets the authenticated user's favorite LLM models.")
                .Produces<Response>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status401Unauthorized, "application/json")
                .WithTags(CustomTags.FavoriteModels);
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        ErrorOr<FavoriteModelsReadModel> result = await sender.Send(new GetFavoriteModelsQuery(), ct);

        if (result.IsError)
        {
            await Send.ResultAsync(CustomResults.Problem(result));
            return;
        }

        await Send.ResponseAsync(ResponseMapper.ToResponse(result.Value), cancellation: ct);
    }
}
```

- [ ] **Step 4: Inspect the query endpoint**

Run:

```bash
git diff -- src/services/Chat/Chat.Api/Endpoints/FavoriteModels/GetFavoriteModels
```

Expected: the endpoint returns a flat `Models` collection with model IDs, enabled state, favorite timestamp, and nested provider metadata.

---

### Task 9: Generate the Database Migration

**Files:**
- Create: `src/services/Chat/Chat.Infrastructure/Database/Migrations/<timestamp>_FavoriteModels.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Database/Migrations/<timestamp>_FavoriteModels.Designer.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

- [ ] **Step 1: Request elevated permission and generate the migration**

Run only after receiving elevated permission:

```bash
dotnet ef migrations add FavoriteModels --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj --context ChatDbContext --output-dir Database/Migrations
```

Expected: EF creates the migration, designer file, and updates the snapshot.

- [ ] **Step 2: Inspect the generated migration**

Run:

```bash
git diff -- src/services/Chat/Chat.Infrastructure/Database/Migrations
```

Expected migration details:

- Creates `favorite_models` with `id`, `user_id`, `llm_model_id`, and `favorited_at`.
- Primary key on `id`.
- Unique index on `(user_id, llm_model_id)`.
- Foreign key to `llm_models.id` with cascade delete.
- No foreign key to `users`.

The composite unique index begins with `user_id`, so it also supports the favorite-list lookup without a redundant user-only index.

---

### Task 10: Verify the Complete Feature

**Files:**
- Review all files changed by Tasks 1-9.

- [ ] **Step 1: Check formatting and repository diff**

Run:

```bash
git diff --check
git status --short
```

Expected: no whitespace errors; only favorite-model feature files, migration output, the approved design, and this plan are present unless the user has unrelated existing changes.

- [ ] **Step 2: Request elevated permission and build**

Run only after receiving elevated permission:

```bash
dotnet build
```

Expected: build succeeds with zero errors.

- [ ] **Step 3: Verify the migration model**

Run only after receiving elevated permission:

```bash
dotnet ef migrations list --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj --context ChatDbContext
```

Expected: `FavoriteModels` appears as the latest migration.

- [ ] **Step 4: Perform final behavior review**

Confirm from the implementation:

- Repeated `PUT /v1/me/favorite-models/{modelId}` is successful.
- A missing model returns `404`.
- A disabled model that is not already favorited returns `409`.
- Repeated `DELETE` is successful and removes a loaded aggregate when one exists.
- Disabled existing favorites remain in `GET` results with `IsEnabled = false`.
- Results are ordered globally by model name and model ID.
- Provider `IsFeatured` has no effect on favorite ordering.
- `LlmModel` remains owned and mutated through `LlmProvider`.
- No user-projection foreign key or domain events were introduced.
