---
status: accepted
---

# ADR-023: Operator-Controlled Read Scope over Reserved Harness Surfaces

## Context and Problem Statement

ADR-022 moved four harness-owned record directories — `tasks/`, `conversations/`,
`findings/`, `remediation-tasks/` — out of the git-ignored data directory and into the
wiki content root, and explicitly accepted the visibility consequence it could foresee:

> **Bad / deliberate**: conversations, findings, and remediation-task records move from
> the git-ignored data directory into the wiki directory (spec clarification 2026-08-06 —
> they are agent output). This reverses the ADR-003/ADR-009 placement of that bookkeeping
> as internal, git-ignored state: an operator who version-controls the wiki will now see
> it.

It considered *operator* visibility. It did not consider *agent* visibility, and that is
the gap this ADR closes.

Every shipped policy grants the whole content root on read. All three of
`backend/src/Grimoire.{Ingest,Query,Lint}Agent/Instructions/policy.json` carry:

```json
"read": [ { "pathPrefix": "index.md" }, { "pathPrefix": "log.md" }, { "pathPrefix": "." } ]
```

`PolicyLoader.NormalizeRulePrefix` turns `"."` into the wiki root with a trailing
separator, which `SafetyPolicy.PrefixMatches` resolves via its `StartsWith` branch — the
root and everything beneath it, recursively. Combined with ADR-022's relocation, **every
agent today may read every task artifact, every finding, every remediation record, and
every stored conversation transcript of every other session.** No configuration expresses
an alternative, and no ADR ever decided that this should be so; it is the emergent product
of two independently reasonable decisions.

Feature 022 (`specs/022-align-wiki-structure/spec.md`, FR-014–FR-018) makes this an
operator's decision rather than an accident. The operator chooses, per surface, whether
agents may read it; the harness defaults to denying. The spec's clarification records the
reasoning:

> An operator who reads all of that data themselves may reasonably decide their agents
> should too, so the harness must permit that choice rather than forbid it.

Two structural obstacles stand in the way, and neither is covered by an existing ADR.

**First, the read scope has no subtractive concept.** `SafetyPolicy` holds
`IReadOnlyList<string> _readPrefixes` — bare strings, no mode, no exclusions. `Evaluate`'s
read branch is a first-match-wins allow loop returning `no_rule` when nothing matches.
`ExcludePrefixes` exists only on `WriteRule`, is reached only from the write branch, and is
exact-match equality rather than prefix matching, so it cannot exclude a subtree even if it
were consulted. The type's own documentation states the assumption plainly:

> the policy schema is allow-list-only with first-match-wins and no deny-rule concept

Expressing "the content root except `tasks/`" is not possible in the current model, and it
cannot be worked around by enumerating what *is* allowed: category folders are agent-created
and open-ended by design (feature 014), so no fixed allow-list can name them.

**Second, `policy.json` is not an operator surface.** ADR-022 made the instruction files
agent-project sources distributed to the agent directory by the build:

> Every build refreshes the destination, so hand edits in the output are overwritten by
> design — durable instruction changes are made in the agent's sources

and

> The hub **never writes** anything under the agent directory.

An operator who edits the deployed `policy.json` loses the edit on the next build. The file
is a developer-owned, version-controlled artifact. FR-014 requires that "the operator owns
this decision," and no accepted ADR says where an operator-owned guardrail input lives.

There is a third, quieter problem. ADR-006 fixed policy provenance as
"policy identity (version + SHA-256) is recorded in every task artifact." That hash covers
the policy *file*. A second authority over the same guardrail decision, living outside the
file, would change what the agent was permitted to read without changing the recorded hash —
leaving an auditor unable to reconstruct the effective scope of a completed run. FR-017 and
SC-011 require exactly that reconstruction.

## Decision Drivers

- FR-014: the operator owns the decision; the harness must not hard-code either answer.
- FR-015: default deny — an installation that has configured nothing exposes only wiki
  content to its agents.
