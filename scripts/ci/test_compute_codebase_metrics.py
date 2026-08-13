"""
Pytest tests for compute_codebase_metrics.py: the arithmetic that aggregates a lizard CSV
export into the two shields.io endpoint badges shown in README.md.

Reuses scripts/ci/fixtures/sample-complexity.csv, the same fixture
format-complexity-report.test.sh and check-complexity-delta.test.sh already parse, so all
three self-tests agree on one sample dataset.
"""

import csv
import json
from pathlib import Path

import pytest

import compute_codebase_metrics as m

FIXTURES_DIR = Path(__file__).parent / "fixtures"
SAMPLE_CSV = FIXTURES_DIR / "sample-complexity.csv"


def _write_csv(path, rows):
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        for row in rows:
            writer.writerow(row)


# --- _parse_rows -------------------------------------------------------------------


def test_parse_rows_reads_nloc_and_complexity_from_sample_fixture():
    rows = m._parse_rows(SAMPLE_CSV)

    assert len(rows) == 6
    assert rows[0] == {"nloc": 63, "complexity": 24}
    assert rows[-1] == {"nloc": 5, "complexity": 2}


def test_parse_rows_skips_short_and_non_numeric_records(tmp_path):
    csv_path = tmp_path / "mixed.csv"
    _write_csv(
        csv_path,
        [
            ["NLOC", "CCN", "token", "PARAM", "length", "loc", "file", "func", "long", "s", "e"],
            ["not-a-number", "1", "0", "0", "0", "loc", "f.cs", "F", "F()", "1", "2"],
            ["too", "short"],
            [10, 2, 0, 0, 0, "loc", "f.cs", "G", "G()", 1, 2],
        ],
    )

    rows = m._parse_rows(csv_path)

    assert rows == [{"nloc": 10, "complexity": 2}]


# --- _rating -------------------------------------------------------------------------


@pytest.mark.parametrize(
    "avg_complexity,expected",
    [
        (0.0, ("Low", "brightgreen")),
        (5.0, ("Low", "brightgreen")),
        (5.1, ("Moderate", "yellow")),
        (10.0, ("Moderate", "yellow")),
        (10.1, ("High", "orange")),
        (15.0, ("High", "orange")),
        (15.1, ("Very High", "red")),
    ],
)
def test_rating_bands_match_docs_code_complexity_analysis_thresholds(avg_complexity, expected):
    assert m._rating(avg_complexity) == expected


# --- _complexity_factor ---------------------------------------------------------------


@pytest.mark.parametrize(
    "avg_complexity,expected_factor",
    [
        (0.0, 1.0),
        (5.0, 1.0),
        (10.0, 2.0),
        (15.0, 3.0),
        (100.0, 3.0),  # capped
    ],
)
def test_complexity_factor_floors_at_1x_and_caps_at_3x(avg_complexity, expected_factor):
    assert m._complexity_factor(avg_complexity) == pytest.approx(expected_factor)


# --- _format_duration ------------------------------------------------------------------


@pytest.mark.parametrize(
    "hours,expected",
    [
        (0.0, "< 1 working day"),
        (7.9, "< 1 working day"),
        (8.0, "~1 working day"),
        (16.0, "~2 working days"),
        (80.0, "~10 working days"),  # exactly 10 days stays in the day-formatted band
        (88.0, "~2 weeks"),  # 11 days -> 2.2 weeks, rounds to 2
    ],
)
def test_format_duration_switches_to_weeks_only_past_ten_working_days(hours, expected):
    assert m._format_duration(hours) == expected


# --- compute_metrics ---------------------------------------------------------------------


def test_compute_metrics_on_sample_fixture_matches_hand_computed_totals():
    # NLOC: 63+41+23+12+6+5=150, CCN: 24+18+9+5+3+2=61 -> avg 10.1(6), "High" band.
    # factor = 10.1(6)/5.0 = 2.03(3), hours = 150/400*2.03(3) = 0.7625 -> < 1 working day.
    metrics = m.compute_metrics(SAMPLE_CSV)

    assert metrics["function_count"] == 6
    assert metrics["total_nloc"] == 150
    assert metrics["avg_complexity"] == pytest.approx(61 / 6)
    assert metrics["rating"] == "High"
    assert metrics["rating_color"] == "orange"
    assert metrics["duration"] == "< 1 working day"


def test_compute_metrics_raises_on_empty_csv(tmp_path):
    empty_csv = tmp_path / "empty.csv"
    empty_csv.write_text("", encoding="utf-8")

    with pytest.raises(ValueError, match="No functions parsed"):
        m.compute_metrics(empty_csv)


# --- main (CLI wiring: badge files + stdout summary) --------------------------------------


def test_main_writes_both_badge_files_and_prints_summary(tmp_path, capsys):
    out_dir = tmp_path / "out"
    out_dir.mkdir()

    exit_code = m.main([str(SAMPLE_CSV), "--out-dir", str(out_dir)])

    assert exit_code == 0

    complexity_badge = json.loads((out_dir / "complexity-badge.json").read_text(encoding="utf-8"))
    assert complexity_badge == {
        "schemaVersion": 1,
        "label": "code complexity",
        "message": "High (avg CCN 10.2)",
        "color": "orange",
    }

    time_badge = json.loads((out_dir / "understanding-time-badge.json").read_text(encoding="utf-8"))
    assert time_badge == {
        "schemaVersion": 1,
        "label": "est. time to understand",
        "message": "< 1 working day",
        "color": "blue",
    }

    out = capsys.readouterr().out
    assert "6 functions" in out
    assert "150 NLOC" in out
    assert "avg CCN 10.17 (High)" in out
    assert "estimated understanding time: < 1 working day" in out
