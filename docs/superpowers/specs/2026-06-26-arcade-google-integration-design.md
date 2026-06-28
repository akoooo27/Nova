# Arcade Google Integration (User-Managed) — Design Spec

**Date:** 2026-06-26
**Status:** Approved design, pre-implementation

**Goal:** Let a signed-in user **connect, view, and disconnect their Google account** for Arcade-backed tools, starting with **read-only Gmail** (`https://www.googleapis.com/auth/gmail.readonly`). The user gets a single "Google" integration they can toggle on/off; once connected, the existing Arcade tools (e.g. `Gmail.WhoAmI`) run against their real, user-granted connection instead of the current force-authorize stopgap.

**Scope of this change:** Backend only — **Chat.Api** endpoints + an Application seam + an Infrastructure implementation. The BFF (`src/services/BFF`) already blanket-forwards `/api/chat/{**catch-all}` to Chat.Api with user-token attachment + antiforgery (`ChatApiProxyConfiguration`), so **no BFF changes are needed**. The frontend UI lives outside this repo and consumes the endpoints below.

**Builds on:** the existing Arcade wiring — `ArcadeOptions`, the singleton `ArcadeClient`, `AddArcadeAuth` (registered in both the API host and the TurnWorker), and `IArcadeAuthClient` / `ArcadeAuthClient` behind `GET /me/integrations/arcade/verify` (the custom user-verifier).

---

## 1. How Arcade models integrations (the part we use)

The integration surface on `ArcadeClient` splits into three groups; this feature uses the first two.

1. **`client.Auth`** — per-user OAuth flow (public API, user-facing).
   - `Authorize(AuthAuthorizeParams) → AuthorizationResponse` (`POST /v1/auth/authorize`). Starts/continues a flow for `UserID` against `AuthRequirement { ProviderID, ProviderType, Oauth2 { Scopes } }`, with optional `NextUri` (post-authorization redirect). Returns `{ ID, Status, URL, ProviderID, Scopes, UserID, Context }`. `Status ∈ {NotStarted, Pending, Completed, Failed}`; when already `Completed`, `URL` is null.
   - `ConfirmUser(AuthConfirmUserParams { FlowID, UserID }) → { AuthID, NextUri }` (`POST /v1/auth/confirm_user`) — the custom verifier hook, **already implemented** as `VerifyArcadeUser`.
2. **`client.Admin.UserConnections`** — manage existing connections (admin API; requires a management-capable API key).
   - `List(UserConnectionListParams { Provider.ID, User.ID, Limit, Offset }) → UserConnectionListPageResponse` (`GET /v1/admin/user_connections?provider[id]=…&user[id]=…`). Each `UserConnectionResponse`: `ID`, `ConnectionID`, `ConnectionStatus`, `ProviderID`, `ProviderType`, `ProviderDescription`, `ProviderUserInfo` (`JsonElement?` — the connected Google account), `Scopes`, `UserID`.
   - `Delete(UserConnectionDeleteParams { ID }) → Task` (`DELETE /v1/admin/user_connections/{id}`).
3. `client.Admin.AuthProviders` / `client.Admin.Secrets` — **out of scope**: account-level provider configuration (client id/secret, endpoints, PKCE). The Google provider is configured once in Arcade as `my-google-provider`; users never touch this.

**Mapping to this feature:** **connect** = `Auth.Authorize` (provider-first). **status** = `UserConnections.List`. **disconnect** = `UserConnections.List` → `Delete` each.

---

## 2. Decisions (locked)

1. **Scope:** Google **read-only Gmail** only (`gmail.readonly`). Configurable.
2. **Connect mechanism:** provider-first `Auth.Authorize` with an explicit `ProviderID` + scopes — a stable "Google" integration card independent of any one tool (not tool-driven `Tools.Authorize`).
3. **Provider id:** `my-google-provider` (a custom provider configured in the Arcade account), `ProviderType = "oauth2"`. Lives in config, not source.
4. **Disconnect granularity:** logical — "Disconnect Google" removes **all** of the user's `my-google-provider` connections (loop `Delete` over `List`). The API never exposes raw `connectionId`s to the client.
5. **Endpoint style:** direct injection of the Application seam into the endpoint (mirrors the sibling `VerifyArcadeUser`), **not** Mediator — these are thin external pass-throughs with no domain or persistence.