- FR-016: a denied read is denied at the guarded tool boundary, recorded with a reason, and
  the run continues — Constitution Principle V's guardrail contract.
- FR-017 / SC-011: the effective read scope of a run must be reconstructable from the run's
  own record.
- The grant applies uniformly to every agent (spec clarification, 2026-08-09) — a design
  that requires the same decision to be repeated per agent invites the drift it forbids.
- Constitution Principle I: no new external system, therefore no new port; but adapter
  containment and the dependency-free Domain Core still bind.
- ADR-022's configuration-surface cap: the CLI path-switch surface is closed at three and
  structurally enforced.

## Considered Options

1. **Read-side exclusions in `policy.json`, operator-edited.** Add `excludePrefixes` to the
   read schema and let the operator edit the deployed file.
2. **A new `Grimoire:` configuration section, enforced as a runtime narrowing of the loaded
   policy.** The Hub owns the grant set, threads it to each agent at spawn, and the agent
   applies it as a subtractive narrowing over the policy it loaded from disk.
3. **Hub-side filtering of tool results.** Let the agent's read succeed and strip
   harness-surface content from the result before it reaches the model.
4. **Separate the surfaces from the content root entirely.** Move `tasks/` and friends back
   out of the wiki directory, reverting ADR-022's placement, so no read scope needs
   narrowing.

## Decision Outcome

**Chosen: option 2** — a new top-level `Grimoire:HarnessSurfaceReads` configuration section,
bound in the Hub, threaded to every agent at spawn, and applied as a subtractive narrowing of
the loaded `SafetyPolicy` with its own denial reason.

### The grant set

A `HarnessSurfaceReadOptions` record with `SectionName = "Grimoire:HarnessSurfaceReads"` and
one `bool` property per reserved surface — `Tasks`, `Conversations`, `Findings`,
`RemediationTasks` — each defaulting to `false`. The four keys are written explicitly into
`appsettings.json` with `false` values, so the effective posture is readable in the one
versioned file ADR-022 established as the place an operator looks.

This follows the established shape for operator-controlled non-path settings:
`QueryConcurrencyOptions` (ADR-011) and `LintReviewWindowOptions` (feature 013) both bind a
top-level `Grimoire:` key with a code-level default. ADR-022's no-code-defaults rule (R2) is
scoped to *path* roots — its tripwire forbids IL literals equal to `.grimoire` or
`llm-wiki` — and a boolean default trips neither it nor the three-switch cap (R1). The
setting takes **no** command-line switch.

It does not go under `Grimoire:Paths`. That section is the single composition point for
runtime *locations*, resolved by `GrimoirePathResolver` into `ResolvedGrimoirePaths`; a
grant set is not a location and would corrupt that invariant.

### Precedence: one authority, not two

The shipped `policy.json` files are **not** changed by this ADR. They continue to grant the
content root on read. The grant set is the sole authority over harness-surface reads, applied
as a narrowing after the policy loads. This is deliberate: a two-source design (deny in the
file, widen by configuration) would need a precedence rule, and would couple an operator's
boolean to the policy SHA-256 — invalidating the entire eval recording corpus on every flip,
since ADR-012 fingerprints `policy.json` as a staleness input.

### Enforcement: subtractive read scope in the Domain

`SafetyPolicy` gains a set of denied read subtrees, checked in the read branch of `Evaluate`
*before* the allow loop, returning the new denial reason `harness_surface_not_granted`. The
model for this is the existing `WithNoWriteAccess()`, whose documentation already frames the
distinction this ADR depends on:

> the same loaded policy identity (version/sha256) still describes what was read from disk;
> this method only changes what the in-memory `SafetyPolicy` instance enforces for that one
> run.

The subtrees are matched as directories *including the bare directory itself*, so
`list_files("tasks")` is denied and not only `read_file("tasks/x.md")`.

