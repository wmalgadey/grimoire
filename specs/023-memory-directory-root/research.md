# Phase 0 Research: Independent Memory Directory Root

**Feature**: `023-memory-directory-root` | **Date**: 2026-08-11 |
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

The Technical Context in `plan.md` carries no `NEEDS CLARIFICATION` markers: the language,
dependencies, storage and test tiers are all fixed by the existing solution, and the one
open question the spec itself flagged (the ADR-022 R1 conflict) is resolved below in R1.
This document records the eight decisions that shape the implementation, each traced to
the code that forced it.

R8 covers the author directive of 2026-08-11 (make the directory grouping visible in
`appsettings.json`), which arrived after the spec was clarified and is not yet reflected
in a functional requirement — see the scope note in [plan.md](./plan.md).

---

## R1 — The memory root is a fourth CLI switch, and ADR-022 rule R1 is amended

**Decision**: Add `--memory-dir` / `Grimoire__Paths__Memory__Dir` /
`Grimoire:Paths:Memory:Dir` as a fourth root at the same tier as the existing three, and
amend ADR-022 rule R1 from an exact enumeration of three switches to an exact enumeration
of four. Recorded in
[ADR-023](../../docs/adr/ADR-023-memory-directory-fourth-root.md). (The nested key form
comes from R8 below; the switch name is unaffected by it.)

**Rationale**: Spec FR-001 puts the memory folder "at the same configuration tier as" the
other roots, and spec assumption line 119 flagged the R1 conflict as a required
ADR-review step rather than a silent exception. ADR-022's own criterion for what earns a
switch is *operator-meaningful and independently relocatable*: the eleven switches it
deleted were internal layout details (`--raw-dir`, `--state-db`, `--findings-dir`, …) and
the three it kept were roots. The memory directory is squarely in the kept category — US1's
operator relocates it precisely to put bookkeeping on different storage. The cap's
anti-regrowth property is preserved because it remains an exact named enumeration with a
1:1 `HubPathSettings` parity assertion, not a count; ADR-022's "a new sub-path is never a
switch" sentence continues to bind unchanged.

**Alternatives considered**:

- *Configuration-file-only fourth root (no switch)*. Compatible with R1 as written, which
  is its only virtue. It would make the one root an operator actually wants to redirect the
  only root they cannot redirect from the command line — inverting the surface ADR-022 was
  tuning. Rejected.
- *Leave the four sub-paths under `WikiDir` and document absolute-path overrides*. Already
  possible today; it is the status quo the spec rejects (four keys to keep consistent, no
  single name for a backup rule). Rejected.
- *Nest the memory root under `DataDir`*. Reintroduces the shared-parent coupling ADR-022
  removed with `BaseDir`; relocating runtime data would drag bookkeeping along, violating
  FR-003. Rejected.
- *Amend ADR-022 in place rather than writing ADR-023*. Rejected for consistency with the
  house pattern: ADR-022 itself amended ADR-002/007/009/012/019/020 through a new document
  with a "Superseded and amended decisions" table, which keeps the reasoning for each change
  dated and attributable.

---

## R2 — The R2 tripwire is namespace-scoped for this root, because `memory` already exists as a production literal

**Decision**: Do **not** add `memory` to `NoCodeLevelPathDefaultsRuleTests`'
assembly-wide `_forbiddenDefaultLiterals` array. Add a separate, namespace-scoped case
asserting no type in `Grimoire.Hub.Runtime.Paths` contains the IL literal `memory`
(ADR-023 rule M2).

