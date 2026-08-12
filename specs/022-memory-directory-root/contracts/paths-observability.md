# Contract: Path-Resolution Observability

**Feature**: `022-memory-directory-root` | **Governs**: FR-006, FR-007, FR-008 |
**Verified by**: SC-004, SC-005, SC-006

This feature adds no new signal. It widens the mandatory-field set of one existing log
event and one existing span, and widens the trigger surface of three existing signals.
Each is a contract change and is treated as such: implementation, deterministic test, CI
gate.

All contract tests MUST obtain their signals from the production composition root
(`HubHostComposition`'s real telemetry registration, real sampler, real exporter pipeline).
Standing up a test-only `ActivitySource`, an always-on sampler, or a listener attached
directly to the instrumentation class is a false negative of exactly the kind Constitution
Principle IV rejects — the existing `PathConfiguration` contract tests already do this
correctly and the new assertions extend them in place.

---

## 1. Structured log events

Source: `backend/src/Grimoire.Hub/Runtime/Paths/GrimoirePathLogEvents.cs`.
Tests: `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathLoggingContractTests.cs`.

### 1.1 `paths_resolved` — EventId 40, level `Information`

Emitted exactly once per successful path resolution, from
`GrimoirePathResolver.Resolve` after validation and auto-creation.

| Field | Value | Status |
| --- | --- | --- |
| `data_dir` | resolved absolute path | existing |
| `wiki_dir` | resolved absolute path | existing |
| `agent_dir` | resolved absolute path | existing |
| **`memory_dir`** | **resolved absolute path** | **new mandatory field** |
| `secrets_file` | resolved absolute path | existing |
| `state_db` | resolved absolute path | existing |
| `raw_dir` | resolved absolute path (reports `RawOriginalsDir`) | existing, unchanged |
| `sources` | `"<name>=<source>, …"` over every `PathLocation` | existing — **gains a `memory_dir=<source>` pair** |

`<source>` ∈ `{command-line, environment, config-file}`. The `PathLocation` names in
`sources` (`data_dir`, `wiki_dir`, `agent_dir`, `memory_dir`, `tasks_dir`, …) are
**unchanged** by the configuration regrouping — only the underlying configuration keys that
`DetermineSource` looks up are nested. An operator reading a log line or a startup report
sees the same vocabulary as before.

Message template gains one placeholder, in root order (data, wiki, agent, memory) so the
rendered line stays readable:

```text
Runtime paths resolved. data_dir={data_dir} wiki_dir={wiki_dir} agent_dir={agent_dir}
memory_dir={memory_dir} secrets_file={secrets_file} state_db={state_db} raw_dir={raw_dir}
sources={sources}
```

**Test obligations**: assert the event name, `LogLevel.Information`, and the presence of
all eight fields including `memory_dir` with the expected resolved value. Assert the
`sources` string contains a `memory_dir=` pair. (SC-006)

### 1.2 `paths_location_created` — EventId 41, level `Information`

Emitted per writable location created because it was absent.

| Field | Value | Status |
| --- | --- | --- |
| `location` | logical name, e.g. `memory_dir` | existing |
| `resolved_path` | absolute path created | existing |

**Widened trigger**: now fires for `memory_dir` and for `tasks_dir`, `conversations_dir`,
`findings_dir`, `remediation_tasks_dir` at their new anchor.

**Test obligations**: on a cold start with no `memory/` on disk, assert an event with
`location=memory_dir` and that the directory exists afterwards. (SC-005, FR-007)

### 1.3 `paths_configuration_missing` — EventId 43, level `Error`

Emitted when configuration binding leaves one or more roots empty, immediately before
`GrimoirePathConfigurationMissingException` is thrown.

| Field | Value | Status |
| --- | --- | --- |
| `configuration_file` | `appsettings.json` | existing |
| `missing_keys` | comma-joined missing root keys | existing field — **two changes**: the memory root joins the checked set, and the values become **full key paths** (`Grimoire:Paths:Memory:Dir`) instead of bare field names (`MemoryDir`) |

The full-key-path change follows from the grouped configuration shape: under nesting, a
bare `Dir` would be ambiguous across four groups, and the full path is what an operator can
search for verbatim in the file. The field *name* is unchanged; only its values are.

**Test obligations**: two cases. (a) Bind a configuration with `Grimoire:Paths:Memory:Dir`
omitted; (b) bind one with the entire `Grimoire:Paths:Memory` group omitted. For each,
assert the event is emitted at `LogLevel.Error`, that `missing_keys` contains
`Grimoire:Paths:Memory:Dir`, and that the thrown
`GrimoirePathConfigurationMissingException.MissingKeys` contains the same value with its
message naming `appsettings.json`. Case (b) exists because the group-property initializer
makes it a distinct code path — without it the binder would produce a null group and an
NRE rather than this event. (SC-004, FR-006)

This test is also the behavioral backstop for ADR-024 rule M2: a code-level default for
the memory root anywhere in the solution would make it fail, regardless of the namespace
the literal lives in.

### 1.4 `paths_configuration_superseded` — **new**, level `Error`

Emitted when the bound configuration supplies one or more superseded flat keys, before the
mandatory-root gate runs, immediately preceding the thrown exception (FR-014/SC-010).

| Field | Value |
| --- | --- |
| `superseded_keys` | comma-joined old key paths found in the configuration |
| `replacements` | comma-joined `old → new` pairs, in the same order |

This is the **one genuinely new signal** in the feature; everything else in this contract is
a widening. Its implementation, deterministic test and CI rows are therefore required in
full rather than as extensions.

**Test obligations**: for each of the eleven superseded keys, supply it (via both the
configuration file and the environment-variable form) and assert the event is emitted at
`LogLevel.Error` with the key in `superseded_keys` and the correct replacement in
`replacements`; assert startup aborts rather than resolving the location to a default; and
assert `grimoire.hub.path_resolution_failures_total{reason="configuration_superseded"}` is
incremented once. A table-driven test over the key list keeps this proportionate.

---

## 2. Distributed trace spans

Source: `GrimoirePathLogEvents.StartLogEventSpan`.
Tests: `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathTracingContractTests.cs`.

### 2.1 `paths_resolved`

| Property | Value |
| --- | --- |
| Span name | `paths_resolved` |
| Parent | root — emitted during host composition, before any request activity exists |
| `signal_type` | `log` |
| `event_name` | `paths_resolved` |
| `level` | `Information` |
| `data_dir`, `wiki_dir`, `agent_dir`, **`memory_dir`**, `secrets_file`, `state_db`, `raw_dir`, `sources` | resolved values — `memory_dir` is a **new required attribute** |

`memory_dir` MUST be set inside the same span scope as the log call, so the log event and
the span carry identical values and stay correlated.

**Test obligations**: assert the span exists, is a root span in this composition (not the
child of an unsampled parent — the ADR-005/Principle IV failure mode), and carries
`memory_dir` with the expected value. (SC-006)

---

## 3. Business metrics

Source: `backend/src/Grimoire.Hub/HubMetrics.cs`.
Tests: `backend/tests/Grimoire.IntegrationTests/PathConfiguration/PathMetricsContractTests.cs`.

| Metric | Type | Labels | Change |
| --- | --- | --- | --- |
| `grimoire.hub.path_resolution_failures_total` | Counter | `reason` ∈ `configuration_missing`, `agent_directory_empty`, `location_invalid`, **`configuration_superseded`** (new) | **Widened trigger** plus **one new label value.** `reason=configuration_missing` now also fires for a missing memory root; `reason=location_invalid` fires if the resolved memory path is occupied by a file; `reason=configuration_superseded` is new and fires when a superseded flat key is supplied (FR-014). |

No new *metric* is introduced. The memory root is one more location inside an existing
resolution step; a dedicated `grimoire.hub.memory_dir_*` counter would answer no operator
question that the existing counter and its `reason` label do not already answer. The new
label value is the minimum needed to distinguish a genuinely different failure cause.

**Test obligations**: assert one increment with `reason=configuration_missing` on a start
missing the memory root, and one with `reason=configuration_superseded` on a start
supplying a superseded key.

---

## 4. CI enforcement

All tests named above live in `Grimoire.IntegrationTests`, which
`.github/workflows/ci.yml` already runs on every PR. The CI obligation for each row is
therefore a **confirmation** task, not a workflow edit: verify the new cases execute in the
standard PR pipeline. If a case turns out not to run there, the remedy is a workflow task —
not a waiver.
