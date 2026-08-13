#!/bin/bash
# Fixture-based test for scripts/ci/compute-codebase-metrics: given the same sample lizard
# CSV used by format-complexity-report.test.sh, the script must aggregate the expected
# totals and emit the two shields.io endpoint badge JSON files consumed by README.md.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fixture="$script_dir/fixtures/sample-complexity.csv"
out_dir="$(mktemp -d)"
trap 'rm -rf "$out_dir"' EXIT

# Fixture totals: NLOC 63+41+23+12+6+5=150, CCN 24+18+9+5+3+2=61 -> avg CCN 10.1(6) ->
# "High" band (10.0 < 10.17 <= 15.0). Factor = 10.1(6)/5.0 = 2.03(3), hours = 150/400*2.03(3)
# = 0.7625 -> < 1 working day.
actual="$("$script_dir/compute-codebase-metrics" "$fixture" --out-dir "$out_dir")"

failures=0

assert_contains() {
  local haystack="$1" needle="$2" label="$3"
  if [[ "$haystack" != *"$needle"* ]]; then
    echo "FAIL ($label): expected to contain: $needle" >&2
    echo "--- actual ---" >&2
    echo "$haystack" >&2
    failures=$((failures + 1))
  fi
}

assert_contains "$actual" "6 functions" "stdout summary"
assert_contains "$actual" "150 NLOC" "stdout summary"
assert_contains "$actual" "avg CCN 10.17 (High)" "stdout summary"
assert_contains "$actual" "< 1 working day" "stdout summary"

complexity_json="$(cat "$out_dir/complexity-badge.json")"
assert_contains "$complexity_json" '"label": "code complexity"' "complexity badge"
assert_contains "$complexity_json" '"message": "High (avg CCN 10.2)"' "complexity badge"
assert_contains "$complexity_json" '"color": "orange"' "complexity badge"
assert_contains "$complexity_json" '"schemaVersion": 1' "complexity badge"

time_json="$(cat "$out_dir/understanding-time-badge.json")"
assert_contains "$time_json" '"label": "est. time to understand"' "time badge"
assert_contains "$time_json" '"message": "< 1 working day"' "time badge"
assert_contains "$time_json" '"color": "blue"' "time badge"

# A codebase-scale sanity check with a larger, more realistic average CCN, to catch
# regressions in the day/week formatting thresholds that the tiny fixture can't exercise.
large_csv="$(mktemp)"
trap 'rm -rf "$out_dir" "$large_csv"' EXIT
{
  # 40 functions, 200 NLOC and CCN 6 each -> 8000 NLOC total, avg CCN 6.0 (Moderate).
  # factor = 6.0/5.0 = 1.2, hours = 8000/400*1.2 = 24 -> 3 working days.
  for i in $(seq 1 40); do
    echo "200,6,0,0,0,\"loc\",\"file$i.cs\",\"Func$i\",\"Func$i()\",1,200"
  done
} > "$large_csv"

large_actual="$("$script_dir/compute-codebase-metrics" "$large_csv" --out-dir "$out_dir")"
assert_contains "$large_actual" "avg CCN 6.00 (Moderate)" "large-sample stdout summary"
assert_contains "$large_actual" "~3 working days" "large-sample stdout summary"

if [[ "$failures" -gt 0 ]]; then
  echo "FAILED: $failures assertion(s) did not hold." >&2
  exit 1
fi

echo "OK: compute-codebase-metrics aggregates totals and emits both badge JSON files."
