# Feature Specification: Unified Agent Platform & Naming Convention

**Feature Branch**: `010-unified-agent-platform`

**Created**: 2026-07-27

**Status**: Implemented — completed 2026-07-30

**Input**: User description: "IngestAgent and QueryAgent could actually be the same
code, just with different system prompts. The functionality of the agents is
identical, just the guardrails and their intent may differ. All agent assemblies
should be nearly identically implemented or share the same agent runtime project.
The same goes for tests: the first agent's tests are just called ReplayEvalTests,
but for query it is QueryReplayEvalTests — I want to know directly which agent a
test file belongs to, especially with the next agent (lint) coming, which is also
the same as ingest and query but with different intent."

## Terminology

- **Agent**: One LLM-driven worker with a distinct intent operating on the wiki.
  Today: Ingest (fills the wiki) and Query (answers from the wiki); next: Lint
  (checks wiki health). All agents are dispatched, supervised, and constrained by
  the same harness.
- **Agent Profile**: The complete set of artifacts that make one agent *itself* and
  distinguish it from every other agent: its identity (name), its System Prompt
  Document, its tool set (which capabilities it may use), and its guardrail policy.
  Everything not in the profile is, by definition, shared platform behavior.
- **Agent Platform**: The shared machinery every agent runs on — the model
  interaction loop, guarded tool enforcement, instruction loading, run event
  emission, and telemetry setup. One implementation, used by all agents.
- **Naming Convention**: The written rule that maps a code artifact's name to its
  owner: agent-specific artifacts carry their agent's name; only genuinely
  cross-agent artifacts are unprefixed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Two agents, one platform (Priority: P1)

A developer compares the Ingest and Query agents and finds that everything they
share in behavior is shared in code: one platform provides the model loop, guarded
tool enforcement, instruction loading, run events, and telemetry setup. What
remains per agent is exactly its profile — identity, system prompt, tool set, and
policy. Nothing that both agents need exists twice; a fix or improvement to shared
behavior lands in one place and both agents get it.

**Why this priority**: This is the core of the feature. Duplication between the two
existing agents is already causing drift (diverging telemetry setup, diverging
naming), and every future agent multiplies the cost. Consolidation must land before
the Lint agent is built.

**Independent Test**: Enumerate the per-agent code of Ingest and Query. Verify the
difference between them consists only of profile artifacts (identity, system
prompt, tool set, policy) plus intent-specific artifact handling. Verify no shared
concern (loop, guardrails, instruction loading, events, telemetry setup) is
implemented more than once.

**Acceptance Scenarios**:

1. **Given** the consolidated platform, **When** the per-agent code of Ingest and
   Query is compared, **Then** each agent's own code is limited to its Agent
   Profile and intent-specific artifact handling, and every shared concern exists
   exactly once in the platform.
2. **Given** a defect fixed or behavior improved in a shared concern, **When** the
   fix lands, **Then** it takes effect for all agents without per-agent code
   changes.
3. **Given** the consolidated platform, **When** a structural rule scans for
   duplicate implementations of platform concerns across agents, **Then** it finds
   none — and this rule is proven live with a Red/Green probe.

---

### User Story 2 - A file's name tells you whose it is (Priority: P2)

