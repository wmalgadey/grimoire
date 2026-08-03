# Quickstart: Hub CLI Command Parity for Write Actions

**Feature**: `018-hub-cli-commands` | **Date**: 2026-08-02, revised 2026-08-03

Validation guide proving the feature works end-to-end. Command grammar, output
formats, and exit codes are defined in [contracts/cli-commands.md](./contracts/cli-commands.md);
design rationale in [research.md](./research.md) and ADR-019.

**Key property to validate throughout**: no running Hub is required — every command
executes in-process against the data directory and blocks until its work is done.

## Prerequisites

- .NET 10 SDK
- A Hub data directory (any `--base-dir` with `wiki/`); commands that act on tasks
  need seeded state — see scenarios
- An agent secrets file in place for commands that spawn agents (`lint-run`, `query`,
  `ingest-*`, executed `remediation-authorize`) — same requirement as `submit-source`
- Build once:

```bash
cd backend
dotnet build Grimoire.slnx
HUB_DLL=src/Grimoire.Hub/bin/Debug/net10.0/Grimoire.Hub.dll
```

## 1. Help and dispatch

```bash
dotnet "$HUB_DLL" --help          # expect: logo, Commands: all 8, Server options: path switches; exit 0
dotnet "$HUB_DLL" query --help    # expect: prompt argument, --conversation-id, --timeout, path switches; exit 0
dotnet "$HUB_DLL" no-such-command # expect: usage error naming the command; exit 2
dotnet "$HUB_DLL" remediation-authorize --base-dir /tmp/empty
# expect: usage error (missing --task-id); exit 2, no state touched
```

## 2. One-shot state transitions (no Hub running)

Remediation lifecycle (needs a proposed task — produce one via a lint run over content
with findings, or seed a row; ids are visible on the task board or in the state DB):

```bash
DATA=--base-dir\ /path/to/grimoire-data

dotnet "$HUB_DLL" remediation-dismiss  $DATA --task-id does-not-exist
# → "Remediation task 'does-not-exist' was not found." ; exit 3

dotnet "$HUB_DLL" remediation-authorize $DATA --task-id <id>
# queue paused (fresh process): → "Remediation task <id> authorized at …" ; exit 0
# queue not paused: blocks, streams execution status, exits with the task's outcome

dotnet "$HUB_DLL" remediation-withdraw  $DATA --task-id <id>   # → back to proposed, exit 0 (if not executing)
dotnet "$HUB_DLL" remediation-dismiss   $DATA --task-id <id>   # → dismissed, exit 0
dotnet "$HUB_DLL" remediation-dismiss   $DATA --task-id <id>
# → again: "not proposed (current state: dismissed)" ; exit 4
```

## 3. Blocking agent runs

```bash
dotnet "$HUB_DLL" lint-run $DATA
# → status stream on stderr; final line "Lint run {runId} completed. …" ; exit 0
# While it runs, from a second terminal:
dotnet "$HUB_DLL" lint-run $DATA
# → "A lint run is already active." (lint.pid held by the first process) ; exit 4

dotnet "$HUB_DLL" ingest-resume $DATA
# → "Ingest queue resumed: {n} task(s) queued." then blocks until the queue drains; exit 0

dotnet "$HUB_DLL" ingest-retrigger $DATA --task-id <queuedId>
# → blocks until that task's terminal state; exit 0/1
```

## 4. Blocking query

```bash
dotnet "$HUB_DLL" query $DATA "What does the wiki say about deployment?"
# → answer streams to stderr while running; stdout ends with
#   "Query turn {turnId} in conversation {generatedId}: completed" + answer; exit 0

dotnet "$HUB_DLL" query $DATA "Follow-up question" --conversation-id <generatedId>
# → next turn in the same conversation

dotnet "$HUB_DLL" query $DATA "Slow question" --timeout 1
# (turn takes longer) → "Timed out after 1s… interrupted and its partial answer persisted." ; exit 5
# Verify: conversation record shows the turn terminal state "interrupted" with partial answer.

dotnet "$HUB_DLL" query $DATA "Long question"   # press Ctrl-C while waiting
# → "Cancelled: query turn {turnId} interrupted." ; exit 130
# Verify: same interrupted terminal state in the record.
```

## 5. Coexistence with a running Hub (accepted, unguarded)

Start a Hub (`dotnet "$HUB_DLL" $DATA`) and, from a second terminal, run
`remediation-dismiss`/`withdraw` CLI commands against the same data directory — they
succeed (SQLite busy-tolerance), and `lint-run` conflicts are detected in **both**
directions via `lint.pid` (trigger via UI, then CLI → exit 4; trigger via CLI, then
UI → 409). Other concurrent agent runs are intentionally unguarded (Clarification
2026-08-03).

## 6. Automated validation

```bash
cd backend

# Structural gates (Phase 0): Spectre containment, namespace ownership map
dotnet test tests/Grimoire.ArchTests

# Command matrix, parity, query stream/timeout/cancel, concurrency, help output
dotnet test tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~HubCli"
dotnet test tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~HubHelpUsage"
```

Expected: all green; the integration tests are hermetic (real composed service graph,
temp-dir SQLite, fake agent launcher — no live LLM calls).

## 7. Success-criteria checklist

- [ ] SC-001: every command in §2–§4 ran from the terminal with no Hub running
- [ ] SC-002: each failure demo printed its specific reason and a non-zero exit
- [ ] SC-003: each success printed an identifier + state line on stdout, exit 0
- [ ] SC-004: `--help` lists all commands with arguments
- [ ] SC-005: parity tests green (`HubCliParityTests`)
- [ ] SC-006: timeout (exit 5) vs. Ctrl-C (exit 130) vs. failure (exit 1) distinguishable; turn `interrupted` in both timeout and cancel cases
