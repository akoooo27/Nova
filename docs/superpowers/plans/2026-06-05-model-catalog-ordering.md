# Model Catalog Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace manually managed provider and model sort orders with featured-first provider ordering and alphabetical provider/model ordering, while keeping capability filtering in frontend clients.

**Architecture:** `LlmProvider` owns a boolean `IsFeatured` product-curation flag and updates its complete editable profile atomically through one aggregate method. A full `PUT` admin operation updates name, slug, nullable logo key, and featured status, then emits one provider-updated domain event so the cached public catalog is invalidated. `LlmModel` has no ordering state, and the PostgreSQL public catalog reader returns the complete enabled catalog ordered by featured provider, provider name, and model name.

**Tech Stack:** .NET 10, C#, EF Core with Npgsql, Dapper, FastEndpoints, `Mediator.SourceGenerator` / `Mediator.Abstractions`, FusionCache, PostgreSQL

**Testing constraint:** Do not create or modify test files in this implementation. Existing tests reference `SortOrder` and will require a separate follow-up update. Verify production projects directly rather than building the complete solution.

---

## File Structure

**Delete**

- `src/services/Chat/Chat.Domain/ModelCatalog/ValueObjects/SortOrder.cs` - obsolete numeric ordering value object.

**Create**

- `src/services/Chat/Chat.Domain/ModelCatalog/Events/LlmProviderUpdated.cs` - domain event raised when the editable provider profile changes.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/UpdateLlmProvider/UpdateLlmProviderCommand.cs` - full provider-update mediator contract.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/UpdateLlmProvider/UpdateLlmProviderCommandValidator.cs` - validates the complete provider request.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/UpdateLlmProvider/UpdateLlmProviderHandler.cs` - validates value objects, enforces slug uniqueness, and persists one aggregate update.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/UpdateLlmProvider/Request.cs` - full FastEndpoints replacement request.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/UpdateLlmProvider/Endpoint.cs` - protected provider `PUT` endpoint.
- `src/services/Chat/Chat.Infrastructure/ModelCatalog/Caching/LlmProviderUpdatedCacheHandler.cs` - invalidates the public catalog cache tag.
- `src/services/Chat/Chat.Infrastructure/Database/Migrations/*_ModelCatalogFeaturedOrdering.cs` - EF-generated schema migration.
- `src/services/Chat/Chat.Infrastructure/Database/Migrations/*_ModelCatalogFeaturedOrdering.Designer.cs` - EF-generated migration metadata.

**Modify**

- `src/services/Chat/Chat.Domain/ModelCatalog/LlmProvider.cs` - replace provider sort order with `IsFeatured`; remove model sort-order operations.
- `src/services/Chat/Chat.Domain/ModelCatalog/Entities/LlmModel.cs` - remove model sort-order state and constructor input.
- `src/services/Chat/Chat.Domain/ModelCatalog/ILlmProviderRepository.cs` - add a slug-existence query that excludes the provider being updated.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/CreateLlmProvider/CreateLlmProviderCommand.cs` - accept `IsFeatured`.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/CreateLlmProvider/CreateLlmProviderCommandValidator.cs` - remove numeric sort-order validation.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/CreateLlmProvider/CreateLlmProviderHandler.cs` - pass the feature flag into the aggregate.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/AddLlmModel/AddLlmModelCommand.cs` - remove model sort order.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/AddLlmModel/AddLlmModelCommandValidator.cs` - remove numeric sort-order validation.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/AddLlmModel/AddLlmModelHandler.cs` - stop constructing and passing `SortOrder`.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Results/LlmProviderResult.cs` - expose `IsFeatured` instead of `SortOrder`.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Results/LlmModelResult.cs` - remove model `SortOrder`.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Results/ModelCatalogResultMapper.cs` - map the flag and alphabetize aggregate model results.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Queries/GetPublicModelCatalog/PublicLlmProviderReadModel.cs` - expose `IsFeatured`.
- `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Queries/GetPublicModelCatalog/PublicLlmModelReadModel.cs` - remove model `SortOrder`.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/CreateLlmProvider/Request.cs` - accept `IsFeatured`.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/CreateLlmProvider/Endpoint.cs` - map `IsFeatured` to the command.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/AddLlmModel/Request.cs` - remove model `SortOrder`.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/AddLlmModel/Endpoint.cs` - stop mapping model `SortOrder`.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/Responses/LlmProviderResponse.cs` - expose `IsFeatured`.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/Responses/LlmModelResponse.cs` - remove model `SortOrder`.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/Responses/ModelCatalogResponseMapper.cs` - map the new response shapes.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/GetModelCatalog/ProviderResponse.cs` - expose `IsFeatured`.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/GetModelCatalog/ModelResponse.cs` - remove model `SortOrder`.
- `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/GetModelCatalog/ResponseMapper.cs` - map the new public response shapes.
- `src/services/Chat/Chat.Infrastructure/ModelCatalog/Configurations/LlmProviderConfiguration.cs` - persist and index `IsFeatured`.
- `src/services/Chat/Chat.Infrastructure/ModelCatalog/Configurations/LlmModelConfiguration.cs` - remove model sort-order mapping/index.
- `src/services/Chat/Chat.Infrastructure/ModelCatalog/Repositories/LlmProviderRepository.cs` - implement the excluding-provider slug query.
- `src/services/Chat/Chat.Infrastructure/ModelCatalog/Readers/PublicModelCatalogDapperReader.cs` - select `IsFeatured`, remove model sort order, and apply deterministic ordering.
- `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs` - register provider-update cache invalidation.
- `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs` - updated by EF migration generation.

Historical migration files remain unchanged.

---

### Task 1: Replace Domain Sort Order With Provider Feature Status

**Files:**

- Modify: `src/services/Chat/Chat.Domain/ModelCatalog/LlmProvider.cs`
- Modify: `src/services/Chat/Chat.Domain/ModelCatalog/Entities/LlmModel.cs`
- Create: `src/services/Chat/Chat.Domain/ModelCatalog/Events/LlmProviderUpdated.cs`
- Delete: `src/services/Chat/Chat.Domain/ModelCatalog/ValueObjects/SortOrder.cs`

- [ ] **Step 1: Add the provider-updated domain event**

Create `LlmProviderUpdated.cs`:

```csharp
using Chat.Domain.ModelCatalog.ValueObjects;

using SharedKernel;

namespace Chat.Domain.ModelCatalog.Events;

public sealed record LlmProviderUpdated(LlmProviderId ProviderId) : IDomainEvent;
```

- [ ] **Step 2: Replace provider sort-order state and behavior**

In `LlmProvider`, replace `SortOrder` with:

```csharp
public bool IsFeatured { get; private set; }
```

Change the private constructor and factory to receive a boolean:

```csharp
private LlmProvider
(
    LlmProviderId id,
    ProviderName name,
    ProviderSlug slug,
    bool isFeatured
) : base(id)
{
    Name = name;
    Slug = slug;
    IsFeatured = isFeatured;
}

public static LlmProvider Create
(
    ProviderName name,
    ProviderSlug slug,
    bool isFeatured
) => new
(
    id: LlmProviderId.New(),
    name: name,
    slug: slug,
    isFeatured: isFeatured
);
```

Remove the `sortOrder` parameter from `AddModel` and from the `LlmModel.Create` call. Delete `UpdateModelSortOrder` and `UpdateSortOrder`. Add one idempotent full-profile mutation:

```csharp
public void UpdateDetails
(
    ProviderName name,
    ProviderSlug slug,
    AssetKey? logoKey,
    bool isFeatured
)
{
    if (Name == name && Slug == slug && LogoKey == logoKey && IsFeatured == isFeatured)
    {
        return;
    }

    Name = name;
    Slug = slug;
    LogoKey = logoKey;
    IsFeatured = isFeatured;
    AddDomainEvent(new LlmProviderUpdated(Id));
}
```

- [ ] **Step 3: Remove sort order from `LlmModel`**

Delete the `SortOrder` property, constructor parameter, assignment, factory parameter, and `UpdateSortOrder` method. The resulting constructor/factory shape should be:

```csharp
private LlmModel
(
    LlmModelId id,
    LlmProviderId providerId,
    ExternalModelId externalModelId,
    LlmModelProfile profile,
    bool isEnabled
) : base(id)
{
    ProviderId = providerId;
    ExternalModelId = externalModelId;
    Profile = profile;
    IsEnabled = isEnabled;
}

internal static LlmModel Create
(
    LlmProviderId providerId,
    ExternalModelId externalModelId,
    LlmModelProfile profile
) => new
(
    id: LlmModelId.New(),
    providerId: providerId,
    externalModelId: externalModelId,
    profile: profile,
    isEnabled: true
);
```

- [ ] **Step 4: Delete the obsolete value object**

Delete `src/services/Chat/Chat.Domain/ModelCatalog/ValueObjects/SortOrder.cs`.

- [ ] **Step 5: Check domain production references**

Run:

```bash
rg -n "SortOrder|sortOrder" src/services/Chat/Chat.Domain
```

Expected: no matches. Do not inspect or modify test references in this task.

- [ ] **Step 6: Commit the domain change**

```bash
git add src/services/Chat/Chat.Domain
git commit -m "refactor(chat): replace catalog sort order with featured status"
```

---

### Task 2: Update Create, Add, Result, And Admin Response Contracts

**Files:**

- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/CreateLlmProvider/CreateLlmProviderCommand.cs`
- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/CreateLlmProvider/CreateLlmProviderCommandValidator.cs`
- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/CreateLlmProvider/CreateLlmProviderHandler.cs`
- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/AddLlmModel/AddLlmModelCommand.cs`
- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/AddLlmModel/AddLlmModelCommandValidator.cs`
- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/AddLlmModel/AddLlmModelHandler.cs`
- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Results/LlmProviderResult.cs`
- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Results/LlmModelResult.cs`
- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Results/ModelCatalogResultMapper.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/CreateLlmProvider/Request.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/CreateLlmProvider/Endpoint.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/AddLlmModel/Request.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/AddLlmModel/Endpoint.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/Responses/LlmProviderResponse.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/Responses/LlmModelResponse.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/Responses/ModelCatalogResponseMapper.cs`

- [ ] **Step 1: Change provider creation to accept `IsFeatured`**

Use this command shape:

```csharp
public sealed record CreateLlmProviderCommand
(
    string Name,
    string Slug,
    bool IsFeatured,
    string? LogoKey
) : ICommand<ErrorOr<LlmProviderResult>>;
```

Delete the `SortOrder` validator rule. In the handler, remove `SortOrder.Create`, its error collection, and pass:

```csharp
LlmProvider provider = LlmProvider.Create
(
    name: nameResult.Value,
    slug: slug,
    isFeatured: command.IsFeatured
);
```

- [ ] **Step 2: Change the create-provider FastEndpoint contract**

Replace request `SortOrder` with:

```csharp
public bool IsFeatured { get; init; }
```

Map it in `Endpoint.HandleAsync`:

```csharp
CreateLlmProviderCommand command = new
(
    Name: req.Name,
    Slug: req.Slug,
    IsFeatured: req.IsFeatured,
    LogoKey: req.LogoKey
);
```

An omitted JSON boolean remains `false`, which is the intended default.

- [ ] **Step 3: Remove sort order from model creation**

Remove `int? SortOrder` from `AddLlmModelCommand`, remove its validator rule, and remove all `SortOrder.Create` handling from `AddLlmModelHandler`. Call the aggregate with:

```csharp
ErrorOr<LlmModel> addModelResult = provider.AddModel
(
    externalModelId: externalModelIdResult.Value,
    profile: profile
);
```

Remove `SortOrder` from the FastEndpoints add-model request and command mapping.

- [ ] **Step 4: Update application result contracts**

Use these record shapes:

```csharp
public sealed record LlmProviderResult
(
    Guid Id,
    string Name,
    string Slug,
    bool IsFeatured,
    string? LogoKey,
    IReadOnlyCollection<LlmModelResult> Models
);
```

```csharp
public sealed record LlmModelResult
(
    Guid Id,
    Guid ProviderId,
    string ExternalModelId,
    string Name,
    string Description,
    int ContextWindow,
    bool SupportsVision,
    bool SupportsReasoning,
    bool SupportsToolCalling,
    bool IsEnabled
);
```

Update `ModelCatalogResultMapper` to map `provider.IsFeatured`, remove model `SortOrder`, and order models deterministically:

```csharp
Models: provider.Models
    .OrderBy(model => model.Profile.Name.Value)
    .ThenBy(model => model.Id.Value)
    .Select(model => model.ToResult())
    .ToList()
```

- [ ] **Step 5: Update shared admin API responses**

Replace provider response `SortOrder` with:

```csharp
public required bool IsFeatured { get; init; }
```

Delete model response `SortOrder`. Update `ModelCatalogResponseMapper` with:

```csharp
IsFeatured = provider.IsFeatured,
```

and remove both `SortOrder` assignments.

- [ ] **Step 6: Check application and admin API production references**

Run:

```bash
rg -n "SortOrder|sortOrder" \
  src/services/Chat/Chat.Application/ModelCatalog \
  src/services/Chat/Chat.Api/Endpoints/ModelCatalog
```

Expected: only public catalog and persistence references scheduled for later tasks, or no matches in the files changed by this task.

- [ ] **Step 7: Commit contract changes**

```bash
git add src/services/Chat/Chat.Application/ModelCatalog src/services/Chat/Chat.Api/Endpoints/ModelCatalog
git commit -m "refactor(chat): remove catalog sort order contracts"
```

---

### Task 3: Add The Full Admin Provider Update Operation

**Files:**

- Modify: `src/services/Chat/Chat.Domain/ModelCatalog/ILlmProviderRepository.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/ModelCatalog/Repositories/LlmProviderRepository.cs`
- Create: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/UpdateLlmProvider/UpdateLlmProviderCommand.cs`
- Create: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/UpdateLlmProvider/UpdateLlmProviderCommandValidator.cs`
- Create: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/UpdateLlmProvider/UpdateLlmProviderHandler.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/UpdateLlmProvider/Request.cs`
- Create: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/UpdateLlmProvider/Endpoint.cs`

- [ ] **Step 1: Create the mediator command and validator**

`UpdateLlmProviderCommand.cs`:

```csharp
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmProvider;

public sealed record UpdateLlmProviderCommand
(
    Guid ProviderId,
    string Name,
    string Slug,
    string? LogoKey,
    bool IsFeatured
) : ICommand<ErrorOr<LlmProviderResult>>;
```

`UpdateLlmProviderCommandValidator.cs`:

```csharp
using FluentValidation;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmProvider;

internal sealed class UpdateLlmProviderCommandValidator : AbstractValidator<UpdateLlmProviderCommand>
{
    public UpdateLlmProviderCommandValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(ModelCatalogLimits.ProviderNameMaxLength);

        RuleFor(x => x.Slug)
            .NotEmpty()
            .MaximumLength(ModelCatalogLimits.ProviderSlugMaxLength);

        RuleFor(x => x.LogoKey)
            .MaximumLength(ModelCatalogLimits.ProviderLogoKeyMaxLength)
            .When(x => x.LogoKey is not null);
    }
}
```

- [ ] **Step 2: Add a slug uniqueness query that excludes the current provider**

Add this overload to `ILlmProviderRepository` while retaining the existing create-provider query:

```csharp
Task<bool> ExistsBySlugAsync
(
    ProviderSlug slug,
    LlmProviderId excludedProviderId,
    CancellationToken cancellationToken = default
);
```

Implement it in `LlmProviderRepository`:

```csharp
public async Task<bool> ExistsBySlugAsync
(
    ProviderSlug slug,
    LlmProviderId excludedProviderId,
    CancellationToken cancellationToken = default
)
{
    return await db.LlmProviders
        .AnyAsync
        (
            provider => provider.Slug == slug && provider.Id != excludedProviderId,
            cancellationToken
        );
}
```

- [ ] **Step 3: Create the command handler**

```csharp
using Chat.Application.Abstractions.Database;
using Chat.Application.ModelCatalog.LlmProviders.Errors;
using Chat.Application.ModelCatalog.LlmProviders.Results;
using Chat.Domain.ModelCatalog;
using Chat.Domain.ModelCatalog.ValueObjects;
using Chat.Domain.Shared;

using ErrorOr;

using Mediator;

namespace Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmProvider;

internal sealed class UpdateLlmProviderHandler
(
    ILlmProviderRepository providers,
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateLlmProviderCommand, ErrorOr<LlmProviderResult>>
{
    public async ValueTask<ErrorOr<LlmProviderResult>> Handle
    (
        UpdateLlmProviderCommand command,
        CancellationToken cancellationToken
    )
    {
        ErrorOr<LlmProviderId> providerIdResult = LlmProviderId.Create(command.ProviderId);
        ErrorOr<ProviderName> nameResult = ProviderName.Create(command.Name);
        ErrorOr<ProviderSlug> slugResult = ProviderSlug.Create(command.Slug);
        AssetKey? logoKey = null;
        List<Error> errors = [];

        if (providerIdResult.IsError)
        {
            errors.AddRange(providerIdResult.Errors);
        }

        if (nameResult.IsError)
        {
            errors.AddRange(nameResult.Errors);
        }

        if (slugResult.IsError)
        {
            errors.AddRange(slugResult.Errors);
        }

        if (command.LogoKey is not null)
        {
            ErrorOr<AssetKey> logoKeyResult = AssetKey.Create(command.LogoKey);

            if (logoKeyResult.IsError)
            {
                errors.AddRange(logoKeyResult.Errors);
            }
            else
            {
                logoKey = logoKeyResult.Value;
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        LlmProvider? provider = await providers.GetByIdAsync(providerIdResult.Value, cancellationToken);

        if (provider is null)
        {
            return LlmProviderOperationErrors.ProviderNotFound(providerIdResult.Value);
        }

        ProviderSlug slug = slugResult.Value;

        bool slugExists = await providers.ExistsBySlugAsync(slug, provider.Id, cancellationToken);

        if (slugExists)
        {
            return LlmProviderOperationErrors.SlugAlreadyExists(slug);
        }

        provider.UpdateDetails
        (
            name: nameResult.Value,
            slug: slug,
            logoKey: logoKey,
            isFeatured: command.IsFeatured
        );

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return provider.ToResult();
    }
}
```

- [ ] **Step 4: Create the full FastEndpoints request**

```csharp
namespace Chat.Api.Endpoints.ModelCatalog.UpdateLlmProvider;

internal sealed class Request
{
    public required string Name { get; init; }

    public required string Slug { get; init; }

    public string? LogoKey { get; init; }

    public required bool IsFeatured { get; init; }
}
```

- [ ] **Step 5: Create the protected full-replacement endpoint**

Use `PUT /model-catalog/providers/{providerId}`. Because this is a full provider replacement, `LogoKey: null` or an omitted logo key removes the current logo.

```csharp
using Chat.Api.Endpoints.ModelCatalog.Responses;
using Chat.Api.Security;
using Chat.Application.ModelCatalog.LlmProviders.Commands.UpdateLlmProvider;
using Chat.Application.ModelCatalog.LlmProviders.Results;

using ErrorOr;

using FastEndpoints;

using Mediator;

using Shared.Api.Endpoints;

namespace Chat.Api.Endpoints.ModelCatalog.UpdateLlmProvider;

internal sealed class Endpoint(ISender sender) : BaseEndpoint<Request, LlmProviderResponse>
{
    public const string RouteName = "Chat.ModelCatalog.LlmProviders.Update";

    public override void Configure()
    {
        Put("/model-catalog/providers/{providerId}");
        Version(1);

        Permissions(ChatPermissions.ManageModelCatalog);

        Options(builder => builder.WithName(RouteName));

        Description(descriptor =>
        {
            descriptor
                .WithSummary("Update LLM Provider")
                .WithDescription("Replaces the editable profile of an LLM provider in the model catalog.")
                .Produces<LlmProviderResponse>(StatusCodes.Status200OK, "application/json")
                .ProducesProblemDetails(StatusCodes.Status400BadRequest, "application/json")
                .ProducesProblemDetails(StatusCodes.Status409Conflict, "application/json")
                .ProducesProblemDetails(StatusCodes.Status404NotFound, "application/json")
                .WithTags(CustomTags.ModelCatalog);
        });
    }

    public override async Task HandleAsync(Request req, CancellationToken ct)
    {
        UpdateLlmProviderCommand command = new
        (
            ProviderId: Route<Guid>("providerId"),
            Name: req.Name,
            Slug: req.Slug,
            LogoKey: req.LogoKey,
            IsFeatured: req.IsFeatured
        );

        ErrorOr<LlmProviderResult> result = await sender.Send(command, ct);

        await SendErrorOrAsync
        (
            errorOr: result,
            mapper: ModelCatalogResponseMapper.ToResponse,
            cancellationToken: ct
        );
    }
}
```

- [ ] **Step 6: Commit the admin operation**

```bash
git add \
  src/services/Chat/Chat.Domain/ModelCatalog/ILlmProviderRepository.cs \
  src/services/Chat/Chat.Infrastructure/ModelCatalog/Repositories/LlmProviderRepository.cs \
  src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Commands/UpdateLlmProvider \
  src/services/Chat/Chat.Api/Endpoints/ModelCatalog/UpdateLlmProvider
git commit -m "feat(chat): update model catalog providers"
```

---

### Task 4: Invalidate The Public Catalog When A Provider Changes

**Files:**

- Create: `src/services/Chat/Chat.Infrastructure/ModelCatalog/Caching/LlmProviderUpdatedCacheHandler.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add the cache handler**

```csharp
using Chat.Domain.ModelCatalog.Events;

using Mediator;

using Shared.Infrastructure.DomainEvents;

using ZiggyCreatures.Caching.Fusion;

namespace Chat.Infrastructure.ModelCatalog.Caching;

internal sealed class LlmProviderUpdatedCacheHandler(IFusionCache cache)
    : INotificationHandler<DomainEventNotification<LlmProviderUpdated>>
{
    public async ValueTask Handle
    (
        DomainEventNotification<LlmProviderUpdated> notification,
        CancellationToken cancellationToken
    )
    {
        await cache.RemoveByTagAsync(ModelCatalogCacheTags.Catalog, token: cancellationToken);
    }
}
```

- [ ] **Step 2: Register the handler through Mediator**

In `DependencyInjection.AddCacheServices`, keep the existing model-profile handler and add:

```csharp
services
    .AddScoped<INotificationHandler<DomainEventNotification<LlmProviderUpdated>>,
        LlmProviderUpdatedCacheHandler>();
```

- [ ] **Step 3: Commit cache invalidation**

```bash
git add \
  src/services/Chat/Chat.Infrastructure/ModelCatalog/Caching/LlmProviderUpdatedCacheHandler.cs \
  src/services/Chat/Chat.Infrastructure/DependencyInjection.cs
git commit -m "fix(chat): invalidate catalog when provider changes"
```

---

### Task 5: Update The Public Catalog Contract And Ordering Query

**Files:**

- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Queries/GetPublicModelCatalog/PublicLlmProviderReadModel.cs`
- Modify: `src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Queries/GetPublicModelCatalog/PublicLlmModelReadModel.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/ModelCatalog/Readers/PublicModelCatalogDapperReader.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/GetModelCatalog/ProviderResponse.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/GetModelCatalog/ModelResponse.cs`
- Modify: `src/services/Chat/Chat.Api/Endpoints/ModelCatalog/GetModelCatalog/ResponseMapper.cs`

- [ ] **Step 1: Change public application read models**

Use:

```csharp
public sealed record PublicLlmProviderReadModel
(
    Guid Id,
    string Name,
    string Slug,
    bool IsFeatured,
    string? LogoKey,
    IReadOnlyCollection<PublicLlmModelReadModel> Models
);
```

```csharp
public sealed record PublicLlmModelReadModel
(
    Guid Id,
    Guid ProviderId,
    string ExternalModelId,
    string Name,
    string Description,
    int ContextWindow,
    bool SupportsVision,
    bool SupportsReasoning,
    bool SupportsToolCalling
);
```

- [ ] **Step 2: Rewrite the Dapper projection and ordering**

In the provider query, select:

```sql
p.is_featured as "IsFeatured"
```

instead of `p.sort_order`, and order providers with:

```sql
order by p.is_featured desc, p.name, p.id;
```

Remove the model `sort_order` selection and order models with:

```sql
order by m.provider_id, m.name, m.id;
```

Update `ProviderRow` to contain `bool IsFeatured`, remove `SortOrder` from `ModelRow`, map `IsFeatured: provider.IsFeatured`, and remove both read-model `SortOrder` arguments.

- [ ] **Step 3: Change public FastEndpoints response contracts**

In `ProviderResponse`, replace `SortOrder` with:

```csharp
public required bool IsFeatured { get; init; }
```

Delete `SortOrder` from `ModelResponse`. Update `ResponseMapper` with:

```csharp
IsFeatured = provider.IsFeatured,
```

and remove provider/model `SortOrder` assignments. Keep all capability fields unchanged so frontend clients can filter the complete model list locally.

- [ ] **Step 4: Commit public catalog behavior**

```bash
git add \
  src/services/Chat/Chat.Application/ModelCatalog/LlmProviders/Queries/GetPublicModelCatalog \
  src/services/Chat/Chat.Infrastructure/ModelCatalog/Readers/PublicModelCatalogDapperReader.cs \
  src/services/Chat/Chat.Api/Endpoints/ModelCatalog/GetModelCatalog
git commit -m "feat(chat): order public catalog by feature status and name"
```

---

### Task 6: Update EF Core Persistence And Generate The Migration

**Files:**

- Modify: `src/services/Chat/Chat.Infrastructure/ModelCatalog/Configurations/LlmProviderConfiguration.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/ModelCatalog/Configurations/LlmModelConfiguration.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Database/Migrations/*_ModelCatalogFeaturedOrdering.cs`
- Create: `src/services/Chat/Chat.Infrastructure/Database/Migrations/*_ModelCatalogFeaturedOrdering.Designer.cs`
- Modify: `src/services/Chat/Chat.Infrastructure/Database/Migrations/ChatDbContextModelSnapshot.cs`

- [ ] **Step 1: Map provider `IsFeatured`**

Replace the provider `SortOrder` conversion with:

```csharp
builder.Property(provider => provider.IsFeatured)
    .HasDefaultValue(false)
    .IsRequired();
```

Replace the old sort/name index with:

```csharp
builder.HasIndex(provider => new { provider.IsFeatured, provider.Name })
    .IsDescending(true, false);
```

- [ ] **Step 2: Remove model sort-order persistence**

Delete the `SortOrder` property conversion and the `{ ProviderId, SortOrder }` index from `LlmModelConfiguration`. Keep the unique `{ ProviderId, ExternalModelId }` index.

- [ ] **Step 3: Generate the EF Core migration**

Run with the repository-required elevated permission when Codex executes it:

```bash
dotnet ef migrations add ModelCatalogFeaturedOrdering \
  --project src/services/Chat/Chat.Infrastructure/Chat.Infrastructure.csproj \
  --startup-project src/services/Chat/Chat.Api/Chat.Api.csproj \
  --context ChatDbContext \
  --output-dir Database/Migrations
```

Expected: EF generates the migration, designer, and updates `ChatDbContextModelSnapshot.cs`.

- [ ] **Step 4: Inspect the generated migration**

The generated `Up` method must perform the equivalent of:

```csharp
migrationBuilder.DropIndex
(
    name: "ix_llm_models_provider_id_sort_order",
    table: "llm_models"
);

migrationBuilder.DropIndex
(
    name: "ix_llm_providers_sort_order_name",
    table: "llm_providers"
);

migrationBuilder.DropColumn
(
    name: "sort_order",
    table: "llm_models"
);

migrationBuilder.DropColumn
(
    name: "sort_order",
    table: "llm_providers"
);

migrationBuilder.AddColumn<bool>
(
    name: "is_featured",
    table: "llm_providers",
    type: "boolean",
    nullable: false,
    defaultValue: false
);

migrationBuilder.CreateIndex
(
    name: "ix_llm_providers_is_featured_name",
    table: "llm_providers",
    columns: new[] { "is_featured", "name" },
    descending: new[] { true, false }
);
```

The generated `Down` method must drop `is_featured`, restore both non-nullable integer `sort_order` columns with default `1`, and recreate the former indexes. Do not edit older migration files.

- [ ] **Step 5: Commit persistence changes**

```bash
git add \
  src/services/Chat/Chat.Infrastructure/ModelCatalog/Configurations \
  src/services/Chat/Chat.Infrastructure/Database/Migrations
git commit -m "feat(chat): migrate catalog to featured ordering"
```

---

### Task 7: Verify Production Code And Document The Deferred Test Work

**Files:**

- No production source changes expected.
- Do not modify files under `tests/`.

- [ ] **Step 1: Confirm active production code no longer uses numeric sort order**

Run:

```bash
rg -n "SortOrder|sortOrder|sort_order" \
  src/services/Chat \
  --glob '!**/Database/Migrations/**'
```

Expected: no matches. Historical migrations may still contain `sort_order`, which is intentional.

- [ ] **Step 2: Confirm the public query preserves all capability fields**

Run:

```bash
rg -n "SupportsVision|SupportsReasoning|SupportsToolCalling" \
  src/services/Chat/Chat.Infrastructure/ModelCatalog/Readers/PublicModelCatalogDapperReader.cs \
  src/services/Chat/Chat.Api/Endpoints/ModelCatalog/GetModelCatalog
```

Expected: each capability remains selected, mapped, and exposed in the public response.

- [ ] **Step 3: Build the Chat API production graph**

Run with elevated permission when Codex executes it:

```bash
dotnet build src/services/Chat/Chat.Api/Chat.Api.csproj
```

Expected: build succeeds with zero warnings and zero errors.

- [ ] **Step 4: Build the migration worker**

Run with elevated permission when Codex executes it:

```bash
dotnet build src/workers/Chat.MigrationWorker/Chat.MigrationWorker.csproj
```

Expected: build succeeds with zero warnings and zero errors.

- [ ] **Step 5: Record the intentional test-suite deferral**

Do not run or build the full solution in this implementation pass. Existing domain and application tests construct providers/models with `SortOrder`; those files are intentionally deferred to a separate user-approved test task.

- [ ] **Step 6: Review the final diff**

Run:

```bash
git diff --stat HEAD
git diff HEAD -- src/services/Chat
```

Expected: changes are limited to model-catalog domain, application, API, infrastructure, and the new migration. No test files are changed.
