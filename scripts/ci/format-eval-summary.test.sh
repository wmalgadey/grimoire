#!/bin/bash
# T018 (US2) — fixture-based test for scripts/ci/format-eval-summary: given a sample
# eval-results file with mixed pass/fail scores, the formatter must produce the expected
# per-test pass/fail + scores markdown (consumed as both the PR-comment body and the job
# summary — see contracts/eval-workflow-dispatch.md).
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fixture="$script_dir/fixtures/sample-eval-results.trx"

actual="$("$script_dir/format-eval-summary" "$fixture")"

failures=0

assert_contains() {
  local needle="$1"
  if [[ "$actual" != *"$needle"* ]]; then
    echo "FAIL: expected output to contain: $needle" >&2
    failures=$((failures + 1))
  fi
}

assert_contains "## Agent Eval Results"
assert_contains "CatalogDiscoverabilityEvals.SC008_TouchedPages_AreDiscoverableFromIndex_AtLeast95Percent"
assert_contains "ConventionAdherenceEvals.SC007_ProducedPages_FollowInstructionConventions_AtLeast95Percent"
assert_contains "UpdateOverDuplicateEvals.SC006_OverlappingSources_UpdateOrSupersedeRate_IsAtLeast90Percent"
assert_contains "SteeringAdoptionEvals.SC007_SteeredRuns_ReflectTheRequestedFocus_AtLeast90Percent"
assert_contains "✅ Passed"
assert_contains "❌ Failed"
assert_contains "Success rate: 66.7% (2/3)."
assert_contains "Success rate: 40.0% (2/5)."
assert_contains "2 passed, 2 failed, 4 total"

if [[ "$failures" -gt 0 ]]; then
  echo "--- actual output ---" >&2
  echo "$actual" >&2
  echo "FAILED: $failures assertion(s) did not hold." >&2
  exit 1
fi

echo "OK: format-eval-summary produces the expected pass/fail + scores markdown."
