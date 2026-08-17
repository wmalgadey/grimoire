---
status: accepted
---

# ADR-027: Version Numbers Computed by GitVersion, Branching by GitHub Flow

## Context and Problem Statement

Grimoire has three version numbers and none of them means anything.

`backend/Directory.Build.props` declares `<Version>1.0.0</Version>`,
`<AssemblyVersion>1.0.0.0</AssemblyVersion>` and `<FileVersion>1.0.0.0</FileVersion>` as
literals. Every assembly the solution has ever produced — the Hub, the three agent
runtimes, the eval runner — carries `1.0.0`, and has since the first commit. The Hub goes
out of its way to display it: `HubCliHelpProvider.LogoVersion` reads
`AssemblyInformationalVersionAttribute` and prints it under the logo on every help screen,
so an operator asking a deployed Hub which version it is gets a confident, permanent
`1.0.0`. `frontend/package.json` says `0.0.1`, equally frozen. The deployment tool
`deploy/server/grimoire-server` carries no version at all.

Meanwhile the repository *has* a real version line: 25 tags, `0.0.1` through `0.0.25`,
bare SemVer with no `v` prefix. Nothing in the build reads them. The information exists in
git and stops there.

The branching model has the same shape — real in practice, unwritten. Every feature branch
is cut from `main`, merged back through a pull request, and deleted; there is no `develop`,
no release branch, no maintenance line. That is GitHub Flow, and CONTRIBUTING.md describes
its consequences (one branch per Spec Kit feature, stacked PRs, CI on every layer) without
ever naming the model or saying what a release is.

Three concrete things follow from the gap:

1. **A deployed stack cannot be identified.** `grimoire-server deploy` records the commit
   it deployed, but the running Hub reports `1.0.0`. An operator with a Hub in front of
   them and no access to the deployment record has no way to find out what they are
   looking at.
2. **A tag decides nothing.** Tagging `0.0.26` produces artifacts indistinguishable from
   the ones built the day before. The tag is a bookmark, not a release.
3. **There is no rule for what a version increment means.** Nothing in the repository says
   when the minor number moves, so nothing can be inferred from the fact that it did.

## Decision Drivers

- The version must be **derived, not declared**: a number written into a file is a second
  source of truth, and it is the one that goes stale (all three current ones did).
- The build must keep working where there is no git repository to read — the container
  image build has none by design (`.dockerignore` excludes `.git/`, which is what keeps the
  build context small and the layer cache from invalidating on every commit).
- The branching model already in use should be **named and made explicit**, not replaced.
  A model change would invalidate the stacked-PR convention and the Spec Kit branch
  naming, neither of which is broken.
- Per Constitution Principle IV, whatever is decided here needs a CI gate. The failure mode
  of a versioning setup is silent: it keeps compiling and stamps a wrong number.

## Considered Options

1. **GitVersion, configured for GitHub Flow.** Version computed from tags, branch and
   commit history at build time.
2. **Nerdbank.GitVersioning.** Equivalent derivation, but its source of truth is a
   `version.json` file that is bumped by hand and tagged by tooling — a declared number
   again, with git supplying only the height.
3. **MinVer.** Tag-only derivation, very small surface. No branch awareness at all: two
   branches on the same commit count produce the same version, which for a repository that
   builds every pull request in CI means colliding prerelease identities.
4. **Keep hand-maintained literals, bump them in review.** No new dependency; the failure
   mode already observed (three numbers, all frozen, for the entire life of the project)
   is the argument against it.

## Decision Outcome

**Chosen: option 1 — GitVersion, in GitHub Flow mode, as the single source of every version
number the build produces.**

### V1 — The version is computed, never declared *(Feature-Scoped Invariant)*

`GitVersion.yml` at the repository root configures `workflow: GitHubFlow/v1`.
`backend/Directory.Build.props` references `GitVersion.MsBuild` and declares no version
literal: `Version`, `AssemblyVersion`, `FileVersion` and `InformationalVersion` all come
from the git history.

What that produces, given the existing tags:

| Where | Version |
| --- | --- |
| the commit tagged `0.0.25` | `0.0.25` |
| two commits past it on `main` | `0.0.26-2` |
| a branch `claude/some-slug` off it | `0.0.26-claude-some-slug.1` |
| a branch `027-new-feature` off it | `0.0.26-027-new-feature.1` |
| a branch whose name matches no prefix | `0.0.26-<branch-name>.1` |
| a pull request, as `ci.yml` builds it | `0.0.26-PullRequest95.4` |

