# Feature Specification: Recorded-Replay Agent Evaluations

**Feature Branch**: `claude/speckit-agent-eval-alternatives-yhik4p`

**Created**: 2026-07-23

**Status**: Draft

**Input**: User description: "der agent eval test ist ziemlich aufwendig. er testet llm antworten und ist damit auch noch teuer. wir brauchen eine alternative, den agenten zu testen. evtl. könnten wir snapshots der antworten speichern, um damit einen agent durchlauf zu simulieren? oder wir entfernen die eval tests komplett und testen den deterministischen teil der anwendung"

## Clarifications

### Session 2026-07-23

- Q: How should eval runs be executed going forward — as tests, as a standalone command ("remote control"), or hybrid? → A: Hybrid — live evals (recording and on-demand full runs) move out of the test suite into a dedicated, standalone eval command; replay remains an always-running deterministic test in the standard suite. The standard test suite contains no skipped eval tests anymore.
- Q: How does the eval command execute the system — an in-process rebuild of the agent loop (as today) or from outside against the real application in an isolated environment? → A: The eval command drives the real application through its production entry points, in an isolated per-run workspace — no rebuilt/duplicated wiring. A fully external black-box deployment is out of scope.
- Q: How strictly are changes to agent instruction files governed? → A: Manual instruction edits remain allowed, but a change to instruction files can only merge with fresh eval evidence: recordings captured after the change, meeting the spec-defined thresholds. Staleness becomes a merge gate. Restricting instruction changes to an evaluating agent/system is a separate future feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Verify agent behavior via replay at zero provider cost (Priority: P1)

A developer wants to verify that the agent-behavior evaluation suite still passes — for example after a harness refactoring or before opening a pull request — without paying for live model calls and without configuring any provider credential. They run the eval suite in replay mode: instead of calling a model provider, each eval scenario is driven by a stored recording of a genuine earlier agent run. The existing scoring logic evaluates the recorded agent behavior against the same spec-defined thresholds, and the run reports pass/fail results within minutes.

**Why this priority**: This is the core value of the feature. Today every eval execution burns provider spend and wall-clock time, so evals are run rarely and late. A free, fast replay tier lets developers verify agent-facing behavior routinely — while staying compliant with the project's testing principles, which explicitly allow evaluation against recorded model output.

**Independent Test**: Can be fully tested by removing all provider credentials from the environment, running the eval suite in replay mode against existing recordings, and observing that all evals execute, produce scored results, and make zero outbound model calls.

**Acceptance Scenarios**:

1. **Given** recordings exist for every eval scenario, **When** the developer runs the eval suite in replay mode, **Then** all eval tests execute against the recordings and report scores against the spec-defined thresholds, with zero calls to any model provider.
2. **Given** no provider credential or endpoint is configured anywhere, **When** the eval suite runs in replay mode, **Then** it still executes completely — replay requires no provider configuration.
3. **Given** the same recording set, **When** the replay suite is executed twice, **Then** both runs produce identical scores and identical pass/fail outcomes.
4. **Given** a recording is missing for one eval scenario, **When** the replay suite runs, **Then** that eval reports an actionable "recording missing" outcome that names how to produce the recording — it is clearly distinguished from an agent-judgment failure.

---

### User Story 2 - Capture and refresh recordings from genuine live runs (Priority: P2)

A developer (or maintainer in CI) wants to create or refresh the recordings that replay mode consumes. They explicitly start a recording run via the standalone eval command: the eval scenarios execute against a real model provider exactly as today, and every model interaction of every sample is captured as a recording. Each recording carries the identity of the model that produced it, the capture date, and a fingerprint of the agent instruction files and eval scenario it was captured against. Recordings are stored versioned alongside the eval suite so every contributor replays the same, reviewable state.

**Why this priority**: Replay is only trustworthy if recordings are genuine, attributable, and refreshable. Without a defined capture path the replay tier would decay into unverifiable fixtures — which the project's principles prohibit (agent judgment must not be replaced by hand-authored deterministic data).

**Independent Test**: Can be tested by running a recording run against a configured provider, verifying that a recording with complete provenance metadata exists for every executed sample afterward, and that a subsequent replay run consumes exactly those recordings.

**Acceptance Scenarios**:

1. **Given** a configured live provider (per the existing affordable-provider or reference-provider setup), **When** a developer starts an explicit recording run, **Then** every executed eval sample produces a stored recording carrying model identity, capture timestamp, and fingerprints of the instruction files and scenario it was captured against.
2. **Given** recordings already exist, **When** a recording run is repeated, **Then** the affected recordings are replaced by the fresh captures — old and new recordings are never silently mixed within one scenario.
3. **Given** a developer runs the suite in replay mode, **When** any recording is missing or unusable, **Then** the suite never falls back to live provider calls on its own — recording happens only on explicit request.
4. **Given** a recording run completes, **When** the recordings are inspected, **Then** they contain no credential material.

