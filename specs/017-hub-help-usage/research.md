# Phase 0 Research: Hub --help Usage Output

No items in `plan.md`'s Technical Context were marked `NEEDS CLARIFICATION` — this is a
small, self-contained change to an existing composition root. The decisions below
record the choices made while turning the spec into a concrete implementation approach,
per the `/speckit-plan` Phase 0 requirement to consolidate research findings.

## Decision: Where in `Program.cs` the help check must run

**Decision**: Check `args` for `--help`/`-h` as the very first statement in `Program.cs`,
before `WebApplication.CreateBuilder(args)`.

**Rationale**: FR-003 requires the process to exit "without performing any other
startup side effect (no path resolution failures, no state database initialization)".
`GrimoirePathResolver.Resolve(...)` (line ~54 in the current file) can throw if
`data/.env`-adjacent configuration is missing or malformed, and `OperationalStateRepository.InitializeAsync()`
(line ~66-67) touches SQLite. Both run before the existing `submit-source` branch. To
guarantee `--help` works even with no `data/` directory present (spec Assumption), the
check must precede all of that — including `WebApplication.CreateBuilder(args)`, since
`CreateBuilder` itself does not fail on a bare invocation but nothing after it should
run for a help request.

**Alternatives considered**:
- *Check after `AddCommandLine` binds the ADR-009 switches*: rejected — `AddCommandLine`
  itself doesn't validate values, so this would work, but it adds no value over checking
  earlier and would make the "no side effects before exit" guarantee harder to audit at
  a glance (a reviewer would have to trace forward to confirm nothing else runs first).
- *Check inside the existing `if (args.Length > 0 && ... "submit-source")` block only*:
  rejected — this only covers `submit-source --help`, not a bare `--help`, violating
  FR-001 (recognized "regardless of where they appear").

## Decision: How the usage text is generated

**Decision**: Build the usage text from the same `PathConfigurationSwitchMappingsFactory()`
dictionary already used to wire `AddCommandLine`'s switch mappings, rather than a
separately hand-written list of `--base-dir` etc.

**Rationale**: ADR-009 constrains this feature (see `plan.md`) — the switch vocabulary
already exists as a single source of truth in `Program.cs`. Duplicating it as a literal
string in a new `PrintUsage()` method would let the two drift the next time a switch is
added or renamed, silently breaking SC-002's completeness guarantee. Iterating the
dictionary's keys and pairing each with a short static description (a small side lookup
table keyed by the same switch string) keeps the switch *names* single-sourced while
still allowing a human-readable description per switch.

**Alternatives considered**:
- *Reflection over `GrimoirePathOptions`*: rejected — the options class's property
  names don't necessarily match the CLI switch spelling, and reflection would be a
  disproportionate mechanism for a static console message.
- *Hand-written usage string*: rejected per Rationale above (drift risk); this is
  exactly the failure mode the Phase 0 parity test (see `plan.md` Test Strategy,
  SC-002) is designed to catch structurally.

## Decision: How to test process exit / no-server-start behavior

**Decision**: Spawn the actual built Hub executable as a real OS process
(`ProcessStartInfo` + `Process.Start`), matching the existing pattern in
`ReplayAdapterTests.cs` / `CrossProcessFileLockTests.cs`, and assert it exits (with the
expected exit code and stdout content) within a short timeout — rather than using
`WebApplicationFactory<T>` or calling `Program`'s top-level code in-process.

**Rationale**: `WebApplicationFactory` boots the host in-process and never observes
"the process exited before `app.Run()`" — proving FR-003 requires an actual process
boundary. A short wait-for-exit timeout (e.g. 5s) is a reliable proxy for "the host
never started," since `app.Run()` blocks indefinitely; if the help path were bypassed,
the test process would hang past the timeout and fail loudly rather than passing by
accident.

**Alternatives considered**:
- *Assert no TCP port is listening*: rejected as the primary signal — flaky (needs a
  fixed port or dynamic-port discovery) and strictly weaker than "the process exited,"
  which already implies no port was bound.