---

## 3. Configuration — separate `GoogleIntegrationOptions`

The Google config lives in its **own** options class, **not** on `ArcadeOptions`. This is load-bearing: the shared `AddArcadeAuth` (which binds + `ValidateOnStart`s `ArcadeOptions`) runs in **both** the API host and the TurnWorker. Putting required Google fields on `ArcadeOptions` makes the worker fail to start validating options it never uses (a user-connect redirect URI is meaningless in a background worker). So `ArcadeOptions` (`BaseUrl`, `ApiKey`) is untouched, and a new `GoogleIntegrationOptions` is bound + validated **only in the API host** (§5).

```csharp
// Chat.Infrastructure/Options/GoogleIntegrationOptions.cs
public sealed class GoogleIntegrationOptions
{
    public const string SectionName = "Arcade:GoogleIntegration";

    [Required] public string ProviderId { get; init; } = "my-google-provider";

    // Default empty on purpose: the config binder *appends* array values to a
    // non-empty code default instead of replacing them (verified), which would
    // duplicate/leak scopes. Config is the source of truth; MinLength(1) fails
    // fast if it's missing.
    [Required, MinLength(1)]
    public IReadOnlyList<string> Scopes { get; init; } = [];

    // Where Arcade returns the browser after a completed connect (the frontend
    // integrations page). Server-configured to avoid open-redirect; see §6.
    [Required] public Uri PostConnectRedirectUri { get; init; }
        = new("https://localhost:7001/settings/integrations");
}
```

`appsettings.json` (Chat.Api) nests the values under `Arcade:GoogleIntegration`:

```json
"Arcade": {
  "BaseUrl": "https://api.arcade.dev",
  "GoogleIntegration": {
    "ProviderId": "my-google-provider",
    "Scopes": [ "https://www.googleapis.com/auth/gmail.readonly" ],
    "PostConnectRedirectUri": "https://localhost:7001/settings/integrations"
  }
}
```

The TurnWorker's config is unchanged and it binds none of this.

---

## 4. Application seam — `Chat.Application/Abstractions/Arcade/`

New `IArcadeIntegrationClient`, alongside `IArcadeAuthClient`. Google-named methods (only provider for now); the Infrastructure impl maps "Google" → the configured provider id + scopes, so adding a provider later is an Infrastructure change, not an endpoint rewrite.

```csharp
public interface IArcadeIntegrationClient
{
    Task<GoogleIntegrationStatus> GetGoogleStatusAsync(string userId, CancellationToken cancellationToken);

    // Returns Connected (already authorized, no URL) or a consent URL to send the user to.
    Task<GoogleConnectResult> StartGoogleConnectAsync(string userId, CancellationToken cancellationToken);

    // Idempotent: removes every my-google-provider connection for the user.
    Task DisconnectGoogleAsync(string userId, CancellationToken cancellationToken);
}

public sealed record GoogleIntegrationStatus(
    bool Connected,
    string? AccountEmail,            // best-effort from ProviderUserInfo
    IReadOnlyList<string> Scopes);

public sealed record GoogleConnectResult(
    bool Connected,                  // true when Arcade reports Status == Completed
    Uri? AuthorizationUrl);          // null when already Connected
```

---

## 5. Infrastructure impl — `Chat.Infrastructure/Arcade/ArcadeIntegrationClient.cs`

Wraps `ArcadeClient` + `IOptions<ArcadeOptions>`. One class, Arcade SDK confined to it (same containment as `ArcadeAuthClient`).

- **`GetGoogleStatusAsync`** → `Admin.UserConnections.List` filtered by `Provider.ID = GoogleProviderId`, `User.ID = userId`. `Connected = items.Count > 0`; `Scopes` = union across items; `AccountEmail` = best-effort `ProviderUserInfo.email` (defensive `JsonElement` read, null if absent).
- **`StartGoogleConnectAsync`** → `Auth.Authorize` with `AuthRequirement { ProviderID = GoogleProviderId, ProviderType = "oauth2", Oauth2 { Scopes = GoogleScopes } }`, `UserID = userId`, `NextUri = GooglePostConnectRedirectUri`. Map `Status == Completed` → `Connected = true, AuthorizationUrl = null`; otherwise parse `response.URL` into `AuthorizationUrl`.
- **`DisconnectGoogleAsync`** → `List` (as above) then `Delete { ID = item.ID }` for each. No-op when the list is empty (idempotent).