---

### User Story 3 - Trust replay results through staleness detection (Priority: P3)

A developer changes an agent instruction file (the versioned files that govern agent behavior) and later runs the replay suite. Because the recordings were captured under the old instructions, replaying them can no longer prove anything about the new behavior. The replay run detects this and flags every affected eval as stale — naming the recordings that need a refresh — instead of reporting a misleading green result.

**Why this priority**: Staleness detection is what makes the replay tier honest. Without it, replay would keep validating outdated behavior forever and the team would lose the signal that a live re-recording (and thus a real evaluation of the changed instructions) is due.

**Independent Test**: Can be tested by capturing recordings, modifying an instruction file, running replay mode, and verifying the affected evals are reported stale rather than passed.

**Acceptance Scenarios**:

1. **Given** recordings captured under the current instruction files, **When** the replay suite runs, **Then** results are reported as trusted and count as passing.
2. **Given** an instruction file or an eval scenario changed after capture, **When** the replay suite runs, **Then** every affected eval is flagged stale, the flagged result does not count as a trusted pass, and the output names which recordings must be refreshed.
3. **Given** stale recordings are flagged, **When** the developer performs a recording run for the affected scenarios, **Then** a subsequent replay reports those evals as trusted again.

---

### Edge Cases

- What happens when a harness change alters the conversation the agent loop would conduct (for example different tool wiring), so the recorded interaction no longer matches what the harness replays? The replay run must surface this as an actionable replay-mismatch outcome — clearly distinct from an agent-judgment failure — rather than producing meaningless scores.
- What happens when someone hand-edits a recording to make a failing eval pass? Recordings are captured artifacts with provenance metadata; a recording whose content does not match its recorded fingerprints is treated as unusable, not silently replayed.
- What happens when a new eval scenario is added before any recording exists for it? Replay reports "recording missing" with the capture instruction; the scenario's first trusted result requires one live recording run.
- What happens when only a subset of recordings is stale? Unaffected evals still replay as trusted; only the affected ones are flagged, so a partial refresh is sufficient.
- What happens to live evaluation? It remains fully available (per the existing affordable-provider setup) for recording runs and on-demand full runs; replay complements it, it does not remove it.
- What happens if recordings grow large over time? Each scenario keeps only its current recording set (superseded captures are replaced), bounding growth to the size of the active suite.
- What happens when a pull request changes an instruction file but ships no refreshed recordings? The staleness gate blocks the merge and names the affected recordings and the eval-command invocation that refreshes them — the developer records locally (or triggers the on-demand CI eval) and commits the fresh recordings.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The agent-behavior evaluation suite MUST offer a replay mode in which every eval scenario executes against stored recordings of genuine earlier agent runs, applies the unchanged spec-defined scoring and thresholds, and makes zero calls to any model provider.
- **FR-002**: Replay mode MUST require no provider credential or endpoint configuration and MUST be runnable in the standard PR pipeline without any secrets, satisfying the hermetic-test rules for that pipeline.
- **FR-003**: Replay of an unchanged recording set MUST be deterministic: repeated replays produce identical scores and identical pass/fail outcomes.
- **FR-004**: Recordings MUST originate exclusively from genuine model output captured during an explicit recording run against a real provider. Hand-authored or hand-modified recordings MUST NOT be accepted as trusted replay input.
- **FR-005**: Recording MUST happen only on explicit request via the eval command; replay mode MUST never fall back to live provider calls on its own.
- **FR-006**: Every recording MUST carry provenance metadata: the identity of the model that produced it, the capture timestamp, and fingerprints of the agent instruction files and the eval scenario it was captured against.
- **FR-007**: Recordings MUST be stored versioned alongside the eval suite so that all contributors and CI replay the identical recording state, and recording changes are reviewable.
- **FR-008**: The replay run MUST detect staleness: when the current instruction files or eval scenario no longer match a recording's fingerprints, every affected eval MUST be flagged stale, MUST NOT count as a trusted pass, and the output MUST name the recordings requiring refresh.
- **FR-009**: A missing or unusable recording MUST produce an actionable outcome that names the capture step — clearly distinguished from an agent-judgment failure and from a staleness flag.
- **FR-010**: When the interaction the harness conducts during replay diverges from the recorded interaction, the affected eval MUST fail with an actionable replay-mismatch outcome, not with misleading judgment scores.
- **FR-011**: Recordings and replay outputs MUST NOT contain credential material.
- **FR-012**: Every replay result MUST name its provenance: which recording it used, the model that originally produced it, and the capture date — so replayed results are never confused with live results.
- **FR-013**: Live evaluation (recording runs and on-demand full live runs) MUST be provided by a dedicated, standalone eval command — a "remote control" outside the test suite — reusing the existing provider configuration (affordable-provider setup and its on-demand CI workflow). After this feature, the standard test suite MUST contain no eval tests that skip for lack of provider configuration; replay evals run unconditionally, live evals live only in the eval command.
- **FR-014**: The spec-defined agent-judgment thresholds MUST remain unchanged by this feature; replay changes how often live model calls are needed, not what agent behavior must achieve.
- **FR-015**: The eval command MUST execute the system under evaluation through the application's real production entry points, in an isolated per-run workspace (own wiki fixture copy, own working directories) — it MUST NOT maintain a separate, rebuilt copy of the application's orchestration wiring. Evaluating a fully deployed system as an external black box is out of scope.
- **FR-016**: A change to agent instruction files MUST NOT merge without fresh eval evidence: the recordings affected by the change MUST have been re-captured after the change (via the eval command) and the replayed results MUST meet the spec-defined thresholds. The staleness detection (FR-008) acts as the merge-blocking gate in the standard PR pipeline. Changes that do not touch instruction files or eval scenarios are unaffected by this gate.

