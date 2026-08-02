# Feature Specification: Hub --help Usage Output

**Feature Branch**: `017-hub-help-usage`

**Created**: 2026-08-02

**Status**: Draft

**Input**: User description: "Hub startet mit --help und zeigt eine Usage-Meldung an, statt den Web-Server zu starten. Wer die Grimoire.Hub-Executable mit --help (oder -h) aufruft, bekommt eine Übersicht der verfügbaren Kommandozeilen-Optionen (u.a. submit-source sowie die ADR-009 Pfad-Switches wie --base-dir, --data-dir, --content-root usw.) auf der Konsole ausgegeben und der Prozess beendet sich sofort mit Exit-Code 0, ohne den Hub-Host tatsächlich hochzufahren."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Discover available startup options (Priority: P1)

A developer or operator who does not remember the exact command-line switches for
starting the Grimoire Hub runs the executable with `--help` (or `-h`) and immediately
sees a usage overview listing all supported options, instead of having to read source
code or documentation to find them.

**Why this priority**: This is the entire feature — without it there is no usage output
at all. It is also the only user story; the feature has no other independently
shippable slice.

**Independent Test**: Can be fully tested by running the Hub executable with `--help`
and verifying that a usage message is printed to the console and the process exits
without starting the web server.

**Acceptance Scenarios**:

1. **Given** the Hub executable, **When** it is started with `--help`, **Then** a usage
   message listing the available commands and options is printed to standard output,
   the process exits with code 0, and no web server is started (no port is bound, no
   HTTP endpoint becomes reachable).
2. **Given** the Hub executable, **When** it is started with `-h`, **Then** the same
   usage message and exit behavior as for `--help` occurs.
3. **Given** the Hub executable, **When** it is started with `--help` together with any
   other arguments (e.g. `--help --base-dir /tmp`), **Then** the usage message is still
   shown and the process still exits without starting the web server — `--help` takes
   precedence over all other arguments.

### Edge Cases

- What happens when `--help` is combined with the `submit-source` command (e.g.
  `submit-source --help`)? The usage message is shown and the process exits without
  attempting to submit anything — `--help` always wins over any other requested action.
- What happens when the Hub is started with no arguments at all? Behavior is unchanged
  by this feature: the web server starts normally, as it does today.
- What happens when `--help` is misspelled or an unrecognized flag is passed instead
  (e.g. `--halp`)? Out of scope for this feature — existing behavior for unrecognized
  arguments is unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Hub MUST recognize `--help` and `-h` as equivalent requests to show
  usage information, regardless of where they appear among the supplied arguments.
- **FR-002**: When a help request is recognized, the Hub MUST print a usage message to
  standard output that lists every documented top-level command (currently
  `submit-source`) and every documented command-line option (the ADR-009 path switches:
  `--base-dir`, `--data-dir`, `--content-root`, `--raw-dir`, `--state-db`,
  `--secrets-file`, `--instructions-dir`, `--agent-worker`, `--query-instructions-dir`,
  `--conversations-dir`, `--query-agent-worker`, `--write-locks-dir`, `--findings-dir`,
  `--lint-instructions-dir`, `--lint-agent-worker`, `--remediation-tasks-dir`; plus
  `submit-source`'s own `--path` and `--source-kind` options), each with a short
  description of its purpose.
- **FR-003**: When a help request is recognized, the Hub MUST exit with status code 0
  immediately after printing the usage message, without building or starting the web
  host, without binding any port, and without performing any other startup side effect
  (no path resolution failures, no state database initialization).
- **FR-004**: A help request MUST take precedence over every other recognized argument
  or command — if `--help`/`-h` is present anywhere in the arguments, the Hub MUST show
  usage and exit rather than executing `submit-source` or starting the server.
- **FR-005**: The usage message MUST be human-readable plain text suitable for a
  terminal (no implementation-internal detail such as .NET type names or stack traces).

### Key Entities

N/A — this feature has no data entities; it only affects process startup and console
output.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Hub invocations with `--help` or `-h` anywhere in the arguments
  print a usage message and exit with code 0, without starting the web server.
- **SC-002**: 100% of currently documented commands and options (`submit-source` and
  the 15 ADR-009 path switches) appear in the printed usage message.
- **SC-003**: A developer unfamiliar with the Hub's command-line surface can identify
  the correct switch to relocate the content root within 30 seconds of reading the
  `--help` output (readable, organized listing — no separate documentation lookup
  required for the common case).

## Assumptions

- "Usage message" means plain text written to standard output (not JSON or another
  structured format) — consistent with how `submit-source` already reports its result
  via `Console.WriteLine`.
- The set of options to document is exactly the set already wired up in
  `Program.cs` today (the ADR-009 path switches and `submit-source`'s `--path`/
  `--source-kind`); this feature only adds discoverability, it does not add, remove, or
  rename any existing option.
- This is a harness/CLI-parsing concern, not wiki-content judgment — no agentic
  behavior is involved (Constitution Principle V does not apply).
