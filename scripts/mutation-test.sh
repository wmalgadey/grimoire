#!/usr/bin/env bash
# Mutation testing for Grimoire (Stryker.NET + StrykerJS, https://stryker-mutator.io/).
#
# Line coverage answers "was this line executed?"; a mutation score answers the question
# the Definition of Done actually cares about — "would the suite notice if this line were
# wrong?". Stryker rewrites one operator, literal, or branch at a time, reruns the tests
# that cover it, and reports every mutant nobody killed. A survivor is a line the suite
# executes without asserting anything about.
#
# Usage:
#   ./scripts/mutation-test.sh                 # the fast group (default, minutes)
#   ./scripts/mutation-test.sh --group all     # everything (HOURS — see the warning below)
#   ./scripts/mutation-test.sh --only hub      # one target, repeatable
#   ./scripts/mutation-test.sh --list          # what the targets are
#   ./scripts/mutation-test.sh --index         # rebuild the index page, run nothing
#
# Options:
#   --group <fast|backend|frontend|all>  target group (default: fast)
#   --only <name>                        run one target; repeat for several
#   --force                              re-run targets that already have a report
#   --list, --index, --help
#
# Environment:
#   MUTATION_CONCURRENCY           parallel test runners for the .NET lane (default: nproc/2)
#   MUTATION_FRONTEND_CONCURRENCY  same for the frontend lane (default: 2 — see below)
#
# Output: docs/reports/mutation/<target>/ — one Stryker HTML report per target plus the
# raw JSON, and docs/reports/mutation/index.html linking them with their scores. The
# directory is gitignored: a full run writes tens of megabytes of generated HTML.
#
# HOW LONG. Mutation testing runs the covering tests once per mutant, so the cost is
# (mutants x test time), not (test time). Measured on this repository, 4 cores:
#
#   fast group (1233 mutants, 93 unit tests)              ~4 min
#   frontend   (1200 mutants, 248 Vitest tests)           ~7 min
#   hub        (6582 mutants against the 801-test
#               integration suite, which starts real
#               hosts and spawns real agent processes)    ~17 h, extrapolated from a
#                                                         measured 368-mutant subset
#
# The backend group is an overnight job on a small machine. Raise MUTATION_CONCURRENCY on
# a big one — the cost scales down close to linearly. Targets are independent, and one
# already measured against the current tree is skipped on the next invocation, so an
# interrupted run resumes by being started again. Change anything the run measures — a
# commit, a rebase, an edit — and it re-runs instead of reporting yesterday's score under
# today's name.
#
# NOT A GATE. .github/workflows/mutation.yml runs the fast group on pull requests that
# touch what it mutates and posts the score as a PR comment; every other group runs by
# hand, because none of them fits in a pull request's lifetime. Either way nothing here
# binds anything: every config sets break: 0, so no score fails a job. Turning one into a
# merge criterion is the change that would need an ADR.
set -eo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

OUT_DIR="docs/reports/mutation"

# name | lane | working directory | --project (dotnet lane) | extra arguments
TARGETS=(
  "domain|dotnet|backend/tests/Grimoire.Domain.UnitTests|Grimoire.Domain.csproj|"
  "remediation-state-machine|dotnet|backend/tests/Grimoire.Domain.UnitTests|Grimoire.Hub.csproj|--mutate **/RemediationTasks/RemediationActionTask.cs"
  "hub|dotnet|backend/tests/Grimoire.IntegrationTests|Grimoire.Hub.csproj|"
  "agent-runtime|dotnet|backend/tests/Grimoire.IntegrationTests|Grimoire.AgentRuntime.csproj|"
  "agent-runtime-guardrails|dotnet|backend/tests/Grimoire.IntegrationTests|Grimoire.AgentRuntime.csproj|--mutate **/Guardrails/*.cs --mutate **/Guardrails/Coordination/*.cs"
  "ingest-agent|dotnet|backend/tests/Grimoire.IntegrationTests|Grimoire.IngestAgent.csproj|"
  "query-agent|dotnet|backend/tests/Grimoire.IntegrationTests|Grimoire.QueryAgent.csproj|"
  "lint-agent|dotnet|backend/tests/Grimoire.IntegrationTests|Grimoire.LintAgent.csproj|"
  "eval-runner|dotnet|backend/tests/Grimoire.IntegrationTests|Grimoire.EvalRunner.csproj|"
  "frontend|node|frontend|-|"
)

# Deliberately not targets:
#   Grimoire.ArchTests   — structural rules over assemblies. Mutating production code to
#                          see whether an architecture test goes red measures nothing:
#                          the rules assert dependency direction, not behavior.
#   Grimoire.AgentEvals  — agent judgment against recordings (Constitution Principle II).
#                          What it verifies lives in instruction files, and Stryker cannot
#                          mutate a markdown prompt. Its hermetic harness-mechanics subset
#                          is covered where that harness code lives (eval-runner).
#   *.svelte components  — StrykerJS mutates .js/.ts, not Svelte templates. The frontend
#                          target covers src/**/*.ts, which is where the logic is.

group_for() {
  case "$1" in
    domain|remediation-state-machine) echo "fast backend all" ;;
    frontend) echo "frontend all" ;;
    *) echo "backend all" ;;
  esac
}

usage() { sed -n '2,/^set -eo/p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//; $d'; }

list_targets() {
  printf '%-28s %-9s %s\n' TARGET LANE GROUPS
  local line name lane
  for line in "${TARGETS[@]}"; do
    IFS='|' read -r name lane _ _ _ <<<"$line"
    printf '%-28s %-9s %s\n' "$name" "$lane" "$(group_for "$name")"
  done
}

