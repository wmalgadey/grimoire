# Contract: Query Write Scope, Create-Only Mode, and Write Coordination

Covers three coupled surfaces this feature changes: the policy-file schema, the
`write_file` tool-result contract as seen by the agent, and the CLI/process contract
carrying the write-locks location into each agent process. Implementation lives in
`Grimoire.Domain.Guardrails`, `Grimoire.AgentRuntime.Guardrails(.Coordination)`,
`Grimoire.QueryAgent`, `Grimoire.IngestAgent`.

## 1. Policy file schema (`mode` field)

Additive, backward-compatible change to the existing `write[]` rule shape consumed
by `PolicyLoader`:

```json
{ "pathPrefix": "pages/", "mode": "create-only" }
```

- `mode` is optional. Absent ⇒ `"read-write"` (existing behavior, byte-for-byte —
  `data/agents/ingest/policy.json` needs no edit).
- Recognized values: `"read-write"`, `"create-only"`. Any other string is a
  **fail-closed load error** (`PolicyLoadFailure`), matching the existing
  `defaultDecision` strictness — never silently defaulted.
- `PolicyDecision.Allow()` carries the matched rule's mode forward (e.g.
  `Allow(isCreateOnly: true)`) so `GuardedToolExecutor` can act on it without
  `SafetyPolicy` performing any I/O.

## 2. `write_file` tool-result contract (agent-visible)

Extends the existing denial-result shape
(`denied: {reason}. This action is outside the safety policy; continue with your
remaining allowed work.`) with two new reasons that are **not** policy-scope
denials but are surfaced identically as `is_error` tool results, so the agent's
existing "a tool call failed, adapt and continue" behavior handles them with no
new prompt engineering required:

| Reason string | When | Suggested agent recovery (documented in `agents/query/system-prompt.md` and `agents/ingest/system-prompt.md`) |
|---|---|---|
| `create_only_target_exists` | Target already exists and the matched rule is `create-only` | Do not attempt to overwrite; if the intent was to extend existing content, that is out of this agent's scope |
| `write_conflict_stale_read` | Target changed since this run last read it | Re-read the file with `read_file` and retry the write with a version incorporating the current content |
| `write_coordination_timeout` | Lock acquisition exceeded the backoff cap (default 5s, configurable) | Treat as a transient failure; the insight/update is not preserved this turn |

All three are recorded as `DeniedActionRecord` entries (existing shape, existing
`denied_actions` surfacing into run-completion metadata and the Conversation
Record) — from the harness's perspective these are simply new `reason` values, no
new record shape.

## 3. `SharedFileWriteGuard` algorithm contract

```text
OnReadFile(canonicalPath, content):
    _readHashes[canonicalPath] = SHA256(content)

OnWriteFile(canonicalPath, content, isCreateOnly) -> WriteOutcome:
    lock = CrossProcessFileLock.For(canonicalPath, writeLocksDir)
    if not lock.TryAcquire(timeout: backoffCap):
        return Denied("write_coordination_timeout")
    try:
        exists = File.Exists(canonicalPath)
        if isCreateOnly and exists:
            return Denied("create_only_target_exists")
        if exists and not isCreateOnly:
            currentHash = SHA256(File.ReadAllBytes(canonicalPath))
            expectedHash = _readHashes.GetValueOrDefault(canonicalPath)
            if expectedHash is null or currentHash != expectedHash:
                return Denied("write_conflict_stale_read")
        // existing journal + atomic temp-file + rename write happens here,
        // still inside the lock
        PerformExistingAtomicWrite(canonicalPath, content)
        _readHashes[canonicalPath] = SHA256(content)
        return Allowed()
    finally:
        lock.Release()
```

- `backoffCap` default: 5000 ms, polling with short exponential backoff (e.g.
  25 ms → 200 ms cap per attempt). Configurable per agent via the same
  configuration surface as `QueryConcurrencyLimit` (`Grimoire:WriteLockTimeoutMs`
  or equivalent — exact key decided in `tasks.md`).
- A run's **own** first write to a path it never read (a brand-new page) has no
  entry in `_readHashes` and `exists` is false — always allowed (subject to the
  create-only/policy check), matching today's behavior exactly.
- Lock release is unconditional (`finally`) — including on cancellation from run
  interruption, so an interrupted run cannot wedge a target for later runs; an OS
  file lock also releases automatically if the process is killed outright.

## 4. Process/CLI contract: `--write-locks-dir`

Both `Grimoire.IngestAgent` and `Grimoire.QueryAgent` gain a new required CLI
argument, following the exact existing pattern of `--wiki-root` (ADR-002/009):

```text
--write-locks-dir <path>
```

- Supplied by the Hub from `ResolvedGrimoirePaths.WriteLocksDir` at spawn time
  (same composition point that supplies `--wiki-root`, `--policy-path`, etc.).
- The agent's composition root (`Program.cs`) passes it into the
  `GuardedToolExecutor`/`SharedFileWriteGuard` construction alongside the existing
  `SafetyPolicy`/`WriteJournal`/`repositoryRoot` arguments.
- Missing or unwritable directory: fail-closed, run fails before any tool call is
  dispatched (same posture as a missing policy file).

## 5. `completed` event metadata: created pages

`RunCompletionMetadata.CreatedArtifacts` (new, nullable list of wiki-root-relative
paths) is serialized on the `completed` NDJSON event as `createdPages`. The Hub
writes it verbatim into the turn's Conversation Record bookkeeping block as
`created_pages:` (empty list when the turn created nothing — never omitted, so
parsers do not need to distinguish "no field" from "no pages").

## 6. Backward compatibility

- `data/agents/ingest/policy.json` is unchanged — its rules have no `mode` field
  and behave exactly as `"read-write"` today. Ingest's writes to `index.md`/
  `log.md`/existing pages now additionally pass through the compare-and-swap
  check; because Ingest is single-slot, its own reads/writes never race
  themselves — the check only ever engages when Ingest races a concurrent Query
  synthesis write, which is precisely the bug this feature closes.
- Existing recorded eval fixtures for Ingest and Query (ADR-012) whose scenarios
  never touch a contested path are unaffected — the guard is a no-op when there is
  no concurrent contention and no prior read to compare against besides the run's
  own.
