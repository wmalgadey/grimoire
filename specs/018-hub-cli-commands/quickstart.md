# Quickstart: Hub CLI Command Parity for Write Actions

**Feature**: `018-hub-cli-commands` | **Date**: 2026-08-02

Validation guide proving the feature works end-to-end. Command grammar, output
formats, and exit codes are defined in [contracts/cli-commands.md](./contracts/cli-commands.md);
design rationale in [research.md](./research.md) and ADR-019.

## Prerequisites

- .NET 10 SDK
- A Hub data directory (any `--base-dir` with `wiki/`; commands that act on tasks need
  seeded state — see scenarios)
- Build once:

```bash
cd backend
dotnet build Grimoire.slnx
```

## 1. Help and dispatch (no Hub required)

```bash
HUB_DLL=backend/src/Grimoire.Hub/bin/Debug/net10.0/Grimoire.Hub.dll

dotnet "$HUB_DLL" --help          # expect: logo, Commands: all 8, Server options: path switches; exit 0
dotnet "$HUB_DLL" query --help    # expect: prompt argument, --conversation-id, --timeout, --hub-url; exit 0
dotnet "$HUB_DLL" no-such-command # expect: usage error naming the command; exit 2
dotnet "$HUB_DLL" remediation-authorize   # expect: usage error (missing --task-id); exit 2, no HTTP call
dotnet "$HUB_DLL" lint-run; echo "exit=$?" # Hub not running → "Cannot reach the Grimoire Hub…"; exit=7
```

## 2. Start a Hub, then run the one-shot commands

Terminal A (running Hub):

```bash
dotnet "$HUB_DLL" --base-dir /path/to/grimoire-data   # listens on http://localhost:5255 (launch profile)
```

Terminal B:

```bash
dotnet "$HUB_DLL" lint-run
# → "Lint run {runId} started (status: running)." ; exit 0
dotnet "$HUB_DLL" lint-run
# → immediately again: "A lint run is already active." ; exit 4

dotnet "$HUB_DLL" ingest-resume
# → "Ingest queue resumed: {n} task(s) queued." ; exit 0 (idempotent)

dotnet "$HUB_DLL" ingest-retrigger --task-id does-not-exist
# → "Task 'does-not-exist' was not found." ; exit 3
```

Remediation lifecycle (needs a proposed task — produce one via a lint run over content
with findings, or seed a row; task ids are visible on the task board / via
`GET /api/remediation-tasks`):

```bash
dotnet "$HUB_DLL" remediation-authorize --task-id <id>   # → authorized, exit 0
dotnet "$HUB_DLL" remediation-withdraw  --task-id <id>   # → back to proposed, exit 0 (if not yet executing)
dotnet "$HUB_DLL" remediation-dismiss   --task-id <id>   # → dismissed, exit 0
dotnet "$HUB_DLL" remediation-dismiss   --task-id <id>   # → again: "not proposed (current state: dismissed)", exit 4
```

## 3. Blocking query

```bash
dotnet "$HUB_DLL" query "What does the wiki say about deployment?"
# → header "Query turn {turnId} in conversation {generatedId}: completed"
#   followed by the answer text; exit 0

dotnet "$HUB_DLL" query "Follow-up question" --conversation-id <generatedId>
# → next turn in the same conversation

dotnet "$HUB_DLL" query "Slow question" --timeout 1
# (with a turn that takes longer) → "Timed out after 1s… still running on the Hub." ; exit 5
# Verify no interrupt happened: GET /api/query-turns/{turnId} still "running"/later terminal.

dotnet "$HUB_DLL" query "Long question"   # press Ctrl-C while waiting
# → "Cancelled: interrupt requested for query turn {turnId}." ; exit 130
# Verify: turn reaches state "interrupted" server-side.
```

## 4. Automated validation

```bash
cd backend

# Structural gates (Phase 0): Spectre/HTTP containment, namespace ownership map
dotnet test tests/Grimoire.ArchTests

# Command matrix, parity, query wait/timeout/cancel, help output
dotnet test tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~HubCli"
dotnet test tests/Grimoire.IntegrationTests --filter "FullyQualifiedName~HubHelpUsage"
```

Expected: all green; the integration tests are hermetic (TestServer + temp-dir SQLite +
fake agent launcher — no live LLM, no network beyond loopback TestServer).

## 5. Success-criteria checklist

- [ ] SC-001: every command in §2/§3 ran from the terminal without the web UI
- [ ] SC-002: each failure demo printed its specific reason and a non-zero exit
- [ ] SC-003: each success printed an identifier + state line, exit 0
- [ ] SC-004: `--help` lists all commands with arguments
- [ ] SC-005: parity tests green (`HubCliParityTests`)
- [ ] SC-006: timeout (exit 5) vs. Ctrl-C (exit 130) vs. failure (exit 1) demos distinguishable, with correct server-side turn state