group=fast
force=0
declare -a only=()
while [ $# -gt 0 ]; do
  case "$1" in
    --group) group="$2"; shift 2 ;;
    --only) only+=("$2"); shift 2 ;;
    --force) force=1; shift ;;
    --list) list_targets; exit 0 ;;
    --index) python3 scripts/mutation-report-index.py "$OUT_DIR"; exit 0 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

sha256() { command -v sha256sum >/dev/null && sha256sum || shasum -a 256; }

# What a report is a report *of*. A target is only resumable while the tree it was measured
# against is unchanged: after a checkout, a rebase, or an edit, the presence of a report
# says nothing about the current code, and skipping on presence alone would put a score
# from another commit on the index page under today's name.
fingerprint() {
  {
    git rev-parse HEAD 2>/dev/null || echo "no-git"
    git status --porcelain=v1 2>/dev/null
  } | sha256 | cut -d' ' -f1
}

cores="$( (command -v nproc >/dev/null && nproc) || sysctl -n hw.ncpu 2>/dev/null || echo 4)"
concurrency="${MUTATION_CONCURRENCY:-$(( cores / 2 > 0 ? cores / 2 : 1 ))}"
# The frontend lane defaults lower than the .NET one on purpose: every Stryker worker
# starts its own Vitest, and they share one Vite dependency-optimizer cache under
# frontend/node_modules/.vite. Several workers re-optimizing at once corrupt it, and the
# run dies with a nonsense ESM error ("does not provide an export named ...") rather than
# a failed test. Two workers do not race.
frontend_concurrency="${MUTATION_FRONTEND_CONCURRENCY:-2}"

selected=()
for line in "${TARGETS[@]}"; do
  IFS='|' read -r name _ _ _ _ <<<"$line"
  if [ "${#only[@]}" -gt 0 ]; then
    for want in "${only[@]}"; do
      [ "$want" = "$name" ] && selected+=("$line")
    done
  elif [[ " $(group_for "$name") " == *" $group "* ]]; then
    selected+=("$line")
  fi
done

if [ "${#selected[@]}" -eq 0 ]; then
  echo "no target matched (group '$group'${only[*]:+, --only ${only[*]}})." >&2
  list_targets >&2
  exit 2
fi

echo "Targets:   ${#selected[@]}   concurrency: $concurrency (.NET) / $frontend_concurrency (frontend)"
echo "Reports:   $OUT_DIR"
case "$group" in
  all|backend) echo "NOTE: the backend targets take hours. See the header of this script." ;;
esac
echo

mkdir -p "$OUT_DIR"

if [ -n "$(printf '%s\n' "${selected[@]}" | grep '|dotnet|' || true)" ]; then
  dotnet tool restore
fi

current_fingerprint="$(fingerprint)"
declare -a failed=()
declare -a suspect=()
for line in "${selected[@]}"; do
  IFS='|' read -r name lane dir project extra <<<"$line"
  target_out="$OUT_DIR/$name"

  if [ -n "$(find "$target_out" -name 'mutation-report.json' 2>/dev/null)" ]; then
    if [ "$force" -eq 1 ]; then
      : # asked for explicitly
    elif [ "$(cat "$target_out/fingerprint" 2>/dev/null)" = "$current_fingerprint" ]; then
      echo "==> $name — skipped, already measured against this tree (--force to re-run)"
      continue
    else
      echo "==> $name — report is from a different tree, re-running"
    fi
  fi

  echo "==> $name  ($lane, $dir)"
  rm -rf "${target_out:?}"
  mkdir -p "$target_out"
  started=$SECONDS

  # read -ra never globs, so the --mutate pattern reaches Stryker unexpanded.
  read -ra extra_args <<<"$extra"

  if [ "$lane" = dotnet ]; then
    ( cd "$dir" && dotnet stryker \
        --project "$project" \
        --output "$REPO_ROOT/$target_out" \
        --concurrency "$concurrency" \
        --skip-version-check \
        "${extra_args[@]}" ) 2>&1 | tee "$target_out/run.log" || failed+=("$name")
  else
    # Unconditional: inside the container wrapper node_modules is a cache directory that
    # outlives the checkout, so "the directory is there" does not mean "it matches the
    # lockfile in front of me". `bun install --frozen-lockfile` is a fast no-op when it
    # already does, and the only thing that notices a dependency bump when it does not.
    ( cd "$dir" && bun install --frozen-lockfile )
    ( cd "$dir" && bunx stryker run \
        --concurrency "$frontend_concurrency" \
        "${extra_args[@]}" ) 2>&1 | tee "$target_out/run.log" || failed+=("$name")
  fi

  if [ -n "$(find "$target_out" -name 'mutation-report.json' 2>/dev/null)" ]; then
    echo "$current_fingerprint" > "$target_out/fingerprint"
  fi

  # A test that was already red before Stryker touched anything takes its mutants with it:
  # they are reported as "only covered by failing tests" and silently leave the score,
  # which then looks like an ordinary number. The runs deliberately do not abort on this
  # (break-on-initial-test-failure stays off, so one flaky test cannot end a seventeen-hour
  # run), so the score has to be labelled instead of trusted.
  if grep -q "only covered by failing tests" "$target_out/run.log" 2>/dev/null; then
    echo "    WARNING: $name had failing tests in its baseline run — its score covers fewer"
    echo "             mutants than it claims. Fix the suite and re-run with --force."
    suspect+=("$name")
  fi

  echo "    $name finished after $(( (SECONDS - started) / 60 )) min"
  echo
done

python3 scripts/mutation-report-index.py "$OUT_DIR"

if [ "${#suspect[@]}" -gt 0 ]; then
  echo "Scores measured against a failing baseline: ${suspect[*]}" >&2
fi

if [ "${#failed[@]}" -gt 0 ]; then
  echo "Targets that did not complete: ${failed[*]}" >&2
  exit 1
fi