**Rationale**: ADR-022 R2's global scan works for `.grimoire` and `llm-wiki` because those
are path-shaped tokens that appear nowhere else in the solution. `memory` is not. It is
already a production IL literal at
[ConversationRecordStore.cs:112-125](../../backend/src/Grimoire.Hub/QueryConversations/ConversationRecordStore.cs#L112-L125),
where it is the conversation-context cache's source label — passed to
`ConversationRecordLogEvents.LogContextLoaded` as a structured-log field value and to
`HubMetrics.RecordConversationContextLoad` as a metric tag value, i.e. two published
observability contracts:

```csharp
ConversationRecordLogEvents.LogContextLoaded(_logger, conversationId, cached.Count, "memory");
HubMetrics.RecordConversationContextLoad("memory");
return new ConversationContextResult.Loaded(cached, "memory");
```

Adding `memory` globally would fail the build on day one on code that has nothing to do
with paths. The two ways out are renaming an unrelated observability contract, or scoping
the scan. Scoping wins: `Grimoire.Hub.Runtime.Paths` is already the only namespace
permitted to compose paths or read ambient process context (ADR-009, enforced by
`RuntimePathsBoundaryRuleTests`), so it is the only realistic site for a reintroduced path
default, and namespace-scoped IL scanning is an idiom the same test project already uses.

**Alternatives considered**:

- *Rename the conversation-cache source label to `cache`*. Rejected: it changes a log field
  value and a metric tag value — an observability contract with its own tests
  (`QueryConversationLogEventTests`, `QueryConversationMetricsTests`) — to accommodate a
  lint rule about an unrelated concept.
- *Pick a different default directory name that does not collide*. Rejected: FR-009 fixes
  the default at `memory`, and it is the right operator-facing name.
- *Skip the tripwire for this root entirely*. Rejected: FR-006 requires no code-level
  fallback for the memory root exactly as for the other three, and R2's whole purpose is to
  make that verifiable rather than reviewed.

**Residual risk, accepted and documented in ADR-023**: the memory root's no-code-default
guarantee is narrower than `.grimoire`'s and `llm-wiki`'s. It is backstopped behaviorally
by the FR-006/SC-004 integration test, which asserts the resolver throws
`GrimoirePathConfigurationMissingException` naming `Grimoire:Paths:Memory:Dir` when the key
is absent — a code-level default anywhere would make that test fail regardless of
namespace.

---

## R3 — The `[[tasks/…]]` wikilink becomes a bare task-id reference

**Decision**: Replace `Task: [[tasks/<task_id>.md]]` with `Task: <task_id>` in both the
Ingest system prompt and
[RestartReconciler.cs:130](../../backend/src/Grimoire.Hub/OperationalState/RestartReconciler.cs#L130).
Enforce the removal with a new IL tripwire (ADR-023 rule M3) covering `[[tasks/`,
`[[conversations/`, `[[findings/` and `[[remediation-tasks/`.

**Rationale**: A wikilink resolves within the wiki tree by definition; once `tasks/`
anchors at `MemoryDir` the link is dangling by construction. The constraint that made this
non-obvious is the harness log backstop:
[WikiLogAppender.cs:67](../../backend/src/Grimoire.AgentRuntime/WikiLog/WikiLogAppender.cs#L67)
decides whether to append its own `log.md` entry with

```csharp
backstopNeeded = !logContent.Contains(correlationId, StringComparison.Ordinal);
```

— a bare ordinal substring match on the raw task id, not a match on `[[tasks/` and not a
regex. So keeping the id in the paragraph in *any* form preserves dedup byte-for-byte,
while dropping it entirely would make every successful ingest run append a spurious
"(harness backstop)" entry plus a `wiki.log.backstop_appended` WARN and a counter
increment — silently, since no test couples the prompt to the dedup. The Query agent
already exhibits exactly this failure mode because its prompt never names the turn id.

ADR-017's format enforcement is not a constraint here: its two regexes in
[SharedFileWriteGuard.cs:52-61](../../backend/src/Grimoire.AgentRuntime/Guardrails/Coordination/SharedFileWriteGuard.cs#L52-L61)
constrain the `log.md` heading shape and the `index.md` catalog line shape and impose no
requirement on paragraph content.

**Alternatives considered**:

- *Point the wikilink outside the wiki (`[[../memory/tasks/…]]`)*. Rejected: it would
  encode the memory directory's relative position, which is operator-configurable and
  therefore unknowable from inside a page.
- *Drop the task reference entirely*. Rejected — the silent backstop regression above.
- *Add a dedup test coupling prompt convention to `WikiLogAppender`*. Deferred, not
  rejected: worth doing, but it is a pre-existing gap rather than one this feature opens.
  The plan's test strategy notes it as an optional hardening, not a requirement.

---

## R4 — Moving `tasks/` outside the wiki root does not touch the guarded write path

**Decision**: No guardrail, policy, or `GuardedToolExecutor` change is required.

**Rationale**: verified by tracing the write. The Ingest agent's task artifact is written
by `TaskArtifactStore` through direct file I/O on the raw `--tasks-dir` value
([TaskArtifactStore.cs:9-14](../../backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactStore.cs#L9-L14)):

```csharp
Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
var content = BuildMarkdown(document);
await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);
```

That namespace (`Grimoire.IngestAgent.TaskArtifact`) is already whitelisted for direct
filesystem writes by
[IngestAgentGuardedWriteBoundaryRuleTests.cs:29-37](../../backend/tests/Grimoire.ArchTests/IngestAgentGuardedWriteBoundaryRuleTests.cs#L29-L37)
— ADR-002's "each process owns its own artifact I/O". The `GuardedToolExecutor` is
constructed with `repositoryRoot: _options.WikiRoot` and never receives the tasks
directory. The Hub side (`HubTaskArtifactWriter`, `RestartReconciler`,
`KanbanBoardProjectionStore`, `TaskRecordWatcher`) is likewise path-agnostic plain I/O.
Nothing anywhere asserts containment of the tasks directory within the wiki root, and
`ResolveAgainst` already accepts an absolute value outside any root verbatim.

**Consequence worth naming**: today the policies' `"."` catch-all makes `tasks/`,
`conversations/`, `findings/` and `remediation-tasks/` *policy-reachable* for agent tool
calls — a write to `tasks/rogue.md` is allowed and only the prompt forbids it (see
[QueryWriteScopeDenialTests.cs:86-110](../../backend/tests/Grimoire.IntegrationTests/QueryWriteScopeDenialTests.cs#L86-L110)).
Moving the folders out of the wiki root closes that incidentally: they are no longer inside
the guarded root at all. This is a hardening, and it is what makes FR-012's instruction
edits a *removal* rather than a rewording — the guidance describes a tree the agent can no
longer see.

**Alternatives considered**: *Add explicit `excludePrefixes` for the four folders to the
three policies*. Rejected as dead weight — the folders are no longer under the policy
anchor, so the rules would match nothing.

---

## R5 — Re-capturing the eval recordings is a mandatory, gating implementation step

**Decision**: Treat recording re-capture as scheduled work in the final implementation
phase, run through the `.github/workflows/eval.yml` `workflow_dispatch` route by default,
with local capture as the fallback. Sequence the FR-012 instruction edits **last** so the
rest of the feature stays CI-green while it is built.

**Rationale**: FR-012 edits all three `system-prompt.md` files.
[Fingerprints.cs:32](../../backend/src/Grimoire.EvalRunner/Recording/Fingerprints.cs#L32)
hashes the full bytes of each prompt into the scenario manifest, and
[ReplayModelClient.cs:62-66](../../backend/src/Grimoire.AgentRuntime/Core/Adapters/Replay/ReplayModelClient.cs#L62-L66)
independently re-checks a per-turn `system_prompt_sha256` inside every sample file. So the
prompt text is pinned in two places, and there is no bless/accept verb in the EvalRunner
CLI (`capture | replay | status` only) — a manifest cannot be refreshed without freshly
captured samples.

Blast radius, measured: **22 scenarios / 252 files** (230 samples + 22 manifests) under
`backend/tests/Grimoire.AgentEvals/Fixtures/recordings/`. Editing the Lint prompt
invalidates 7 scenarios rather than 5, because `RemediationReVerificationStalenessCheck`
also reads `LintSystemPromptPath`. Because all three prompts change, all 22 go stale.

This is a hard PR gate, not an opt-in tier: `.github/workflows/ci.yml:57` runs
`dotnet test backend/tests/Grimoire.AgentEvals` **unfiltered**, so the SlowEval replay
tests execute on every PR. `scripts/test-fast.sh` will not catch the failure locally.

**Route chosen**: `workflow_dispatch` on `.github/workflows/eval.yml`, which captures via
LiteLLM → NVIDIA NIM using `secrets.NVIDIA_NIM_API_KEY`, then uploads the recordings tree
as an artifact to download and commit. It needs no personal API key, and it has no
`--scenario` filter — it re-captures everything, which is precisely what an all-three-prompts
change requires. Local fallback:
`dotnet run --project backend/src/Grimoire.EvalRunner -- capture --scenario <id>` with
`ANTHROPIC_AUTH_TOKEN` or the `GRIMOIRE_EVAL_PROVIDER_*` triple.

**Alternatives considered**:

- *Hand-patch `system_prompt_sha256` in the sample files*. Rejected, and it does not even
  work: the manifest stores a hash of each sample file as written, so editing a sample
  yields `TrustStatus.Mismatch`.
- *Make the instruction edits in a follow-up PR*. Rejected: SC-008 is a success criterion of
  *this* feature, and shipping the relocation without the instruction fix leaves the agents
  instructed about folders that no longer exist.

**Beyond the fingerprint**: the re-captured, re-scored evals are also the only real evidence
that removing the reserved-folder guidance did not degrade agent behavior. SC-008 is a
lexical check; the behavioral risk (the agent wandering, or dropping the task reference from
its log paragraph) is caught by the existing scenario thresholds. This is recorded in the
plan's Test Strategy.

---

## R6 — Ubiquitous Language and naming

**Decision**:

| Concept | Name |
| --- | --- |
| Spec term | Memory Folder / memory directory |
| Options group | `GrimoirePathOptions.Memory` (type `MemoryPathOptions`) |
| Options field for the root | `GrimoirePathOptions.Memory.Dir` |
| Configuration key | `Grimoire:Paths:Memory:Dir` |
| Environment variable | `Grimoire__Paths__Memory__Dir` |
| CLI switch | `--memory-dir <PATH>` |
| Resolved member | `ResolvedGrimoirePaths.MemoryDir` |
| Path-location name (log field, startup report, validation message) | `memory_dir` |
| Shipped default value | `memory` |

**Rationale**: mirrors the existing `data_dir` / `wiki_dir` / `agent_dir` quadruple
exactly, in the grouped form R8 establishes for all four roots — `Memory:Dir` ↔
`--memory-dir` ↔ `memory_dir` reads consistently across configuration, CLI and telemetry.
`ResolvedGrimoirePaths` stays flat (`MemoryDir`, not `Memory.Dir`): it is the *output* of
resolution, consumed by ~40 call sites that want one absolute path, and grouping it would
be churn with no invariant behind it. The `PathLocation.Name` values are snake_case
elsewhere and are consumed by the `paths_resolved` log event's `sources` field and by
validation messages, so consistency there is load-bearing, not cosmetic.

**Alternatives considered**: `BookkeepingDir`, `RecordsDir`, `AgentStateDir`. All rejected
in favor of the user-specified `memory`, which is also the shorter operator-facing word and
the one the spec's Ubiquitous Language already uses throughout.

---

## R7 — Eval workspace layout mirrors production

**Decision**: Change
[EvalWorkspace.cs:20](../../backend/src/Grimoire.EvalRunner/Workspace/EvalWorkspace.cs#L20)
from `Path.Combine(WikiRoot, "tasks")` to a sibling of the workspace wiki root, and delete
the now-dead tasks-exclusion filter from `PageFiles()`.

**Rationale**: the eval workspace exists to reproduce a production run in isolation. Leaving
its tasks directory inside the wiki root would keep the eval agent seeing a tree the
production agent no longer sees, which is exactly the "works in eval, differs in prod"
divergence ADR-012 and ADR-022 both went out of their way to close. The `PageFiles()` filter

```csharp
.Where(path => !path.StartsWith(TasksDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)
```

exists only to compensate for the nesting and becomes unreachable once the directory moves
out of `WikiRoot`.

**Fingerprint impact: none.** The manifest hashes instruction files, the policy, the fixture
directory and the scenario definition — not the workspace layout. This change does not by
itself invalidate any recording (though R5's prompt edits do).

**Alternatives considered**: *Leave `EvalWorkspace` alone*. Rejected — it would preserve a
known-stale assumption in the one harness whose job is fidelity, and the eval-independence
contract in `EvalIndependenceFromHubConfigurationTests` means the fix cannot arrive later
via hub configuration.

---

## R8 — `appsettings.json` is regrouped by anchoring root

**Decision**: Restructure the `Grimoire:Paths` section from a flat list of eleven keys
into four anchor groups (`Data`, `Wiki`, `Agent`, `Memory`) plus the ungrouped
`SecretsFile`. Each group's own root is the key `Dir`; every sibling key in the group is a
sub-path anchored at that group's resolved `Dir`. `GrimoirePathOptions` mirrors the tree.
Recorded in [ADR-023](../../docs/adr/ADR-023-memory-directory-fourth-root.md) as option
C-A, with structural rules M4 and M5.

**Rationale**: Author directive of 2026-08-11 — the configuration file should reflect the
directory structure, because the folders really are grouped and scoped to a base
directory. Beyond the directive, this feature is itself the argument. Re-anchoring
`TasksDir`, `ConversationsDir`, `FindingsDir` and `RemediationTasksDir` from `WikiDir` to
`MemoryDir` changes no key name and no value in the flat shape:

```jsonc
// before and after, under the flat shape — identical
"TasksDir": "tasks",
"ConversationsDir": "conversations",
```

The entire semantic change would show up in a diff as a moved comment. Under the grouped
shape the four keys physically move out of `Wiki` and into `Memory`, so the diff *is* the
change. ADR-022 made the configuration file mandatory and sole-source specifically so that
"the full effective layout is readable in one versioned file"; with four roots and seven
anchored sub-paths, a flat list no longer delivers that.

The mechanism is stock and needs no new infrastructure:
`Configuration.GetSection(...).Bind(options)` binds nested POCOs; environment variables
nest with a second `__`; and `AddCommandLine` switch mappings can target any key depth, so
`PathSwitchCatalog` changes only the `ConfigKey` strings it already owns.
`BuildLocation`/`DetermineSource` take the key suffix as a parameter
([GrimoirePathResolver.cs:214-222](../../backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathResolver.cs#L214-L222))
and work unchanged with `Data:Dir`-style suffixes.

**Alternatives considered**:

- *Keep the flat list and rely on the comment block*. Rejected. The comments are accurate
  today, but nothing checks them, and the anchor change above is exactly the drift they
  cannot catch. Structure the binder enforces beats a comment a reviewer must notice.
- *`Roots` + `SubPaths` sections with each sub-path naming its anchor as a value*
  (`"TasksDir": { "Anchor": "Memory", "Value": "tasks" }`). Rejected: it makes anchoring
  operator-supplied data and invites an anchor the resolver does not implement. Anchoring
  is a structural fact of the code — the file should mirror it, not parameterize it.
- *Prefixed flat keys* (`DataRawDir`, `MemoryTasksDir`). Rejected: hierarchy by naming
  convention, enforced by nothing, and worse environment-variable names than real nesting.
- *Write `Wiki` and `Agent` as bare strings* since neither has a configurable sub-path
  today. Rejected: it makes the four roots read inconsistently, and adding a sub-path later
  would be a breaking shape change rather than an added key.

**Consequences that need handling in tasks**:

1. **Every configuration key and environment variable is renamed.**
   `Grimoire__Paths__DataDir` → `Grimoire__Paths__Data__Dir`, and so on for all eleven. CLI
   switch names, `PathLocation` names and `paths_resolved` log fields are **unchanged** —
   `Memory:Dir` ↔ `--memory-dir` ↔ `memory_dir` stays consistent across all three surfaces.
2. **The rename would fail quietly, so it is detected instead (FR-014, added
   2026-08-11).** An unrecognized CLI switch is a parser error; an unrecognized
   configuration key is simply ignored. Left alone, an operator script still exporting
   `Grimoire__Paths__DataDir` would get the default with no warning — and for a feature
   whose purpose is deliberate placement of bookkeeping, quietly discarding a placement
   instruction is the worst available failure. The resolver therefore probes the bound
   configuration for all eleven superseded keys before the mandatory-root gate and fails
   naming each one with its replacement, emitting a new `paths_configuration_superseded`
   ERROR event and a new `reason=configuration_superseded` value on the existing failure
   counter.

   This does *not* contradict ADR-022's "no aliases, no detection, no replacement guidance"
   stance for removed switches. That stance rests on switches already failing loudly by
   themselves; configuration keys do not, so the same intent — the operator finds out
   immediately — requires the opposite mechanism. The old keys still do not work; they are
   merely reported. The legacy table holds key *names*, not default *values*, so ADR-022 R2
   and rule M2 are untouched, and it is scoped to this one rename — to be deleted rather
   than extended if the layout changes again.
3. **Live references to update** (historical spec documents are records and stay as they
   are): `PathSwitchCatalog.cs` (3 `ConfigKey` values, plus the new 4th),
   `EvalIndependenceFromHubConfigurationTests.cs:25-38` (an 11-entry env-var array),
   `PathPrecedenceTests.cs:116,219`, `CustomAgentDirEndToEndTests.cs:38,87,126`,
   `LintWikiDirEndToEndContentTests.cs:42`, `DefaultLayoutTests.cs:92,198`,
   `RemediationTasksPathTests.cs`, and `docs/operations/runtime-configuration.md:60-66`.
4. **Group properties are initialized, leaf values are not.**
   `public DataPathOptions Data { get; set; } = new();` prevents a
   `NullReferenceException` when a JSON group is absent entirely. Every leaf path property
   stays `string?` with no initializer, so ADR-022 R2 and ADR-023 M2 are untouched — worth
   stating explicitly, because `= new()` reads like a default at a glance and is not one.
5. **The missing-key error improves.** The mandatory-root gate reports full key paths
   (`Grimoire:Paths:Memory:Dir`) instead of bare field names (`MemoryDir`), so the message
   names something an operator can grep for verbatim. `paths_configuration_missing`'s
   `missing_keys` field carries the same full paths — a change in field *values*, not field
   names, and one the SC-004 test must assert.

**Why this earns two structural rules rather than a convention**: the grouping is only
worth doing if it stays true. M4 (Fast tier) keeps the options graph the same shape as the
JSON tree, so the file cannot drift back toward flatness one loose property at a time. M5
(Integration tier) makes the grouping mean what it says — for each group, relocating that
group's `Dir` must move every sub-path declared in it and nothing declared elsewhere,
driven by reflection over the options graph so newly added sub-paths are covered
automatically. M5 also subsumes the 4×4 root-independence matrix that SC-002 would
otherwise need as a separate test.
