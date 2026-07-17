# Feature Specification: Agent Eval Tests on Affordable Model Providers

**Feature Branch**: `claude/eval-tests-nim-endpoint-qgh5pd`

**Created**: 2026-07-17

**Status**: Draft

**Input**: User description: "die eval agent tests sollen ohne anthropic subscription laufen, sondern mit günstigeren anbietern. Im Projekt befindet sich bereits ein script um nim endpunkte über den litellm Proxy verfügbar zu machen. Diese test sollten idealerweise mit diesem nim endpunkt laufen und könnten auch on demand in github worklfows laufen, der api key dann als github secrets."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run agent evals locally without an Anthropic subscription (Priority: P1)

A developer who does not hold an Anthropic subscription wants to run the full agent-behavior evaluation suite on their machine. They configure the project's already-existing affordable-provider endpoint (the NIM endpoint exposed through the project's proxy script), provide the corresponding API key, and run the eval suite. All evaluation tests execute against that endpoint and report pass/fail against the thresholds defined in the feature specs.

**Why this priority**: This is the core value of the feature — today the eval gate hard-requires an Anthropic credential, which blocks every contributor without a subscription from verifying agent behavior at all. Removing that blocker unlocks the constitution-mandated evaluation workflow (Principle II) for everyone.

**Independent Test**: Can be fully tested by unsetting all Anthropic credentials, configuring only the affordable-provider endpoint and its key, running the eval suite, and observing that the evals execute (not skip) and produce scored results.

**Acceptance Scenarios**:

1. **Given** no Anthropic credential is present and the affordable-provider endpoint and key are configured, **When** the developer runs the eval suite with evals enabled, **Then** all eval tests execute against the affordable provider and report results against the spec-defined thresholds.
2. **Given** neither an Anthropic credential nor an affordable-provider configuration is present, **When** the eval suite runs, **Then** eval tests are skipped with a clear reason stating what must be configured to run them.
3. **Given** the affordable-provider endpoint is configured but unreachable (proxy not started, wrong port), **When** an eval test runs, **Then** the test fails with an actionable error identifying the connectivity problem — it does not silently skip or report a misleading judgment failure.

---

### User Story 2 - Run agent evals on demand in CI (Priority: P2)

A maintainer wants to trigger the agent-behavior evaluation suite on demand from the project's CI platform (GitHub Actions) — for example before merging an instruction-file change — without wiring any Anthropic subscription into CI. They start the eval workflow manually; the workflow reads the affordable-provider API key from the repository's secret store, runs the evals against the affordable provider, and publishes the outcome.

**Why this priority**: On-demand CI execution makes eval results reproducible and shareable for review decisions, and it is the stated deployment target for the affordable-provider setup. It builds directly on User Story 1's provider configurability.

**Independent Test**: Can be tested by manually triggering the eval workflow in CI with the provider key stored as a repository secret, and verifying the run completes and publishes eval results without any Anthropic credential configured anywhere in CI.

**Acceptance Scenarios**:

1. **Given** the affordable-provider API key is stored as a repository secret, **When** a maintainer manually triggers the eval workflow, **Then** the workflow runs the eval suite against the affordable provider and reports per-test pass/fail results.
2. **Given** the eval workflow runs in CI, **When** any log or report is produced, **Then** the API key value never appears in logs, reports, or published artifacts.
3. **Given** the repository secret is missing or invalid, **When** the eval workflow is triggered, **Then** the workflow fails early with a clear message naming the missing/invalid configuration, before burning eval samples.
4. **Given** the standard PR pipeline runs (not the eval workflow), **When** it completes, **Then** it neither requires the provider secret nor executes eval tests — the deterministic gates remain unaffected.

---

### User Story 3 - Keep provider choice flexible and transparent (Priority: P3)

A developer wants to choose which provider and model an eval run uses — the affordable default, a different affordable model, or Anthropic when a subscription is available — and wants every eval result to state which model actually produced it, so results from different providers are never confused with one another.

**Why this priority**: Provider flexibility protects the project from lock-in to a single cheap provider and preserves the ability to cross-check instruction quality on the reference model. Transparency of the model identity is required to interpret threshold results correctly.

**Independent Test**: Can be tested by running the same eval with two different configured models and verifying each run's recorded output names the model that was actually used.

**Acceptance Scenarios**:

1. **Given** a developer configures a specific model and endpoint for an eval run, **When** the run completes, **Then** the recorded eval output (task artifact / transcript) names exactly that model.
2. **Given** an Anthropic credential is configured instead of the affordable provider, **When** the eval suite runs, **Then** the evals execute against Anthropic exactly as before — the existing path keeps working.

---

### Edge Cases