A developer opens the test tree (or any part of the codebase) and can tell from
every artifact's name which agent it belongs to. Agent-specific tests, evaluation
suites, namespaces, and instruction folders carry their agent's name; unprefixed
names are reserved for genuinely cross-agent harness code. Existing artifacts that
violate this (named when Ingest was the only agent — e.g. the unprefixed replay
evaluation suite that is actually Ingest-only, next to Query's prefixed sibling)
are renamed. The convention is written down and enforced automatically, so the
third agent cannot re-introduce the ambiguity.

**Why this priority**: The ambiguity is already biting and gets worse with each
agent. It is cheap to fix now and expensive later, but it does not change runtime
behavior — hence P2.

**Independent Test**: List all agent-specific code artifacts and verify each
carries its agent's name; list all unprefixed artifacts and verify each is
genuinely cross-agent. Verify the convention document exists and the automated
check fails when a deliberately misnamed artifact is introduced.

**Acceptance Scenarios**:

1. **Given** the renamed codebase, **When** any agent-specific test file,
   evaluation suite, namespace, or instruction folder is inspected, **Then** its
   name contains its agent's name.
2. **Given** the naming convention, **When** it is consulted, **Then** it exists as
   a written, versioned document stating the rule, its rationale, and the old→new
   mapping of all renames performed.
3. **Given** the automated naming check, **When** a deliberately misnamed
   agent-specific artifact is introduced, **Then** the check fails the build — and
   the deliberate violation is removed afterward (Red/Green probe).
4. **Given** a genuinely cross-agent artifact, **When** the convention is applied,
   **Then** it remains unprefixed — the convention distinguishes ownership, it does
   not blanket-prefix everything.

---

### User Story 3 - Nothing observable changes (Priority: P3)

An operator uses ingest and query exactly as before the consolidation: same
surfaces, same behavior, same artifacts, same events, same guarantees. The
consolidation is invisible at every boundary that anything or anyone outside the
codebase touches.

**Why this priority**: The consolidation only pays off if it is a pure
restructuring. A behavior regression would turn a cleanup into an incident. It is
P3 because it is a constraint on how Stories 1–2 are done, not separate new value.

**Independent Test**: Run the full existing verification suite (integration,
structural, evaluation) after consolidation and verify it passes without weakening
any assertion; compare produced artifacts and emitted events before and after for
identical shape.

**Acceptance Scenarios**:

1. **Given** the consolidated platform, **When** the full pre-existing test suite
   runs (with only naming updated per Story 2), **Then** every test passes without
   any assertion being weakened or removed.
2. **Given** an ingest run and a query turn on the consolidated platform, **When**
   their artifacts and emitted events are compared to pre-consolidation runs,
   **Then** they are identical in structure and content semantics.
3. **Given** the Query agent on the consolidated platform, **When** its
   capabilities are enumerated, **Then** they are exactly its profile's read-only
   tool set — consolidation does not widen (or narrow) any agent's capabilities,
   and the existing structural guarantee that query-path code cannot write remains
   enforced with its probe.

---

### Edge Cases

- What happens when a shared-platform change is correct for one agent but wrong for
  another? The per-agent behavior suites (Story 3) catch the regression for the
  affected agent; behavior that legitimately differs per agent must live in the
  Agent Profile, not in agent-conditionals inside the platform.
- What happens when an artifact serves two agents but not all (e.g. a fixture used
  by ingest and query tests)? It is cross-agent by the convention's definition and
  stays unprefixed; the convention document must state this explicitly.
- What happens to in-flight work on parallel feature branches during the renames?
  The convention document's old→new mapping lets parallel branches rebase
  mechanically.
- What happens when a future agent needs a capability the platform does not offer
  (e.g. a new tool type)? The platform gains the capability once, and only profiles
  that declare it receive it; agents that do not declare it are unaffected.
- What happens to the established per-agent observability identities (metric
  names, event names, span names)? They are observable behavior and remain
  unchanged (Story 3); only their duplicated setup code is consolidated.

## Requirements *(mandatory)*

### Functional Requirements

**One platform**

- **FR-001**: All agents MUST run on a single shared agent platform providing the
  model interaction loop, guarded tool enforcement, instruction loading, run event
  emission, and telemetry setup. No platform concern may be implemented separately
  per agent.
- **FR-002**: Per-agent code MUST be limited to the Agent Profile — identity,
  System Prompt Document, tool set, guardrail policy — plus the artifact handling
  specific to that agent's intent. Behavior that differs between agents MUST be
  expressed in profile artifacts, not as agent-conditional logic inside the
  platform.
- **FR-003**: Adding a new agent MUST require only a new Agent Profile and its
  instruction files, plus the harness-side dispatch surface for its intent — no
  duplication of any platform concern. (The Lint agent, feature 013, is the first
  consumer and the practical proof.)
- **FR-004**: The consolidation MUST NOT change any agent's effective capabilities:
  each agent's tool set remains exactly what its profile declares, enforced at the
  guarded tool boundary at invocation time, and the existing structural guarantee
  that query-path agent code performs no wiki writes remains enforced (with its
  Red/Green probe) for as long as the Query agent's profile declares no write
  capability.

**Naming convention**

- **FR-005**: A written, versioned naming convention MUST exist stating: every
  agent-specific code artifact (test files, evaluation suites, namespaces,
  instruction folders, per-agent components) carries its agent's name; unprefixed
  names are reserved for cross-agent artifacts. It MUST include the old→new
  mapping of all renames performed by this feature.
- **FR-006**: All existing artifacts violating the convention MUST be renamed —
  including the unprefixed Ingest-only replay evaluation suite whose Query sibling
  is prefixed — so that after this feature, name alone identifies ownership.
- **FR-007**: The naming convention MUST be enforced by an automated check that
  fails the build on violation, proven live by introducing a deliberately misnamed
  artifact, observing the failure, and removing it (Red/Green probe).

**Behavior preservation**

- **FR-008**: All externally observable behavior of Ingest and Query MUST be
  unchanged: surfaces, validation, artifact structure and location, event
  semantics, guardrail decisions, supervision behavior, and observability
  identities (metric/event/span names and mandatory fields).
- **FR-009**: The full pre-existing verification suite MUST pass after
  consolidation with no assertion weakened; changes to test code are limited to
  the renames of FR-006 and mechanical updates that follow from the
  consolidation.
- **FR-010**: Existing structural architecture rules (dependency direction,
  adapter containment, guarded-write boundary) MUST be carried over to the
  consolidated structure and remain enforced with live probes; where the
  consolidation moves a boundary, the rule moves with it rather than being
  dropped.

### Key Entities

- **Agent Profile**: The per-agent artifact set: identity, System Prompt Document,
  tool set declaration, guardrail policy. The unit that fully distinguishes one
  agent from another.
- **Agent Platform**: The single shared implementation of all cross-agent
  machinery. Has no knowledge of any specific agent's intent.
- **Naming Convention Document**: Versioned statement of the ownership-naming
  rule, its rationale, and the rename mapping.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Deterministic guarantees (100%)**

- **SC-001**: 100% of platform concerns (model loop, guarded tool enforcement,
  instruction loading, run events, telemetry setup) exist as exactly one
  implementation; a structural check proving absence of duplicates passes with a
  verified Red/Green probe.
- **SC-002**: 100% of agent-specific code artifacts follow the naming convention,
  enforced by an automated check in the standard build pipeline with a verified
  Red/Green probe.
- **SC-003**: 100% of the pre-existing verification suite passes after
  consolidation with no weakened assertions; ingest and query artifacts and events
  are structurally identical to pre-consolidation output.
- **SC-004**: 100% of each agent's effective tool capabilities match its declared
  profile; the Query agent's no-write structural guarantee remains enforced and
  probe-verified.
- **SC-005**: The change set introducing the next agent (feature 013) adds zero
  duplicated platform code — measured when that feature lands, as the practical
  proof of FR-003.

*(This feature is a pure harness restructuring: no agent-judgment success criteria
apply. Agent behavior is deliberately unchanged and covered by the existing
evaluation suites re-run under SC-003.)*

## Assumptions

- **This feature revisits an accepted architectural decision**: feature 008
  deliberately gave Query a separate agent process so that the absence of write
  capability was structural, not merely configured. Whether "one platform" means
  one shared library under near-identical thin per-agent hosts, or a single
  parameterized host process, is a planning decision that MUST be settled by a
  superseding architecture decision record during planning — the more so because
  feature 012 (query synthesis writes) will remove the premise that Query never
  writes. This spec requires uniformity and capability fidelity
  (FR-001/FR-002/FR-004); it does not prescribe the packaging.
- **Sequencing**: This feature is the foundation for features 011–013 and is
  expected to merge first; parallel feature branches rebase using the rename
  mapping (FR-005).
- **Scope boundary**: Frontend code and Hub endpoint surfaces are out of scope
  except where the naming convention applies to agent-specific artifacts; no
  user-facing behavior changes.
- **Observability identities are frozen**: Established metric/event/span names are
  treated as public contract and survive the consolidation unchanged; only their
  setup code is unified.
- **Single-user context**: Unchanged from prior features; no auth or multi-user
  separation is introduced.
