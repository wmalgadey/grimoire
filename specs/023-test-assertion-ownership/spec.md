# Feature Specification: Test Assertion Ownership Boundary

**Feature Branch**: `023-test-assertion-ownership`

**Created**: 2026-08-11

**Status**: Draft

**Input**: User description: "Refactor the test strategy so integration tests assert Grimoire-owned contracts, not any library internals. Proposed scope: 1. Audit all assertions in all tests and classify each as product-owned contract or framework-owned behavior. 2. Keep (or strengthen) tests that validate product-owned contracts. 3. Remove or relax brittle assertions tied to exact rendered formatting/text that we do not own. 4. If needed, move residual framework smoke checks to a minimal 'wire-up' test with clear intent. 5. Update inline test docs/comments to explain the value and ownership boundary of each remaining check. Acceptance criteria: Tests primarily assert product-owned behavior; no test depends on exact output unless explicitly justified as a product requirement; existing protection for surface regressions remains intact; test suite remains green."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - CI failures stay actionable when a dependency changes its output (Priority: P1)

A Grimoire maintainer upgrades the Spectre.Console CLI framework version (or the framework changes its help-table layout, word-wrap point, or logo rendering in a point release). Today, some integration tests assert on exact framework-rendered text, so this kind of upstream change can turn CI red even though nothing about Grimoire's own command surface, exit codes, or guardrail behavior changed. The maintainer needs the test suite to fail only when a *Grimoire-owned* contract actually breaks.

**Why this priority**: This is the core problem statement — false-positive CI failures on framework changes waste maintainer time and erode trust in the suite ("cry wolf" effect), which is exactly what Constitution Principle II's assertion-ownership boundary (v1.9.0) exists to prevent.

**Independent Test**: Can be fully tested by reviewing the audited test files and confirming that no remaining assertion pins a framework-rendered detail (exact multi-line help block, table column widths, ANSI styling, exact word-wrap boundary) without an inline comment justifying it as an explicit product requirement.

**Acceptance Scenarios**:

1. **Given** the audited CLI integration test suite, **When** a reviewer inspects every assertion that reads command-line output, **Then** each assertion either checks a product-owned fact (a command/switch name or description sourced from the product's own catalog, an exit code the product defines, `--help` precedence over side effects, a guardrail/validation outcome) or is explicitly labeled and justified as an intentional product requirement.
2. **Given** a hypothetical Spectre.Console upgrade that changes only column widths, ANSI styling, or word-wrap points (not command names, switches, or exit codes), **When** the audited test suite is run, **Then** no test fails as a result of that change alone.

---

### User Story 2 - Reviewers can tell what a test assertion protects (Priority: P2)

A reviewer looking at a pull request that touches a CLI integration test needs to judge, without re-deriving the reasoning from scratch, whether a given assertion protects something Grimoire itself guarantees or merely happens to pass through a third-party library's rendering.

**Why this priority**: Without a visible ownership label, reviewers cannot distinguish a legitimate regression guard from an accidental coupling to framework internals, which is how brittle assertions get re-introduced after this refactor.

**Independent Test**: Can be fully tested by reading the inline comments on assertions against rendered/produced output and confirming each names the product requirement it enforces (an FR/SC/ADR reference) and, where scoped to a substring or prefix, why that scoping avoids coupling to framework-internal formatting.

**Acceptance Scenarios**:

1. **Given** any assertion in the audited files that reads output produced by Spectre.Console, **When** a reviewer reads the surrounding comment, **Then** the comment states which product requirement the assertion enforces.
2. **Given** an assertion scoped to a substring or prefix rather than full text, **When** a reviewer reads the surrounding comment, **Then** the comment explains why that scoping is necessary to avoid coupling to framework-internal formatting (e.g., word-wrap).

---

### User Story 3 - Framework wiring still gets a minimal, honest smoke check (Priority: P3)

A maintainer wants confidence that the CLI framework is actually wired up correctly (the app boots, real commands dispatch through the framework's real resolver) without pretending that check is a business-logic or rendering contract.

**Why this priority**: Removing framework-coupled assertions must not silently drop wiring coverage — a prior real regression in this codebase (a command dependency-resolution failure) was only caught by a genuine out-of-process dispatch test, so this coverage has to be preserved deliberately rather than assumed away.

**Independent Test**: Can be fully tested by confirming exactly one (or a small, clearly bounded set of) test(s) exists whose sole documented purpose is "the framework boots and dispatches," separate from any test asserting product behavior.

**Acceptance Scenarios**:

1. **Given** the audited test suite, **When** a reviewer searches for wire-up-only checks, **Then** any such check is isolated in its own clearly labeled test whose doc comment states its intent is process/dispatch integration, not a rendering or business-logic contract.

---

### Edge Cases

- What happens when the only observable way to confirm a product-owned fact (e.g., "this switch exists") is by reading it out of framework-rendered stdout? → The assertion MUST still verify the product-owned fact (the switch name is present, sourced from the product's own catalog) but MUST use substring/prefix matching robust to reflow, not an equality check against the full rendered block.
- How does the audit treat a check that currently proves a product decision ("the logo and 'Server options:' section appear only in root help, never in per-command help") but does so today by pinning a byproduct of the framework's own rendering (e.g., a literal fragment of Figlet ASCII-art output)? → The underlying product decision (root-only sections) MUST still be verified, but not via a marker string whose only justification is "this happens to be present in today's captured framework output."
- What happens if a future contributor adds a new CLI integration test that asserts full-text equality against console output? → The spec's acceptance criteria (no test depends on exact output without explicit justification) applies to it the same as to existing tests; this is a standing rule, not a one-time cleanup.
- Does this audit touch Grimoire's own output formats (e.g., the findings-report Markdown/YAML format Grimoire itself defines and owns)? → No — exact-text assertions against a format the product itself specifies are appropriately exact and are out of scope.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every assertion in the audited integration test files that reads Spectre.Console-rendered stdout/stderr MUST verify a product-owned fact (a command or switch name/description sourced from the product's own single source of truth, an exit code the product defines, `--help`/`-h` precedence over command execution, or a guardrail/validation outcome the product specifies) rather than an exact framework-rendered formatting detail (full help-block text, table column widths, table borders, ANSI styling, or an exact word-wrap boundary).
- **FR-002**: Every assertion identified during the audit as depending on a framework-rendering byproduct with no independent product justification (e.g., a hardcoded fragment of the framework's Figlet-logo ASCII art used only because it happens to appear in today's captured output) MUST be replaced with an assertion that verifies the underlying product decision directly, or removed if no such product decision exists.
- **FR-003**: Where a product-owned catalog already exists as the single source of truth for expected command/switch names or descriptions (e.g., a path-switch catalog, a command catalog), test assertions MUST source their expected values from that catalog rather than duplicating literal strings that can silently drift from it.
- **FR-004**: Where a test needs to confirm that content is present within framework-rendered output, it MUST assert via substring or prefix matching robust to text reflow, never via full-text equality against a framework-rendered block.
- **FR-005**: Any test whose only purpose is confirming that the CLI framework boots and dispatches at all (not a rendering or business-logic contract) MUST be isolated into its own explicitly labeled test, with a doc comment stating that its intent is process/dispatch integration.
- **FR-006**: Every remaining assertion against framework-produced output MUST carry an inline comment naming the product requirement it enforces (a functional requirement, success criterion, or ADR reference) and, where the assertion is scoped to a substring or prefix rather than full text, explaining why that scoping is needed to avoid coupling to framework-internal formatting.
- **FR-007**: The audit MUST NOT weaken, relax, or remove any existing protection against a product-owned regression — command/switch catalog presence and parity, `--help`/`-h` precedence over side effects, exit-code semantics, and guardrail/validation behavior MUST remain covered at least as thoroughly as before the refactor.
- **FR-008**: The full backend automated test suite MUST pass after the refactor, with no reduction in the number of distinct product-owned behaviors covered.

### Key Entities

- **Product-owned contract assertion**: A test assertion whose expected value is a fact Grimoire itself defines — a command/switch name from its own catalog, an exit code, a message template the product's own code produces, or a guardrail decision. Survives framework changes that don't touch the product's own behavior.
- **Framework-owned rendering detail**: Output structure or formatting that originates from Spectre.Console's own layout/rendering engine — table column widths, word-wrap points, ANSI escape sequences, borders, Figlet-art glyphs — that Grimoire does not specify and should not pin exactly.
- **Wire-up smoke check**: A narrowly-scoped test whose only job is proving the framework's dispatch machinery reaches real product code (no port bound, real command constructor resolved), explicitly labeled as such and not treated as a rendering or business-logic contract.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of assertions in the audited CLI integration test files (the files identified as exercising Spectre.Console-rendered output) either verify a product-owned fact or are explicitly labeled and comment-justified as an intentional wire-up smoke check.
- **SC-002**: 100% of the regression protections that existed before the refactor for command/switch catalog parity, `--help` precedence over side effects, exit-code semantics, and guardrail/validation behavior still pass after the refactor.
- **SC-003**: 0 test assertions in the audited files depend on exact framework-rendered text (a full help block, table borders, ANSI codes, or an exact word-wrap boundary) without an inline comment justifying it as an explicit product requirement.
- **SC-004**: 100% of the full backend test suite (all test projects) passes after the refactor, with no fewer passing tests than the pre-refactor baseline.

## Assumptions

- The audit's primary target is `backend/tests/Grimoire.IntegrationTests` — specifically the files that exercise the Hub CLI through Spectre.Console (`HubHelpUsageTests.cs`, `HubCliCommandTests.cs`, `HubCliQueryCommandTests.cs`, `HubCliParityTests.cs`) and the two `Grimoire.ArchTests` files that reflect over Spectre attributes/namespaces. A pre-work survey found the majority of assertions in these files already source expected values from product catalogs and use substring/prefix matching; `HubHelpUsageTests.cs` (the Figlet-logo marker and its documented word-wrap-avoidance pattern) needs the most direct attention.
- `LintFindingsReportFormatTests.cs` and any other tests validating a Markdown/YAML/text format that Grimoire itself specifies (not a third-party renderer) are out of scope — exact-text assertions against a product-owned format are appropriate and unaffected by this refactor.
- The frontend test suite (`frontend/`) currently contains no UI-framework snapshot-style assertions (e.g., exact rendered HTML/CSS) and is out of scope for this feature; if that changes in the future, it would need its own audit.
- This feature is harness/test-quality work with no agentic-core or agent-judgment surface (Constitution Principle V does not apply); all success criteria are deterministic guarantees, not evaluation thresholds.
- The definition of "product-owned" vs. "framework-owned" is the one established in Constitution Principle II's "Assertion ownership boundary" (v1.9.0); this feature operationalizes that boundary against the existing test suite rather than redefining it.
- No new test framework, library, or tooling is introduced; the refactor works within the existing xUnit + Spectre.Console testing patterns already used in the repository.
