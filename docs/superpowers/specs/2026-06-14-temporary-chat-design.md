# Temporary Chat — Design Spec

**Goal:** Let a chat be marked **temporary** at creation. A temporary chat is a normal `ChatThread` that carries an immutable `IsTemporary` flag. The flag is **stored and authoritative** server-side; this spec adds nothing that *reads* or *acts on* the flag yet — it only establishes the entity, its persistence, and the create-time request plumbing. Retention/purge, read-gating (hiding from history, refusing GET), and memory suppression are explicitly out of scope.

**Builds on:** `docs/superpowers/specs/2026-06-12-chat-turn-pipeline-design.md` and `docs/superpowers/specs/2026-06-08-conversation-tree-design.md`. The `ChatThread` aggregate and turn flow they define are the surfaces we extend.

---

## 1. What ChatGPT does, and what we take from it

Observed behavior: creating a temporary chat returns a real chat id on the stream, but navigating to that id returns **forbidden**, not 404. A forbidden (not "not found") response means the conversation **is persisted server-side** and merely **access-gated** out of the normal read path. The `?temporary-chat=true` in the browser URL is a **frontend UI hint**, not the authoritative signal — the authoritative signal is what the client sends to the API at create time.

What we adopt now:

- Temporary chats **persist in the same store** (Postgres `chats` table) with a flag — this fits Nova's pipeline, where the TurnWorker re-loads the `ChatThread` from the database to run each turn.
- The flag is decided **at creation** and is **immutable** for the thread's life (the whole conversation is temporary or not).

What we defer (NOT in this spec):

- Deletion/retention — handled by a separate background job (runs nightly; deletes temporary threads, messages follow via cascade).
- Read-gating (forbidding GET, excluding from a history listing) — no such endpoints exist yet.
- Memory/training suppression — `NoOpMemoryRetriever` today, so there is nothing to suppress.

---

## 2. Binding rules inherited from the turn pipeline

These constrain every decision below.

1. **State transitions go through the `ChatThread` aggregate.** The flag is set via the aggregate factory, never via SQL.
2. **Ids-only job rule.** `TurnRequested` carries ids only; the worker re-loads all state from the database. Temporariness is a property of the persisted thread, so it is **never added to the job payload**.
3. **Single source of truth.** The flag lives in exactly one place. It is not denormalized onto messages or carried per-turn.

---

## 3. Architecture

### 3.1 Domain — `ChatThread` aggregate

Add an immutable boolean to the aggregate.

- New property: `public bool IsTemporary { get; private set; }`
- `ChatThread.Create(...)` gains a `bool isTemporary` parameter, stored on construction (and set on the private constructor used by `Create`).
- **No mutator method.** The flag is fixed for the thread's life. There is no "promote temporary to permanent" path.
- `AddUserMessage`, `BeginAssistantMessage`, `CompleteAssistantMessage`, `EditUserMessage`, `RegenerateAssistant`, `SelectMessage` — **untouched.** They operate on an already-loaded thread, which carries the flag for free.

`IsTemporary` is a plain `bool`, **not** a value object — it carries no validation or behavior.

### 3.2 Messages — no flag

`ChatMessage` does **not** store temporariness. It is fully derivable from the parent thread:

- Temporariness is immutable and set at thread creation, so every message in a temporary chat is temporary and could never disagree with its thread. Storing it per-message is denormalization defending an invariant the thread already guarantees.
- Queries lose nothing: `ChatMessage` already has a `ChatId` FK, so anything needing "temporary messages" joins/filters on the thread's flag.
- The existing `OnDelete(DeleteBehavior.Cascade)` from `ChatThread` to its messages means the nightly purge deletes **threads**, and messages follow automatically.

### 3.3 Persistence — EF mapping + migration

- `ChatThreadConfiguration`: add `builder.Property(x => x.IsTemporary).IsRequired();` → maps to a non-null `is_temporary boolean` column on the `chats` table.
- One new EF migration adding the column with a **default of `false`** so existing rows backfill cleanly.
- **No index** in this spec. There is no listing/filtering endpoint yet that would use one. When the history listing arrives, that is when a filtered index earns its place.

### 3.4 Request flow — query param on create only

`POST /chats?temporary-chat=true`

- `CreateChat/Endpoint.cs` `Request`: bind `temporary-chat` from the **query string** (FastEndpoints bind-from-query), type `bool`, default `false`.
- `CreateChatCommand`: add `bool IsTemporary = false`.
- `CreateChatHandler`: pass `IsTemporary` into `ChatThread.Create(...)`.
- `SendMessage` path: **no change.** It loads the thread; the flag rides along. No `IsTemporary` field is added to the SendMessage request, command, or `TurnRequested`.
- The frontend's `?temporary-chat=true` browser URL remains a pure UI hint; the authoritative signal is this create-time query param reaching the API.

---

## 4. Data flow

```
POST /chats?temporary-chat=true
  → CreateChat Request (binds temporary-chat from query, default false)
  → CreateChatCommand(IsTemporary)
  → CreateChatHandler
  → ChatThread.Create(..., isTemporary: true)   // flag stored on aggregate
  → SaveChanges → chats.is_temporary = true persisted
  → TurnRequested(ids only) published            // no flag in payload

Subsequent turns (POST /chats/{id}/messages):
  → SendMessageHandler loads ChatThread (flag already on it)
  → no flag read or acted upon in this spec
```

---

## 5. Out of scope (explicit)

- Deletion / retention timer — separate nightly background job.
- Read-gating: forbidding GET on a temporary chat, excluding it from history listing.
- Memory retrieval/write suppression.
- Any per-turn or per-message notion of temporariness.
- An index on `is_temporary`.

---

## 6. Testing

- **Domain:** `ChatThread.Create(isTemporary: true)` sets `IsTemporary = true`; default/`false` path sets `false`. No mutator exists (compile-time guarantee — assert via absence in review).
- **Handler:** `CreateChatHandler` forwards the command's `IsTemporary` into the created thread; `TurnRequested` payload is unchanged (carries no flag).
- **Persistence:** round-trip a temporary `ChatThread` through `ChatDbContext` and assert `IsTemporary` survives; assert the migration adds a non-null column defaulting to `false`.
- **Endpoint:** `POST /chats?temporary-chat=true` produces a thread with `IsTemporary = true`; omitting the query param yields `false`.
