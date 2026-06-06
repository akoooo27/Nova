# Model Catalog Ordering Design

## Goal

Simplify public model catalog ordering by removing manually managed numeric sort orders. The backend returns a complete, deterministic catalog, while frontend clients use the returned model capabilities to apply presentation-specific filters.

## Provider Ordering

Providers retain one explicit product-curation signal: `IsFeatured`.

The public catalog orders providers by:

1. `IsFeatured` descending
2. Provider name ascending
3. Provider ID ascending

This allows providers such as OpenAI to appear first without requiring administrators to coordinate numeric positions. Provider ID is only a deterministic tie-breaker.

The provider creation contract accepts `IsFeatured` instead of `SortOrder`. Provider maintenance uses one full update operation for `Name`, `Slug`, `LogoKey`, and `IsFeatured`, including for providers that already exist when this change is deployed.

The update operation uses `PUT /model-catalog/providers/{providerId}`. All provider fields are supplied together and validated as one replacement state. A null `LogoKey` removes the existing logo. Slug uniqueness excludes the provider currently being updated.

## Model Ordering

LLM models do not have `SortOrder` or `IsFeatured` fields.

Within each provider, the public catalog orders models by:

1. Model name ascending
2. Model ID ascending

There is no current product requirement to promote individual models. A future requirement should use a field with explicit meaning, such as `IsRecommended` or a provider-level `DefaultModelId`, rather than reintroducing a generic numeric sort order.

## Public Catalog Contract

`SortOrder` is removed from provider and model public response contracts. The response continues to include all enabled providers and models, including each model's capability fields.

The backend guarantees deterministic ordering but does not provide capability-specific catalog variants.

## Frontend Responsibility

Frontend clients receive the complete enabled catalog and may filter models locally using fields such as vision, reasoning, and tool-calling support. Because the catalog already contains the necessary capability data and is expected to remain small, separate backend queries for these presentation filters are unnecessary.

Filtering must preserve the backend-provided alphabetical order unless a frontend experience has an explicit alternative ordering requirement.

## Persistence And Migration

The provider `sort_order` and model `sort_order` columns are removed. A provider `is_featured` column is added with a default value of `false` so existing providers remain non-featured until deliberately promoted.

Existing numeric sort-order values are not migrated into feature status because they do not reliably express that business meaning. Existing providers, including OpenAI, can be promoted through the full admin provider update operation after deployment.

## Error Handling

No ordering-specific validation errors remain after numeric sort orders are removed. The full provider update validates provider identity, name, slug, optional logo key, and slug uniqueness before changing the aggregate. A missing provider returns not found; a slug already used by another provider returns conflict.

## Scope

This design covers domain state, persistence, admin request contracts, result and public response contracts, and public catalog query ordering. It does not add server-side capability filtering, model recommendation semantics, or frontend implementation work.

Test changes are not included unless explicitly requested.
