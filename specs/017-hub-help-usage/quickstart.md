# Quickstart: Hub --help Usage Output

Manual validation guide for this feature. Confirms SC-001/SC-002 by hand and provides
the timed read-through for SC-003 (not automatable — see `plan.md` Test Strategy).

## Prerequisites

- .NET 10 SDK installed (matches `backend/Directory.Build.props`)
- Repository checked out; no `data/` directory or `.env` file required

## Validate `--help` shows usage and exits immediately

From `backend/`:

```bash
dotnet run --project src/Grimoire.Hub/ -- --help
```

**Expected outcome**:
- A usage message is printed listing `submit-source` and every ADR-009 path switch
  (`--base-dir`, `--data-dir`, `--content-root`, `--raw-dir`, `--state-db`,
  `--secrets-file`, `--instructions-dir`, `--agent-worker`, `--query-instructions-dir`,
  `--conversations-dir`, `--query-agent-worker`, `--write-locks-dir`, `--findings-dir`,
  `--lint-instructions-dir`, `--lint-agent-worker`, `--remediation-tasks-dir`), plus
  `submit-source`'s own `--path`/`--source-kind`.
- The command returns to the shell prompt within a couple of seconds (no hang).
- `echo $?` reports `0`.
- No `Now listening on: http://...` line appears (the tell-tale sign the host started).

## Validate `-h` is equivalent

```bash
dotnet run --project src/Grimoire.Hub/ -- -h
```

**Expected outcome**: identical to `--help` above.

## Validate `--help` wins over other arguments

```bash
dotnet run --project src/Grimoire.Hub/ -- --help --base-dir /tmp/does-not-exist
dotnet run --project src/Grimoire.Hub/ -- submit-source --help
```

**Expected outcome**: both print the same usage message and exit 0 — neither attempts
path resolution against the bogus `--base-dir`, nor attempts a `submit-source` run.

## Validate unrelated startup is unaffected

```bash
dotnet run --project src/Grimoire.Hub/
```

(Requires the usual local `data/.env` setup — see `CONTRIBUTING.md`.) **Expected
outcome**: the Hub starts normally as it does today; behavior is unchanged when `--help`
is absent.

## SC-003 read-through check

Time yourself reading the `--help` output and locating the switch to relocate the
content root (`--content-root`). Expected: under 30 seconds, without consulting any
other documentation.