`SafetyPolicy` stays dependency-free and pure: it receives plain strings, never an options
type, a configuration abstraction, or a path resolver. The mapping from grant booleans to
denied subtree paths happens in the agent's composition, not in the Domain.

A distinct denial reason — rather than reusing `no_rule` — is required by SC-010: the
operator must be able to distinguish "you have not granted this" from "this is outside the
policy," and the reason string is echoed to the agent in the tool result.

### Delivery to the agent

One CLI argument per spawn, following the `--review-window-days` precedent
(`AgentProcessHost` → agent CLI options → agent composition). ADR-002 fixes CLI arguments as
the spawn contract, and ADR-022's switch cap governs *path* switches only. The argument must
be added at all five spawn sites, not only the one under active development.

### Provenance

The effective grant set is recorded on each run alongside the existing policy identity, in
whichever record that run produces: the task artifact frontmatter for Ingest, the terminal
`completed` event and the conversation record for Query, the terminal event for Lint. ADR-008's
event vocabulary and ADR-014's open bookkeeping mapping both tolerate new keys, so this adds a
field without restructuring.

### Consequences

- Good, because the operator's decision is expressed once, in the file ADR-022 made the home
  for layout configuration, and applies uniformly to every agent with nothing to keep in sync.
- Good, because deny-by-default means an installation that has configured nothing is in the
  safe posture, and the posture is visible in `appsettings.json` rather than implied by absence.
- Good, because the denial rides the existing guarded-tool funnel — `RecordDenial` already
  produces a `DeniedActionRecord`, emits the denial instrumentation, and returns a tool result
  telling the agent to continue — so FR-016's "run continues" is satisfied structurally with
  no new control flow.
- Good, because `policy.json` is untouched, so no eval recording goes stale from flipping a
  grant.
- Bad, because the effective read scope is now determined by two inputs (the policy file and
  the configuration) rather than one, which is precisely why the provenance record is
  mandatory rather than optional. An auditor who reads only the policy SHA-256 will have an
  incomplete picture; the recorded grant set is what closes that gap.
- Bad, because a subtractive rule is the first deny concept in an allow-list-only model, and
  future policy work must now reason about ordering between denial and allowance. The
  ordering is fixed here — denial is checked first — and the structural rule below pins it.
- Neutral, because the Hub's own reads of these directories are unaffected: ADR-014's
  conversation-context assembly and ADR-018's remediation message-turn context are Hub-side
  filesystem reads that inject content into the prompt, not guarded agent tool calls. An
  agent can therefore still receive remediation context it may not itself read. This
  asymmetry is intentional — the harness decides what to put in front of the agent; the
  guarded boundary governs what the agent may reach on its own — and it is the reason
  denying `remediation-tasks/` does not break ADR-018.

### Structural Enforcement (Constitution Principle III)

Rule **H1**: no production code outside the agent composition layer constructs a
`SafetyPolicy` with an empty denied-read set when a grant set is available, and
`Grimoire.Domain` contains no reference to a configuration or options type. Enforced by a
`Grimoire.ArchTests` rule with a Red/Green probe: introduce a deliberate `IOptions<>`
reference into `Grimoire.Domain.Guardrails`, verify the rule fails and names the file, then
remove it.

Rule **H2**: the four reserved surface names are declared in exactly one place, and the
denied-subtree derivation reads them from there rather than repeating string literals.
Enforced by the same rule class, mirroring ADR-022's R2 tripwire idiom.

## More Information

Supersedes nothing. Extends ADR-006 (guarded tool boundary — adds the first read-side
scoping concept), ADR-022 (configuration surface — adds the first non-path operator setting
that governs a guardrail), and closes the agent-visibility gap that ADR-022's relocation of
the four record directories opened.

Related: ADR-015 and ADR-016 are the precedent for authorizing a policy-schema extension by
ADR — each required one for a strictly smaller change on the write side. ADR-012's
fingerprinting is the reason this ADR keeps `policy.json` unchanged. ADR-018's Hub-injected
remediation context is the asymmetry noted above.