### Key Entities

- **Recording**: The captured, replayable interaction set of one eval sample from a genuine live agent run — the sequence of model exchanges needed to re-drive the agent loop without a provider.
- **Recording provenance**: Metadata attached to each recording — producing model identity, capture timestamp, instruction-file fingerprint, scenario fingerprint — establishing authenticity, attributability, and the basis for staleness detection.
- **Replay run record**: The scored outcome of replaying recordings — per-eval scores against thresholds plus, per result, the recording provenance it derives from and its trust status (trusted, stale, missing, mismatch).
- **Staleness status**: The relationship between a recording's fingerprints and the current state of instruction files and scenarios; determines whether a replay result may count as a trusted pass.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of replay runs complete with zero model-provider calls and zero provider configuration present.
- **SC-002**: A full replay of the eval suite completes in under 5 minutes on a standard developer machine — fast enough to run routinely before every pull request.
- **SC-003**: 100% of replay results name their recording provenance (model, capture date) and trust status.
- **SC-004**: Repeated replays of an unchanged recording set produce identical outcomes in 100% of runs.
- **SC-005**: 100% of recordings affected by an instruction-file or scenario change are flagged stale on the next replay run; 0 stale recordings are reported as trusted passes.
- **SC-006**: Routine verification of agent behavior (local pre-PR checks and the standard PR pipeline) incurs zero provider spend; live provider spend is limited to explicit recording runs and on-demand live eval runs.
- **SC-007**: The agent-judgment thresholds defined in existing feature specs are evaluated unchanged — this feature changes the cost and frequency of live runs, not what agent behavior must achieve.
- **SC-008**: The standard test suite reports zero skipped eval tests in 100% of runs — replay evals always execute; live evals exist only in the standalone eval command.
- **SC-009**: 100% of merged changes to agent instruction files carry eval evidence captured after the change (fresh recordings meeting thresholds); 0 instruction changes merge on stale evidence.

## Assumptions

- **Full removal of eval tests is out of scope.** The user's alternative — deleting the eval tests and testing only the deterministic part of the application — would violate constitution Principle II ("a feature whose value lies in agent judgment and that ships with only hermetic tests is NOT covered") and the Definition of Done, and would therefore require a MAJOR constitution amendment. Principle II explicitly permits evaluation "against real **or recorded** LLM output", so the recorded-replay approach is the constitution-compliant way to achieve the user's cost goal and was chosen for this spec.
- Recordings are stored versioned alongside the eval suite in the repository; their size for the current suite is acceptable for that mode of storage. Each scenario keeps only its current recordings, so growth is bounded.
- The existing scoring of eval scenarios is deterministic given an agent run's transcript and produced artifacts — replaying identical recordings therefore yields identical scores. If any scorer is itself model-based, bringing it under the replay guarantee is part of this feature's scope.
- The live evaluation infrastructure from the affordable-provider feature (NIM endpoint, on-demand CI workflow) is reused unchanged as the capture path; no new provider integration is introduced.
- Refresh cadence is event-driven: recordings are refreshed when staleness is flagged (instruction/scenario changes) or on explicit demand; scheduled periodic re-recording is out of scope.
- Replay evals run and gate like other deterministic tests once trusted; a stale or missing recording blocks a trusted pass but points to the concrete refresh action rather than failing with a judgment error.
- Restricting instruction changes to an evaluating agent/system (prohibiting manual edits entirely) was considered and deferred to a future feature; this feature delivers the fresh-evidence merge gate (FR-016) instead.
