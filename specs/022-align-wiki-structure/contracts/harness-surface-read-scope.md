# Contract: Operator-Controlled Harness-Surface Read Scope

**Feature**: 022-align-wiki-structure | **ADR**: ADR-023

## Configuration

```json
{
  "Grimoire": {
    "HarnessSurfaceReads": {
      "Tasks": false,
      "Conversations": false,
      "Findings": false,
      "RemediationTasks": false
    }
  }
}
```

- All four keys present in `appsettings.json` with `false`, so the posture is visible.
- Bound to `HarnessSurfaceReadOptions` in `HubHostComposition`, following
  `LintReviewWindowOptions`.
- Code-level default is `false` for each. Permitted: ADR-022's no-code-defaults rule (R2) is
  scoped to path roots and its tripwire forbids only the literals `.grimoire` and `llm-wiki`.
- Environment override: `Grimoire__HarnessSurfaceReads__Tasks=true`.
- **No command-line switch.**
- One grant set applies to every agent. There is no per-agent variant.
- `policy.json` is **not** modified — the grant set is the sole authority for harness surfaces.

## Delivery to the agent

One CLI argument per spawn, following the `--review-window-days` precedent, added at **all five**
`AgentProcessHost` spawn sites (ingest, query, lint, remediation execution, remediation message
turn). The value is the ordered list of granted surface names; empty means none granted.

The agent's composition maps the granted set to its complement within the reserved set and
constructs the `SafetyPolicy` with those subtrees denied.

## Evaluation

```text
Evaluate(target, isWrite: false):
    if target escapes the repository root      -> deny "traversal"
    if target is within a denied read subtree  -> deny "harness_surface_not_granted"   [NEW]
    for each read prefix:
        if prefix matches target               -> allow
    deny "no_rule"
```

- The denied-subtree check runs **before** the allow loop; ordering is fixed by ADR-023.
- Subtrees match directory-style, covering the subtree **and the bare directory itself**, so
  `list_files("tasks")` is denied and not only `read_file("tasks/x.md")`.
- Write evaluation is unchanged.
- `SafetyPolicy` receives plain strings and stays dependency-free.

## Denial behaviour

A denied read flows through the existing `RecordDenial` funnel unchanged:

1. A `DeniedActionRecord(Action, RequestedTarget, CanonicalTarget, "harness_surface_not_granted", Turn)`
   is appended to the run's denial list.
2. Denial instrumentation fires, plus the new surface-labelled counter.
3. The tool returns an `is_error` result reading
   `denied: harness_surface_not_granted. This action is outside the safety policy; continue with your remaining allowed work.`
4. **The run continues.** No exception, no terminal state change.

## What is unaffected

- **Hub-side reads.** ADR-014's conversation-context assembly and ADR-018's remediation
  message-turn context are Hub filesystem reads that inject content into the agent's prompt.
  They are not guarded tool calls and are not governed by this scope. An agent can therefore
  receive remediation context it may not itself read — intentional, and the reason denying
  `remediation-tasks/` does not break message-turn mode.
- **The agent's own artifact writes.** Ingest writes its task artifact through
  `TaskArtifactStore`, a permitted writer outside the guarded tool layer per the existing
  `IngestAgentGuardedWriteBoundaryRuleTests` exemption. Denying reads of `tasks/` does not
  affect it.
- **Policy identity.** `policy.json` is unchanged, so its version and SHA-256 are unchanged and
  no eval recording goes stale from a grant flip.

## Provenance

Each run records its effective grant set alongside the existing `policy:` block:

| Record | Field | Shape |
|--------|-------|-------|
| Task artifact frontmatter | `granted_harness_surfaces` | `["tasks"]` or `[]`, same serialiser as `articles_touched` |
| Terminal NDJSON `completed` event | `grantedHarnessSurfaces` | list of strings |
| Conversation record bookkeeping | `granted_harness_surfaces` | flat block list, same shape as `created_articles` |

An operator reading any one of these can reconstruct exactly what the agent was permitted to
read (FR-017, SC-011). This is mandatory rather than optional precisely because the effective
scope is no longer derivable from the policy hash alone.

## Structural rules (ADR-023)

- **H1**: `Grimoire.Domain` contains no reference to a configuration or options type. Red/Green
  probe: introduce an `IOptions<>` reference into `Grimoire.Domain.Guardrails`, verify the rule
  fails and names the file, remove it.
- **H2**: the four reserved surface names are declared in exactly one place
  (`ReservedHarnessSurfaces`), and the denied-subtree derivation reads them from there rather
  than repeating literals.
