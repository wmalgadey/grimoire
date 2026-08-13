#!/bin/bash
# Fixture-based test for scripts/ci/format-complexity-report: given a sample lizard CSV
# with functions above and below the CCN warning threshold, the formatter must produce
# the marker-tagged markdown report (consumed as both the PR-comment body and the job
# summary by .github/workflows/complexity.yml), ranked by CCN descending.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fixture="$script_dir/fixtures/sample-complexity.csv"

actual="$("$script_dir/format-complexity-report" "$fixture" --top 5 --threshold 15 --commit abc1234)"

failures=0

assert_contains() {
  local needle="$1"
  if [[ "$actual" != *"$needle"* ]]; then
    echo "FAIL: expected output to contain: $needle" >&2
    failures=$((failures + 1))
  fi
}

# The stable marker is what lets consecutive workflow runs find and update the comment.
assert_contains "<!-- grimoire-complexity-report -->"
assert_contains "## Code Complexity Report"
assert_contains "**6** functions analyzed"
assert_contains "**2** above the warning threshold (CCN > 15)"
assert_contains "### Top 5 most complex functions"
# Rank 1 and 2 by CCN, flagged as above-threshold.
assert_contains "| 1 | ⚠️ **24** | 63 | 0 | \`TaskArtifactStore::ToString\` | \`backend/src/Grimoire.IngestAgent/TaskArtifact/TaskArtifactStore.cs:47\` |"
assert_contains "| 2 | ⚠️ **18** | 41 | 2 | \`HubCoordinator::DispatchAsync\` | \`backend/src/Grimoire.Hub/HubCoordinator.cs:30\` |"
# Below-threshold rows render a plain CCN.
assert_contains "| 3 | 9 | 23 | 1 | \`loadPage\` | \`frontend/src/lib/api.ts:12\` |"
assert_contains "for commit \`abc1234\`"

# --top must truncate: rank 6 exists in the fixture but must not be listed.
if [[ "$actual" == *"BuildPolicyJson"* ]]; then
  echo "FAIL: expected --top 5 to drop the 6th-ranked function (BuildPolicyJson)." >&2
  failures=$((failures + 1))
fi

if [[ "$failures" -gt 0 ]]; then
  echo "--- actual output ---" >&2
  echo "$actual" >&2
  echo "FAILED: $failures assertion(s) did not hold." >&2
  exit 1
fi

echo "OK: format-complexity-report produces the expected ranked markdown report."
