#!/usr/bin/env bash
# Mutation score for the guardrail surface (Grimoire.AgentRuntime/Guardrails/**).
#
# Scoped through a config file rather than CLI --mutate globs on purpose: the filter is
# applied at test-selection time, not at compile time, so a large "mutants created" count
# and a compile warning about a file outside the filter are both expected and do NOT mean
# the scoping failed. Look for "Removed by mutate filter" to see it working.
#
# Roughly 24 min on 4 cores. Baseline discovery finds 987 tests, not 911, because
# Grimoire.IntegrationTests still references Grimoire.AgentEvals (see issue #180).
#
# Usage: ./scripts/mutation-tests/run-guardrails-stryker.sh [output-dir]
set -eo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${1:-$REPO_ROOT/docs/reports/mutation/agent-runtime-guardrails}"
mkdir -p "$OUT"
cd "$REPO_ROOT/backend/tests/Grimoire.IntegrationTests"
exec dotnet stryker \
  --config-file "$REPO_ROOT/scripts/mutation-tests/stryker-guardrails-config.json" \
  --output "$OUT" \
  --concurrency "${MUTATION_CONCURRENCY:-4}" \
  --skip-version-check