> **`ConnectionStatus` note:** v1 treats *presence of a connection record* as connected and also surfaces `Scopes`. The exact `ConnectionStatus` string values (e.g. active vs. pending) are confirmed against a live call during implementation; if a non-active status needs filtering, it's a one-line predicate here.

**DI:** register `IArcadeIntegrationClient → ArcadeIntegrationClient` (scoped) inside `AddArcadeAuth`, next to `IArcadeAuthClient`. (It's only exercised by the API host; harmless if the shared method also runs in the TurnWorker.)

---

## 6. Endpoints — `Chat.Api/Endpoints/Integrations/Google/`

All require auth, `Version(1)`, tagged `CustomTags.Integrations`, `UserID = IUserContext.UserId`. Reachable from the browser via the BFF at `/api/chat/me/integrations/google/...`.

| Method & route | Purpose | Success |
| --- | --- | --- |
| `GET /me/integrations/google` | Status card | `200` `{ connected, accountEmail, scopes }` |
| `POST /me/integrations/google/connect` | Begin/continue connect | `200` `{ connected, authorizationUrl }` |
| `DELETE /me/integrations/google` | Disconnect (all) | `204` |

- **GET** → `GetGoogleStatusAsync` → response record. `EndpointWithoutRequest`.
- **POST connect** → `StartGoogleConnectAsync`. No request body in v1. Returns `{ connected: true, authorizationUrl: null }` when already connected, else `{ connected: false, authorizationUrl }`; the frontend redirects the browser to `authorizationUrl`. The browser-side flow then reuses the existing **`VerifyArcadeUser`** mid-flow confirmation, and Arcade finally redirects to `GooglePostConnectRedirectUri`, where the frontend re-fetches GET status.
- **DELETE** → `DisconnectGoogleAsync` → `204`.

Each endpoint produces `401` (unauthorized) and `502`/problem on Arcade failure via the existing problem-details setup.

**Open-redirect:** `NextUri` is a **server-configured** value (`GooglePostConnectRedirectUri`), never client-supplied in v1 — eliminates open-redirect. A future per-request `returnUrl` would require an origin allowlist.

---

## 7. Tool executor cleanup (follow-on, same PR or next)

`ArcadeToolExecutor.ExecuteAsync` currently force-calls `Tools.Authorize` before every `Execute` (`// Remove this code, when real auth is added`). Once users explicitly connect Google, that block should be **removed** so tool execution relies on the real user-managed connection; the executor already surfaces `authorization_required` (with the consent URL) when no connection exists, which the chat UI can route to the connect flow. Flagged here; gated on this feature shipping.

---

## 8. Out of scope

- Providers other than Google; non-readonly Gmail or other Google scopes.
- Account-level provider/secret configuration (`Admin.AuthProviders`, `Admin.Secrets`).
- Per-connection (id-level) management UI — disconnect is all-or-nothing per §2.4.
- Frontend UI, and any BFF route changes (the catch-all already covers it).
- Background polling of `Auth.Status` — the browser round-trip via `NextUri` drives completion.

---

## 9. Touch list

| File | Change |
| --- | --- |
| `Chat.Infrastructure/Options/ArcadeOptions.cs` | add `GoogleProviderId`, `GoogleScopes`, `GooglePostConnectRedirectUri` |
| `Chat.Api/appsettings.json` (+ env/secrets) | populate the new `Arcade` keys |
| `Chat.Application/Abstractions/Arcade/IArcadeIntegrationClient.cs` | new seam |
| `Chat.Application/Abstractions/Arcade/GoogleIntegrationStatus.cs`, `GoogleConnectResult.cs` | new records |
| `Chat.Infrastructure/Arcade/ArcadeIntegrationClient.cs` | new impl |
| `Chat.Infrastructure/DependencyInjection.cs` (`AddArcadeAuth`) | register the seam |
| `Chat.Api/Endpoints/Integrations/Google/GetGoogleIntegration/` | GET endpoint + response |
| `Chat.Api/Endpoints/Integrations/Google/ConnectGoogleIntegration/` | POST endpoint + response |
| `Chat.Api/Endpoints/Integrations/Google/DisconnectGoogleIntegration/` | DELETE endpoint |
</content>
</invoke>
