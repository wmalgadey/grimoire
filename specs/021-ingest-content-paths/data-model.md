# Phase 1 Data Model: Rename ContentRootPaths to an Ingest-Specific Type

Both entities below are existing C# records; this feature changes the *name* and *field
membership* of the first, and changes nothing about the second — it only widens where the
second is read from directly.

## IngestContentPaths (renamed from `ContentRootPaths`)

**Represents**: The Ingest-owned projection of wiki-root and write-lock locations,
derived once from `ResolvedGrimoirePaths` (ADR-022) wherever a caller needs these
specific paths without depending on the full resolution/validation pipeline.

**Namespace**: `Grimoire.Hub.ContentRoot` (unchanged — see plan.md Architectural
Constraints; this namespace is cross-agent per the ADR-013 ownership map and may host
per-agent-named types).

**Fields** (post-change):

| Field | Type | Source | Notes |
|---|---|---|---|
| `Root` | `string` | `resolved.WikiDir` | Wiki content root. Unchanged. |
| `TasksDir` | `string` | `resolved.TasksDir` | Task artifact directory. Unchanged. |
| `IndexPath` | `string` | `resolved.IndexPath` | `index.md` path. Unchanged. |
| `LogPath` | `string` | `resolved.LogPath` | `log.md` path. Unchanged. |
| `WriteLocksDir` | `string` | `resolved.WriteLocksDir` | Write-lock directory. Unchanged. |

**Fields removed** (previously duplicated `ResolvedGrimoirePaths.Ingest`):

| Removed field | Replacement read path |
|---|---|
| `SystemPromptPath` | `ResolvedGrimoirePaths.Ingest.SystemPromptPath` |
| `DefaultUserPromptPath` | `ResolvedGrimoirePaths.Ingest.DefaultUserPromptPath` |
| `PolicyPath` | `ResolvedGrimoirePaths.Ingest.PolicyPath` |

**Factory**: `FromResolved(ResolvedGrimoirePaths resolved)` — unchanged in behavior for
the five retained fields; simply stops assigning the three removed fields.

**Validation rules**: None at this type's level (unchanged) — path existence/validity is
enforced upstream by `GrimoirePathResolver` against `ResolvedGrimoirePaths`, before this
projection is constructed. This type never re-validates.

**Relationships**: Constructed from, and only from, a `ResolvedGrimoirePaths` instance
(1:1 derivation, no independent state). Registered as a DI singleton in
`HubHostComposition.cs` alongside `ResolvedGrimoirePaths` itself.

## ResolvedGrimoirePaths.Ingest (`AgentRuntimePaths`) — unchanged

**Represents**: The pre-existing, single-composition-point source (ADR-022) for
everything Ingest's runtime needs — its subfolder, worker DLL, instructions directory,
and the three instruction-file paths this feature's callers now read directly.

**Fields** (unchanged by this feature): `Dir`, `WorkerPath`, `InstructionsDir`,
`SystemPromptPath`, `PolicyPath`, `DefaultUserPromptPath` (nullable; non-null for
Ingest specifically, per its existing doc comment).

**Change in this feature**: None to the type itself. Its `SystemPromptPath` /
`DefaultUserPromptPath` / `PolicyPath` gain new direct readers (the call sites
previously reading the now-removed `IngestContentPaths` fields); no new field, no
behavior change.

## State Transitions

Not applicable — both types are immutable records constructed once per resolution and
never mutated.
