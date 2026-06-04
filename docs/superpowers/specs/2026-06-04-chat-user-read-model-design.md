# Chat User Read Model Design

## Goal

Chat API will consume identity lifecycle events published by IdentityIngress and maintain a local infrastructure-owned user read model keyed by Auth0 user ids.

## Decisions

- Auth0 `sub` / ingress `ProviderUserId` is the Chat user identifier.
- Chat will not create or expose its own user id in this pass.
- Chat.Application will not define a user reader contract yet; application-facing access will be discussed later.
- The user read model exists only in Chat.Infrastructure for now.
- Deleted users are marked as deleted instead of physically removed.
- No tests will be added in this pass because the infrastructure testing approach is not decided yet.

## Architecture

IdentityIngress receives Auth0 events, maps supported user lifecycle events to shared contracts, and publishes the mapped event object. Chat.Infrastructure registers MassTransit consumers for those shared contracts and updates its local user read model inside the Chat database.

The read model is not a domain aggregate. It is integration state owned by Chat.Infrastructure so future Chat features can resolve the authenticated Auth0 user id against locally projected identity data without coupling to IdentityIngress or Auth0 APIs.

## Components

### IdentityIngress Publishing

The Auth0 endpoint must publish the mapped integration event payload, not the `MappedIdentityEvent` wrapper. This lets MassTransit route messages by the shared contract type:

```csharp
await messageBus.PublishAsync(mapping.Event, ct);
```

### Claims Identity

Chat API keeps JWT inbound claim mapping disabled:

```csharp
jwt.MapInboundClaims = false;
```

`ClaimsPrincipalExtensions.GetUserId()` should read the raw Auth0 `sub` claim first. A `ClaimTypes.NameIdentifier` fallback is acceptable for compatibility, but the primary identity value is `sub`.

### Chat User Read Model

Chat.Infrastructure will define the user read model in a new `Users` area. The model will store:

- Auth0 user id from `ProviderUserId`
- identity provider name
- email
- email verification state
- display name
- registration/update timestamps from ingress event occurrence time
- deletion state and deletion timestamp

Read-model field length limits will be centralized in Chat.Infrastructure alongside the user projection. The limits should not be added to Chat.Application in this pass because application user-reader contracts are intentionally out of scope.

The database key should make provider + provider user id unique. Because the current provider is Auth0, this still leaves room for future provider-aware messages without introducing a Chat-owned id.

### Consumers

Chat.Infrastructure will register MassTransit consumers for:

- `UserRegistered`
- `UserUpdated`
- `UserDeleted`

`UserRegistered` upserts the row using provider + provider user id and clears deletion state.

`UserUpdated` upserts the row using provider + provider user id and clears deletion state.

`UserDeleted` marks the row deleted and records the event occurrence time. If a delete event arrives before a create/update event, the consumer should still create a minimal deleted row so the projection remains idempotent and ordered-message assumptions are not required.

### Logging

Consumers will emit structured source-generated logs using the same general style as `LoggingBehavior`: injected `ILogger<T>`, `partial` consumer classes, private `[LoggerMessage]` partial methods, and structured fields such as provider, provider user id, and event id.

Consumers will not use stopwatch or elapsed-time logging.

## Data Flow

1. Auth0 sends a user lifecycle event to IdentityIngress.
2. IdentityIngress maps the payload to a shared identity event contract.
3. IdentityIngress publishes the mapped shared contract.
4. Chat.Infrastructure MassTransit receives the shared contract.
5. The consumer updates the infrastructure-owned user read model in `ChatDbContext`.
6. The MassTransit EF inbox protects consumer idempotency at the transport level, while read-model upsert behavior protects against replay and out-of-order lifecycle messages.

## Error Handling

Consumers should let unexpected database or infrastructure exceptions bubble so MassTransit retry/error handling can apply.

Invalid event data that violates the shared contract expectations should be logged with structured values. The consumer should avoid throwing for harmless duplicate lifecycle events when an upsert or soft-delete can preserve a consistent projection.

## Verification

No tests will be added.

Verification will use build and migration checks only. Any `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet run`, or migration command requires elevated permission first, per project instructions.
