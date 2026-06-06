# Favorite Models Design

## Goal

Allow authenticated users to maintain a personal list of favorite LLM models. Favorites are independent from user settings, memory, instructions, and any future default-model preference.

## Domain Boundary

Each favorite association is a small aggregate in the Chat bounded context:

```text
FavoriteModel
- FavoriteModelId
- UserId
- LlmModelId
- FavoritedAt
```

`FavoriteModelId` is a UUID v7 aggregate identity. A unique database constraint on `(UserId, LlmModelId)` enforces the business identity of the association.

Each association is independently created and removed. There is no collection-style `UserFavoriteModels` aggregate because there are no collection-wide invariants such as a maximum count, custom ordering, or a primary favorite.

`UserId` is a reusable Chat-domain value object wrapping the authenticated external identity. It validates null, empty, whitespace-only, and oversized values at creation. Domain objects can therefore accept `UserId` without repeating those checks.

`FavoriteModel` remains an `AggregateRoot<FavoriteModelId>` even though its current behavior is minimal. It raises no domain events because no domain behavior currently reacts to favorite changes.

## Model Catalog Relationship

`LlmModel` remains an entity owned by the `LlmProvider` aggregate. Favorites do not load, navigate to, or mutate that entity. They store only an opaque `LlmModelId` reference identifying the selected catalog model.

The application layer performs model availability checks through a lightweight catalog reader rather than loading the `LlmProvider` aggregate. Catalog mutations continue to go through `LlmProvider`.

Infrastructure may enforce a foreign key from `favorite_models.llm_model_id` to `llm_models.id`. This relational integrity constraint does not change aggregate ownership or permit independent domain mutation of `LlmModel`.

## HTTP Operations

FastEndpoints exposes these authenticated operations:

```http
PUT    /me/favorite-models/{modelId}
DELETE /me/favorite-models/{modelId}
GET    /me/favorite-models
```

The write endpoints use desired-state semantics. No toggle endpoint is provided.

### Add Favorite

`PUT` is idempotent and returns `204 No Content` on success.

The Mediator command handler:

1. Creates a domain `UserId` from `IUserContext.UserId`.
2. Creates an `LlmModelId` from the route value.
3. Loads the favorite aggregate by `(UserId, LlmModelId)`.
4. Returns success immediately when the aggregate already exists.
5. Checks through a lightweight catalog reader that the model exists and is enabled.
6. Obtains the current time from `IDateTimeProvider`.
7. Creates and persists `FavoriteModel`.
8. Treats a concurrent duplicate-key violation as the same successful final state.

A missing model returns `404 Not Found`. A disabled model that is not already favorited returns `409 Conflict`.

Only enabled models may be newly favorited. If an existing favorite is later disabled, another identical `PUT` remains successful because the requested state already exists.

### Remove Favorite

`DELETE` is idempotent and returns `204 No Content` whether or not the association exists.

The handler loads the favorite aggregate by `(UserId, LlmModelId)`. If it exists, the repository marks that aggregate for removal and the unit of work commits the deletion. If it does not exist, the operation succeeds without writing. Unfavoriting physically deletes the row; no soft-delete or activity history is maintained.

## Favorite Query

`GET` returns a flat list of favorite models. It joins favorite associations with the current LLM model and provider data.

Results are ordered globally by:

1. Model name ascending
2. Model ID ascending as a deterministic tie-breaker

Provider `IsFeatured` and provider catalog ordering do not affect favorite ordering.

Each result represents an LLM model. Its `Id` is the `LlmModelId`; the internal `FavoriteModelId` is not exposed through the query contract.

Each result contains:

```text
FavoriteLlmModel
- Id
- ExternalModelId
- Name
- Description
- ContextWindow
- SupportsVision
- SupportsReasoning
- SupportsToolCalling
- IsEnabled
- FavoritedAt
- Provider
  - Id
  - Name
  - Slug
  - LogoKey
```

Disabled favorites remain visible with `IsEnabled = false`, preserving the user's selection and allowing removal. Re-enabling the catalog model makes the existing favorite usable again.

## Persistence

The `favorite_models` table contains:

- `id` as the primary key
- `user_id`
- `llm_model_id`
- `favorited_at`
- A unique constraint on `(user_id, llm_model_id)`
- A foreign key from `llm_model_id` to `llm_models.id`

The unique index backing `(user_id, llm_model_id)` also supports favorite queries filtered by `user_id`, so no separate user-only index is required.

Physical deletion of an LLM model cascades to its favorite associations because those associations can no longer produce meaningful query results. Disabling a model does not remove favorites.

There is no foreign key to the infrastructure-owned user read model. Authentication establishes the requesting user, while the identity projection is eventually consistent and must not prevent valid favorite writes when identity events arrive late.

## Concurrency

Loading the aggregate avoids ordinary duplicate inserts. The unique constraint remains the authoritative concurrency guard when equivalent `PUT` requests both observe that no aggregate exists. Infrastructure translates that duplicate race into the same successful final state.

A model may be disabled after the availability check but before the favorite insert commits. The resulting disabled favorite is an acceptable final state because the feature explicitly preserves and returns disabled favorites.

## Components

- `FavoriteModel` aggregate and identifier value object
- Reusable Chat-domain `UserId` value object
- Favorite repository for aggregate loading, insertion, and removal
- Lightweight model-availability reader
- Favorite-list projection reader
- Mediator commands, query, and handlers
- FastEndpoints request handlers and response mapping
- EF Core configuration and migration

## Error Handling

- Invalid user or model identifiers return validation errors through the existing application pipeline.
- A missing catalog model maps to `404 Not Found`.
- A disabled model that is not already favorited maps to `409 Conflict`.
- Duplicate creation and removal of a missing association are successful idempotent outcomes.
- Unexpected persistence failures are allowed to propagate to the existing global error handling.

## Verification

Tests are not included unless explicitly requested. Verification will use build and migration checks. Any `dotnet` command requires elevated permission under the project instructions.
