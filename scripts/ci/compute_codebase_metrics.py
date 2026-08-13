#!/usr/bin/env python3
"""
Aggregates `lizard --csv` output (the same CSV format `.github/workflows/complexity.yml`
already produces) into two whole-codebase metrics rendered as shields.io endpoint badges
in README.md:

  * codebase complexity — average cyclomatic complexity (CCN) per function, rated against
    the same bands docs/code-complexity-analysis.md and the CI complexity gate already use.
  * estimated time to understand — a heuristic reading-time estimate: total NLOC divided by
    a cited code-comprehension reading rate, scaled up for higher average complexity.

Full methodology, thresholds, and sources are documented in
docs/codebase-complexity-metric.md — this script only implements the arithmetic described
there.

Expected CSV columns (lizard 1.x, no header row):
    nloc, ccn, token_count, param_count, length, location, file, name, long_name,
    start_line, end_line
"""

import argparse
import csv
import json
import sys

# Same CC bands as the "Cyclomatic complexity (per function)" row in
# docs/code-complexity-analysis.md, applied here to the whole-codebase average instead of
# a single function. The 15.0 ceiling also matches CCN_THRESHOLD in complexity.yml.
RATING_BANDS = [
    (5.0, "Low", "brightgreen"),
    (10.0, "Moderate", "yellow"),
    (15.0, "High", "orange"),
]
VERY_HIGH_RATING = ("Very High", "red")

# Reading rate for careful, comprehension-level code reading — see
# docs/codebase-complexity-metric.md for the cited source and reasoning.
READING_RATE_NLOC_PER_HOUR = 400.0
# The time multiplier stays at 1x up to this average CCN, then scales linearly, capped at
# COMPLEXITY_FACTOR_CAP once avg CCN reaches the same 15.0 threshold used above.
LOW_COMPLEXITY_CEILING = 5.0
COMPLEXITY_FACTOR_CAP = 3.0


def _parse_rows(csv_path):
    rows = []
    with open(csv_path, newline="", encoding="utf-8") as handle:
        for record in csv.reader(handle):
            if len(record) < 11:
                continue
            try:
                rows.append({"nloc": int(record[0]), "complexity": int(record[1])})
            except ValueError:
                # Header row or other non-numeric record (varies across lizard versions).
                continue
    return rows


def _rating(avg_complexity):
    for ceiling, label, color in RATING_BANDS:
        if avg_complexity <= ceiling:
            return label, color
    return VERY_HIGH_RATING


def _complexity_factor(avg_complexity):
    factor = avg_complexity / LOW_COMPLEXITY_CEILING
    return max(1.0, min(COMPLEXITY_FACTOR_CAP, factor))


def _format_duration(hours):
    days = hours / 8.0
    if days < 1:
        return "< 1 working day"
    if days <= 10:
        n = max(1, round(days))
        return f"~{n} working day" + ("s" if n != 1 else "")
    weeks = days / 5.0
    n = max(1, round(weeks))
    return f"~{n} week" + ("s" if n != 1 else "")


def compute_metrics(csv_path):
    rows = _parse_rows(csv_path)
    if not rows:
        raise ValueError(f"No functions parsed from {csv_path} — is the lizard CSV empty?")

    function_count = len(rows)
    total_nloc = sum(row["nloc"] for row in rows)
    avg_complexity = sum(row["complexity"] for row in rows) / function_count

    rating, color = _rating(avg_complexity)
    factor = _complexity_factor(avg_complexity)
    hours = (total_nloc / READING_RATE_NLOC_PER_HOUR) * factor

    return {
        "function_count": function_count,
        "total_nloc": total_nloc,
        "avg_complexity": avg_complexity,
        "rating": rating,
        "rating_color": color,
        "hours": hours,
        "duration": _format_duration(hours),
    }


def _write_badge(path, label, message, color):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump({"schemaVersion": 1, "label": label, "message": message, "color": color}, handle, indent=2)
        handle.write("\n")


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("csv_path", help="Path to the `lizard --csv` output file")
    parser.add_argument("--out-dir", required=True, help="Directory to write the two badge JSON files into")
    args = parser.parse_args(argv)

    metrics = compute_metrics(args.csv_path)

    _write_badge(
        f"{args.out_dir}/complexity-badge.json",
        "code complexity",
        f"{metrics['rating']} (avg CCN {metrics['avg_complexity']:.1f})",
        metrics["rating_color"],
    )
    _write_badge(
        f"{args.out_dir}/understanding-time-badge.json",
        "est. time to understand",
        metrics["duration"],
        "blue",
    )

    sys.stdout.write(
        f"{metrics['function_count']:,} functions · {metrics['total_nloc']:,} NLOC · "
        f"avg CCN {metrics['avg_complexity']:.2f} ({metrics['rating']}) · "
        f"estimated understanding time: {metrics['duration']}\n"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