**A bare release number is reserved for a tagged commit.** Every other build carries a
prerelease tag — including `main`, which between tags is a prerelease of the version it
will carry when tagged and sorts correctly ahead of it (`0.0.26-2` < `0.0.26`). Two
properties follow, and both are the point of the table rather than incidental to it:

- No untagged artifact can be mistaken for a release, in a repository where the deployment
  tool will happily put any ref on a server.
- No two builds claim the same version. The branch name in the label is what does that
  work — CI builds every pull request, so two branches at the same commit height are the
  normal case, not an edge one.

The last row is the shape that actually runs in CI: GitHub checks a pull request out as a
detached merge ref rather than as a branch, and GitVersion classifies it from that ref
rather than from a branch name it cannot see. It is listed because it is the version most
builds of this repository will ever carry, and it is not derivable from the rows above it.

This is a **Feature-Scoped Invariant**, not a Boundary Rule: it constrains the shape of the
build's configuration, not a dependency direction between layers. Per Principle III it is
therefore verified by a behavioural check — the CI step under V3 — and MUST NOT be given a
reflection- or IL-based structural test.

### V2 — Increments are announced in the commit message

GitHub Flow has no release branch to carry the information that a change is more than a
patch, so the commit does: `+semver: minor` or `+semver: major` in a commit message or in
the squashed pull request title moves that number. Without one, `main` increments the
patch. A release is `git tag <version>` on a `main` commit — that is the whole ceremony,
and it is what makes the tag mean something.

### V3 — CI fails when the version stops being computed *(Feature-Scoped Invariant)*

GitVersion needs history and tags; a shallow clone has neither. Every workflow that builds
checks out with `fetch-depth: 0`, and `ci.yml` asserts that the computed version is not the
no-repository fallback. Without this the regression is invisible: the build succeeds and
stamps `0.0.0-nogit` into every assembly.

### V4 — The container image receives its version as a build argument

The image build has no repository to read, and giving it one would mean shipping `.git`
into the build context. `backend/Directory.Build.props` detects the absent repository and
steps aside (`DisableGitVersionTask`), falling back to an explicit `0.0.0-nogit` rather
than to a plausible-looking number. `deploy/Dockerfile` takes `ARG GRIMOIRE_VERSION` and
passes it as `-p:InformationalVersion=`; `compose.yaml` wires it through;
`grimoire-server deploy` sets it from `git describe` of the checkout being deployed. Only
`InformationalVersion` travels, because it is a free-form string: a caller that can only
say `git describe` does not fail the build for saying so.

### V5 — `grimoire-server` versions itself

`deploy/server/grimoire-server` is a single shell file that `install` copies to
`~/.local/bin`, where it runs outside every checkout and every build. There is no step in
its life at which GitVersion could stamp it, so it carries `GRIMOIRE_SERVER_VERSION` in its
text, bumped by hand — the one exception to V1, and confined to it. Provenance is recorded
rather than inferred: `install` and `update` write the commit the copy came from into the
operator state directory, and `grimoire-server version` reports the tool version, that
commit, and the deployed commit as three separate facts.

### No Boundary Rule

This ADR introduces no Dependency & Layering Boundary Rule, and therefore requires no
Phase 0 structural test.

### Consequences

- Good, because a deployed Hub answers "which version are you?" truthfully, on the help
  screen it already prints, with no new surface.
- Good, because tagging a commit on `main` is now a release, and the artifacts built from it
  say so — while every untagged build carries a prerelease tag and cannot be mistaken for
  one.
- Good, because branch and pull-request builds carry distinguishable versions, which is what
  makes a prerelease artifact from a pull request usable at all.
- Good, because the branching model is named, so the stacked-PR convention and the Spec Kit
  branch naming have something to be consistent *with*.
- Bad, because every workflow that builds now clones the full history. At this repository's
  size that is seconds; at ten times the size it would be worth revisiting.
- Bad, because GitVersion runs on every build, including local incremental ones, and adds
  roughly a second.
- Bad, because `+semver:` in a commit message is a convention a contributor can forget.
  Forgetting it produces a patch increment where a minor was meant — recoverable by tagging
  the intended version explicitly, which GitVersion then takes as authoritative.
- Bad, because `grimoire-server`'s version is hand-maintained and can drift from reality.
  The provenance record under V5 is the mitigation: the commit it was copied from is
  recorded, and that cannot drift.
- Neutral, because `frontend/package.json` keeps its `0.0.1`. The frontend is not published
  as a package — it is built into the Hub's `wwwroot` and versioned by the image that
  carries it — so wiring GitVersion into it would produce a number nothing reads. If the
  frontend ever ships independently, that is the ADR that revisits this.

## Review

Scope: internal build and packaging. Per Constitution Principle III ("Review cadence"),
review at least every 365 days.