- What happens when the affordable provider returns responses that are structurally unusable for the agent loop (e.g., the model does not support tool use)? The eval run must fail with a diagnosable error naming the model capability problem, not report it as an agent-judgment failure.
- What happens when the provider rate-limits or times out mid-run? The affected eval sample fails with the provider error recorded; the run must not hang indefinitely.
- What happens when both Anthropic and affordable-provider configurations are present simultaneously? Exactly one provider is used per run, and the choice is deterministic and documented (explicit configuration wins over defaults).
- What happens when a cheaper model consistently scores below a spec-defined threshold that the reference model passes? The eval fails honestly at the spec-defined threshold; thresholds are not silently lowered per provider (see Assumptions).
- What happens if someone triggers the CI eval workflow concurrently twice? Both runs are independent; results are attributed to their own run without interference.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The agent-behavior evaluation suite MUST be runnable against a configurable model provider endpoint and model, without requiring any Anthropic subscription or Anthropic-specific credential.
- **FR-002**: The default affordable path MUST use the project's existing NIM-endpoint-via-proxy setup (the scripts already present in the project), so a developer can go from "proxy started" to "evals running" with documented configuration only — no code changes.
- **FR-003**: The eval enablement gate MUST accept the affordable-provider configuration as sufficient to run evals. When neither an Anthropic credential nor an affordable-provider configuration is present, eval tests MUST skip with a reason message that names both supported options.
- **FR-004**: When evals are enabled but the configured endpoint is unreachable or the credential is rejected, eval tests MUST fail with an actionable connectivity/authentication error — they MUST NOT skip and MUST NOT misreport the problem as an agent-judgment failure.
- **FR-005**: An on-demand, manually triggered CI workflow MUST run the eval suite against the affordable provider, reading the provider API key exclusively from the repository's secret store.
- **FR-006**: The on-demand eval workflow MUST be separate from the standard PR pipeline: PR CI keeps running only deterministic gates and MUST NOT require the provider secret. (Constitution Principle II: harness tests stay hermetic; evals are the sampled, non-hermetic tier.)
- **FR-007**: The CI eval workflow MUST publish the eval outcome (per-test pass/fail and the achieved scores against thresholds) in a form reviewers can read after the run, including the eval transcripts as retrievable run artifacts.
- **FR-008**: The provider API key MUST never appear in test output, logs, transcripts, or published CI artifacts.
- **FR-009**: Every eval run record (task artifact, transcript) MUST name the model that actually produced the run, so results from different providers are distinguishable.
- **FR-010**: The number of samples per eval MUST remain configurable per run (as today), so CI cost can be controlled independently of local runs.
- **FR-011**: The existing Anthropic path MUST keep working unchanged for developers who have a subscription; selecting a provider MUST be a configuration decision, never a code change.

### Key Entities

- **Provider configuration**: The set of values that select a model provider for an eval run — endpoint address, model identifier, and API credential. Exactly one active configuration per run.
- **Eval run record**: The existing per-run output (task artifact, transcript, scored result) extended by the guarantee that it faithfully names the model/provider used.
- **CI eval workflow run**: A manually triggered execution of the eval suite in CI, bound to a secret-stored credential, producing published results and artifacts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer with no Anthropic credential can run the complete agent-eval suite locally using the documented affordable-provider setup; 100% of eval tests execute (none skip) once the provider is configured.
- **SC-002**: 100% of eval run records name the model that actually served the run.
- **SC-003**: A manually triggered CI eval run completes and publishes readable results and transcripts with zero Anthropic configuration present in CI.
- **SC-004**: 100% of CI eval runs expose no credential material in logs, reports, or artifacts.
- **SC-005**: The standard PR pipeline's runtime and required secrets are unchanged by this feature (0 new secrets, 0 new jobs in the PR-triggered pipeline).
- **SC-006**: The agent-judgment thresholds defined in existing feature specs (e.g., "≥ 90% of sampled ingests choose update over duplicate creation") are evaluated unchanged on the affordable provider — the feature changes where evals run, not what they require.

## Assumptions

- The project's existing proxy scripts (NIM endpoint via LiteLLM-style proxy) are the supported mechanism for exposing the affordable provider, and the developer starts the proxy themselves for local runs; keeping the proxy alive is not part of the eval suite's responsibility. For CI runs, the workflow is responsible for making the endpoint available for the duration of the run.
- "Günstigere Anbieter" is interpreted as: any provider reachable through an endpoint that speaks the same message protocol the agent loop already uses (the existing model-client abstraction with a configurable base URL) — with the NVIDIA NIM endpoint as the concrete default. Supporting arbitrary provider-native protocols is out of scope.
- Spec-defined agent-judgment thresholds are provider-independent: if the affordable model cannot meet a threshold, the eval fails and the team decides whether to change the model or the instructions — per-provider threshold lowering is out of scope for this feature.
- The CI platform is GitHub Actions (already in use for the PR pipeline), and its repository secret store is the approved place for the provider API key.
- On-demand triggering (manual start by a maintainer) is the only new CI trigger in scope; scheduled or per-PR eval runs are out of scope for this feature.
- The eval suite's structure (which evals exist, their fixtures and scoring) is unchanged; this feature only changes provider selection, gating, and CI execution.
