# Contributing to Grimoire

## Prerequisites

- .NET SDK matching `backend/Directory.Build.props`
- Node.js + [Bun](https://bun.sh) (frontend uses `bun.lock`)
- Copy `.env-example` to `.env` (repository root) and fill in the credentials you need locally
  (`ANTHROPIC_AUTH_TOKEN` for agent runs; NVIDIA/LiteLLM vars only if you run evals
  against the affordable provider — see `specs/007-eval-tests-nim-endpoint/quickstart.md`)

## Using the devcontainer (recommended)

This is an additional, opt-in onboarding path — the native setup above remains fully
supported and unaffected.

Prerequisites: a container runtime running on the host (Docker Desktop or Podman — see
`.vscode/tasks.json` for the `podman machine start` step) and a devcontainer-capable
editor (e.g. VS Code with the [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers)).

1. Open the repository in your editor.
2. Run "Reopen in Container" (or your tool's equivalent).
3. Wait for the container to build — you land in a shell with `dotnet`, `node`, and
   `bun` already installed and on the `PATH`.

See `specs/016-devcontainer-setup/quickstart.md` for a full step-by-step validation
walkthrough (build, test suites, credentials).

Local credentials still go through the same `.env` file (copied from `.env-example`,
repository root) as the native setup — the devcontainer doesn't change that; it just
makes `.env` reachable from inside the container.

**Known limitation**: the `prod` launch configuration in `.vscode/launch.json`
hardcodes a personal, host-absolute `--wiki-dir` path and is not reachable from
inside the devcontainer. This is an intentional, permanent host-only limitation, not a
bug — use `dev`/`proxy` inside the devcontainer, or run `prod` natively on the host.

## Building and testing

```bash
# Backend
dotnet build backend/Grimoire.slnx --configuration Release
dotnet test backend/tests/Grimoire.ArchTests --configuration Release
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release
dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release

# Frontend
cd frontend
bun install
bun run check   # type-check
bun run lint
bun run test    # vitest
```

The commands above are the full merge-gating suite (`ci.yml`'s shape, unchanged) — every
one of them still gates every PR. Day to day, use the tiered commands below instead of
running everything on every edit.

## Test Tiers

The backend test suite is organized into three tiers by what a test verifies, not by
which project it lives in (a handful of hermetic harness-mechanics tests inside
`Grimoire.AgentEvals` join the Fast tier via an xUnit `Tier` trait rather than living in
a different project — see ADR-021).

| Tier | Command | Contains | Duration |
|---|---|---|---|
| **Fast** | `./scripts/test-fast.sh` | `Grimoire.Domain.UnitTests` (all), `Grimoire.ArchTests` (all), and the hermetic harness-mechanics classes inside `Grimoire.AgentEvals` (`Tier=Fast`) | fast — low single-digit seconds of test execution, excluding build |
| **Integration** | `dotnet test backend/tests/Grimoire.IntegrationTests --configuration Release` | the entire `Grimoire.IntegrationTests` project (real Kestrel hosts, SignalR, fake/real agent processes) | moderate |
| **SlowEval (opt-in)** | `dotnet test backend/tests/Grimoire.AgentEvals --configuration Release --filter "Tier=SlowEval"` | the five genuine replay-eval scenario classes that exercise agent judgment against committed recordings (ADR-012) | slow, opt-in — not part of the default local workflow |

Run `./scripts/test-fast.sh` while you work — it needs a built solution and nothing
else (no recordings, no provider credential, no network) and reports which tier failed
first. Run the Integration tier before opening a PR that touches `Grimoire.Hub` or agent
dispatch. Run the SlowEval tier only when you changed agent instructions, prompts, or
evaluation scenarios — it replays committed recordings through the real agent
executable and is the slow, deliberately opt-in tier; the merge gate (`ci.yml`) still
runs it, and the unfiltered `Grimoire.AgentEvals` project run above covers both the Fast
and SlowEval classes in one invocation, exactly as it always has.

### Mutation testing (on demand)

`./scripts/mutation-test.sh` runs [Stryker](https://stryker-mutator.io/) over the backend
and the frontend and writes an HTML report per target into `docs/reports/mutation/`, with
an index page over all of them. Coverage says a line ran; a surviving mutant says the
suite would not notice if that line were wrong — the more useful question when the tests
are the safety net for a harness whose behavior lives in agent instructions.

```bash
./scripts/mutation-test.sh                 # fast group: guardrail policy + state machine
./scripts/mutation-test.sh --list          # every target and the groups it belongs to
./scripts/mutation-test.sh --only hub      # one target
./scripts/mutation-test.sh --group all     # everything — hours, see below
./scripts/mutation-test-docker.sh --group all   # the same, on a host with only a container runtime
```

Cost scales with (mutants x test time), not with test time, so the groups are very
differently sized: measured on four cores, the fast group takes about four minutes and the
frontend about seven, while `hub` alone — 6582 mutants against the 801-test integration
suite, which starts real hosts and spawns real agent processes — extrapolates to some
seventeen hours from a measured 368-mutant subset. Raise `MUTATION_CONCURRENCY` on a bigger
machine; the cost scales down close to linearly. Targets are independent, and one already
measured against the current tree is skipped next time — so an interrupted run resumes by
being started again, while an edited or rebased one re-runs rather than reporting a score
from a different checkout.

Two things the tool cannot see, both by construction. `Grimoire.ArchTests` is not a target:
its rules assert dependency direction, so mutating production code to see whether they go
red measures nothing. Neither is agent behavior — what an agent decides lives in
`system-prompt.md`, and Stryker mutates code, not prompts (Principle V). Mutation scores
describe the deterministic harness only; the evaluation suite remains the only check on
the other half.

It is not a CI gate, and deliberately not on its way to becoming one. This is a tool
somebody runs by hand every so often to see where the suite is thin: it binds nothing,
gates nothing and sets no threshold, so there is no decision here for an ADR to record —
every config sets `break: 0` and reports rather than fails. Turning a score into a merge
criterion would be the change that needs one.

### Writing new backend tests

- Write tests TDD-style against expected system behavior, not after the fact against
  whatever the implementation happens to do. The binding style is the classicist
  (Chicago-school) rule set in the constitution (Principle II): assert observable
  state — never interactions — use hand-rolled fakes implementing existing port
  interfaces as the only test doubles, and never add a mocking framework.
- Place a new test in the tier that matches what it verifies: hermetic domain/harness
  logic with no external process or real infrastructure belongs in the Fast tier
  (`Grimoire.Domain.UnitTests`, `Grimoire.ArchTests`, or a `Tier=Fast`-tagged
  `Grimoire.AgentEvals` class); anything that starts a real host, SignalR connection, or
  fake/real agent process belongs in `Grimoire.IntegrationTests`; anything that
  exercises agent judgment against a recording belongs in the SlowEval tier.
- Add edge-case coverage only when it is traceable to a concrete user-facing scenario —
  a spec, a functional requirement, or a user story — not speculatively.
- Every deterministic-tier test (Fast/Integration) must wait on the actual condition it
  depends on, not a fixed real-time delay — use
  `Grimoire.IntegrationTests.TestSupport.PollAsync` (bounded, condition-based, fails with
  a clear diagnostic on timeout) rather than `Task.Delay`/`Thread.Sleep`. A test whose own
  subject is genuinely time-based (e.g. verifying a debounce window elapses) may keep a
  real-time wait, but must carry `[Trait("TimingDependent", "true")]` with a one-line
  rationale. `Grimoire.ArchTests`' `DeterministicTierNoFixedWaitRuleTests` enforces this
  in the standard PR pipeline — a fixed unconditional wait outside `PollAsync` or a
  `TimingDependent`-marked test fails CI (ADR-021).

## The development process: Spec-Driven Development

All feature work MUST go through the **Spec Kit** workflow — do not implement features
ad hoc. The mandatory order, gated by the project constitution, is:

1. `/speckit-specify` — capture the feature as user scenarios and requirements
2. `/speckit-clarify` (optional) — resolve ambiguities
3. `/speckit-plan` — generate the technical plan; draft any new ADR a structural change needs
4. **ADR review** — any newly drafted ADR must reach *Accepted* status before tasks are generated
5. `/speckit-tasks` — generate a dependency-ordered task list, starting with a Phase 0 structural boundary test
6. `/speckit-implement` — implement the tasks (Red → Green → Refactor)
7. `/speckit-converge` — validate the Definition of Done

Read [`.specify/memory/constitution.md`](.specify/memory/constitution.md) before opening a
PR — it defines the non-negotiable architectural rules (DDD/hexagonal boundaries, the
agentic-core-vs-deterministic-harness split, observability requirements, testing strategy)
that every change is reviewed against. Architectural decisions live in
[`docs/adr/`](docs/adr/) as MADR records; a structural change without an Accepted ADR
covering it will be rejected in review.

## Document map

Not every markdown file in this repo carries the same authority. Before citing or adding
to a document, check its role in the Document Map in [`CLAUDE.md`](CLAUDE.md) — for
example, `docs/decision-context-overview.md` is background/vision material, not a binding
requirement source; only the constitution and Accepted ADRs are binding.

## Codebase complexity badges

README.md shows a "code complexity" and "est. time to understand" badge, backed by JSON
files under `docs/metrics/`. They aren't regenerated automatically — after a significant
change to `backend/src` or `frontend/src`, refresh them per
[`docs/codebase-complexity-metric.md`](docs/codebase-complexity-metric.md#regenerating-the-badges)
and include the updated files in your PR.

## Branching and versions: GitHub Flow + GitVersion

The branching model is **GitHub Flow** (ADR-027): `main` is always releasable, everything
else is a short-lived branch cut from `main` and merged back through a pull request, then
deleted. There is no `develop`, no release branch, no maintenance line.

Version numbers are **computed, never written down**. [GitVersion](https://gitversion.net)
reads the tags and the history and stamps `Version`, `AssemblyVersion`, `FileVersion` and
`InformationalVersion` on every assembly the build produces — `GitVersion.yml` at the
repository root is its configuration, and `backend/Directory.Build.props` contains no
version literal. Ask any build what it is:

```bash
dotnet msbuild backend/src/Grimoire.Hub/Grimoire.Hub.csproj -t:GetVersion -getProperty:Version -nologo
```

The Hub prints the same number under its logo on every help screen, so a deployed stack can
be identified without the deployment record.

**Only a tagged commit gets a bare release number.** Everything else carries a prerelease
tag, so no untagged build can be mistaken for a release, and no two builds claim the same
version:

| Where you are | What the version looks like |
| --- | --- |
| a tagged commit | `0.0.26` |
| `main`, two commits past the tag | `0.0.26-2` |
| a branch off it | `0.0.26-<branch-name>.1` |
| a pull request, as CI builds it | `0.0.26-PullRequest95.4` |

The prerelease tags sort correctly ahead of the release they lead to (`0.0.26-2` <
`0.0.26`), and the branch name in the label is what keeps two branches at the same commit
height from producing the same version — which matters because CI builds every pull
request.

Three things follow for day-to-day work:

- **Clone with the full history.** GitVersion fails on a shallow clone rather than guessing,
  which is why every workflow that builds checks out with `fetch-depth: 0`. A plain
  `git clone` is already fine.
- **Say when a change is more than a patch.** `main` increments the patch number by default.
  Put `+semver: minor` (or `+semver: major`) in a commit message or in the squashed pull
  request title when it is not.
- **Releasing is tagging.** `git tag 0.0.26 && git push origin 0.0.26` on a `main` commit —
  bare SemVer, no `v` prefix, matching the existing tags. The artifacts built from that
  commit then carry exactly `0.0.26`.

Two things are deliberately outside this and version themselves:
`deploy/server/grimoire-server` carries `GRIMOIRE_SERVER_VERSION` in its own text (it is a
single file that runs outside every checkout, so no build step can stamp it), and
`frontend/package.json` keeps a fixed `0.0.1` because the frontend is not published
separately. ADR-027 has the reasoning for both.

## Pull requests

- One feature branch per Spec Kit feature (`NNN-feature-name`), matching its `specs/NNN-feature-name/` directory
- Large features ship as a **stack** of small PRs rather than one big-bang PR — see below
- CI must pass: architecture tests, integration tests, linting, build
- No unapproved infrastructure — new external dependencies (cloud resources, brokers,
  persistence stores) require an approved ADR first

### Stacked pull requests

A feature's `tasks.md` is already sliced into phases — Phase 0 (structural boundary
tests), setup/foundational, one phase per user story, polish. Those phases are the
natural cut lines for a **stack**: an ordered chain of small pull requests, each
targeting the branch below it, all landing on `main`. Reviewers see one story per diff
instead of an 80-file feature drop, and the MVP story can merge while later stories are
still under review.

Stacks are managed with [`github/gh-stack`](https://github.com/github/gh-stack)
(`gh extension install github/gh-stack`, requires gh ≥ 2.0). Layer branches keep the
feature's numeric prefix (`023-task-ui-improvements-03-us1-status-history`) so branch
validation and the `specs/NNN-*` lookup keep working; feature resolution itself is
branch-independent, so every `/speckit-*` command works from any layer.

Two things to know before splitting: CI runs in full on every layer (cost multiplies by
layer count), and `tasks.md` should be updated in one layer only — per-layer checkbox
edits conflict on every cascading rebase. The Definition of Done stays whole-feature and
is satisfied at the top of the stack.

The full procedure, including layer-cutting rules and the fallback for environments
without `gh`, is in the [`stacked-pr`](.claude/skills/stacked-pr/SKILL.md) skill —
invoke it with `/stacked-pr` when starting implementation of a large feature.
