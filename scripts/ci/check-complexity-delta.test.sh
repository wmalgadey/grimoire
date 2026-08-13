#!/bin/bash
# Fixture-based test for scripts/ci/check-complexity-delta: given base and head lizard
# CSVs, the gate must fail (exit 1) exactly for functions the PR adds above the CCN
# threshold or worsens while above it — never for pre-existing, unchanged complexity —
# and must render the markdown gate section consumed via format-complexity-report
# --gate-file by .github/workflows/complexity.yml.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
base="$script_dir/fixtures/sample-complexity-base.csv"
head="$script_dir/fixtures/sample-complexity-head.csv"

failures=0

assert_contains() {
  local haystack="$1" needle="$2"
  if [[ "$haystack" != *"$needle"* ]]; then
    echo "FAIL: expected output to contain: $needle" >&2
    failures=$((failures + 1))
  fi
}

assert_not_contains() {
  local haystack="$1" needle="$2" reason="$3"
  if [[ "$haystack" == *"$needle"* ]]; then
    echo "FAIL: $reason" >&2
    failures=$((failures + 1))
  fi
}

# --- Failing gate: a new above-threshold function and a worsened above-threshold one.
set +e
gate_output="$("$script_dir/check-complexity-delta" "$base" "$head" --threshold 15)"
gate_exit=$?
set -e

if [[ "$gate_exit" -ne 1 ]]; then
  echo "FAIL: expected exit code 1 for a failing gate, got $gate_exit" >&2
  failures=$((failures + 1))
fi

assert_contains "$gate_output" "### Complexity gate"
assert_contains "$gate_output" "❌ **Failed** — this PR adds or worsens 2 function(s)"
# New function above the threshold.
assert_contains "$gate_output" "| \`HubCoordinator::DispatchAsync\` | \`backend/src/Grimoire.Hub/HubCoordinator.cs:30\` | – (new) | **18** |"
# Existing above-threshold function whose CCN increased.
assert_contains "$gate_output" "| \`LegacyImporter::Import\` | \`backend/src/Grimoire.Hub/LegacyImporter.cs:10\` | 16 | **20** |"
# Unchanged pre-existing complexity (CCN 24 on both sides) must not be gated.
assert_not_contains "$gate_output" "TaskArtifactStore::ToString" \
  "unchanged above-threshold function must not appear as a violation"
# Growth that stays below the threshold (4 -> 9) must not be gated.
assert_not_contains "$gate_output" "loadPage" \
  "below-threshold growth must not appear as a violation"

# --- Passing gate: with a high threshold, nothing violates and the exit code is 0.
pass_output="$("$script_dir/check-complexity-delta" "$base" "$head" --threshold 100)"
assert_contains "$pass_output" "✅ **Passed**"

# --- The formatter must embed the gate section before its footer.
gate_file="$(mktemp)"
trap 'rm -f "$gate_file"' EXIT
printf '%s\n' "$gate_output" > "$gate_file"
report="$("$script_dir/format-complexity-report" "$head" --top 3 --threshold 15 --gate-file "$gate_file")"
assert_contains "$report" "### Complexity gate"
assert_contains "$report" "❌ **Failed**"

if [[ "$failures" -gt 0 ]]; then
  echo "--- gate output ---" >&2
  echo "$gate_output" >&2
  echo "FAILED: $failures assertion(s) did not hold." >&2
  exit 1
fi

echo "OK: check-complexity-delta gates only PR-introduced complexity regressions."
