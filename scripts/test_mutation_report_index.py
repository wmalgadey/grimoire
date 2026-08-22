"""Tests for scripts/mutation-report-index.py.

Same shape as scripts/ci/test_compute_codebase_metrics.py: fixtures in, assertions on the
rendered output. Run with `python3 -m pytest scripts/test_mutation_report_index.py`.

The index is the page somebody reads instead of opening nine Stryker reports, so the
things worth pinning are the ones that would make it quietly disagree with its source: the
score arithmetic, the colour bands, and the two cases that are easy to render wrong — a
target with no scored mutants at all, and a file path that contains markup.
"""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest

SCRIPT = Path(__file__).with_name("mutation-report-index.py")
_spec = importlib.util.spec_from_file_location("mutation_report_index", SCRIPT)
index = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(index)


def report(mutants: list[str], *, path: str = "/repo/src/Thing.cs", thresholds=None) -> dict:
    return {
        "schemaVersion": "2",
        "thresholds": thresholds if thresholds is not None else {"high": 90, "low": 80},
        "files": {
            path: {
                "language": "cs",
                "source": "x",
                "mutants": [
                    {"id": str(i), "mutatorName": "M", "status": s,
                     "location": {"start": {"line": 1, "column": 1}, "end": {"line": 1, "column": 2}}}
                    for i, s in enumerate(mutants)
                ],
            }
        },
    }


def write(root: Path, target: str, data: dict) -> None:
    d = root / target / "reports"
    d.mkdir(parents=True)
    (d / "mutation-report.json").write_text(json.dumps(data), encoding="utf-8")
    (d / "mutation-report.html").write_text("<html></html>", encoding="utf-8")


def build(root: Path) -> str:
    assert index.main(["prog", str(root)]) == 0
    return (root / "index.html").read_text(encoding="utf-8")


def test_timeouts_count_as_killed_and_ignored_mutants_stay_out_of_the_denominator(tmp_path):
    data = report(["Killed", "Killed", "Timeout", "Survived", "NoCoverage", "Ignored", "CompileError"])
    write(tmp_path, "domain", data)
    loaded = index.load(tmp_path / "domain" / "reports" / "mutation-report.json")
    assert loaded["scored"] == 5
    assert loaded["score"] == pytest.approx(60.0)


def test_a_target_with_nothing_scored_renders_a_dash_instead_of_raising(tmp_path):
    # Every mutant fell out before a test ever ran — Stryker's Safe Mode does this to a
    # whole method. The score is undefined, not zero, and formatting None used to crash.
    write(tmp_path, "hub", report(["CompileError", "CompileError", "Ignored"]))
    html = build(tmp_path)
    assert "&mdash;" in html
    assert "hub" in html


def test_bands_follow_the_thresholds_the_report_declares(tmp_path):
    # 82.9 % is "good" under Stryker's 80/60 defaults and "needs work" under this
    # repository's 90/80 — the index must agree with the report it links to.
    assert index.band(82.9, {"high": 90, "low": 80}) == "low"
    assert index.band(82.9, {"high": 80, "low": 60}) == "high"
    assert index.band(None, {"high": 90, "low": 80}) == "none"


def test_runtime_errors_are_reported_alongside_compile_errors(tmp_path):
    write(tmp_path, "hub", report(["Killed", "CompileError", "RuntimeError"]))
    html = build(tmp_path)
    # one killed mutant, and both error kinds shown in the single errors column
    assert ">2<" in html


def test_file_paths_are_escaped(tmp_path):
    write(tmp_path, "hub", report(["Survived"], path="/repo/src/<script>.cs"))
    html = build(tmp_path)
    assert "<script>.cs" not in html
    assert "&lt;script&gt;.cs" in html


def test_markdown_carries_the_marker_the_workflow_looks_for(tmp_path, capsys):
    # .github/workflows/mutation.yml finds its own comment by this marker; without it the
    # job posts a new comment on every push instead of updating the one it owns.
    write(tmp_path, "domain", report(["Killed", "Survived"]))
    assert index.main(["prog", str(tmp_path), "--markdown", "--commit", "abc1234def"]) == 0
    out = capsys.readouterr().out
    assert out.startswith(index.MARKDOWN_MARKER)
    assert "`domain`" in out
    assert "50.0 %" in out
    assert "abc1234" in out and "abc1234def" not in out  # short sha
    assert not (tmp_path / "index.html").exists()  # --markdown writes nothing to disk


def test_markdown_survives_a_target_with_no_score(tmp_path, capsys):
    write(tmp_path, "hub", report(["CompileError", "Ignored"]))
    assert index.main(["prog", str(tmp_path), "--markdown"]) == 0
    out = capsys.readouterr().out
    assert "No mutant was scored" in out
    assert "| — |" in out


def test_an_empty_directory_is_an_error_not_an_empty_page(tmp_path):
    (tmp_path / "hub").mkdir()
    assert index.main(["prog", str(tmp_path)]) == 1
    assert not (tmp_path / "index.html").exists()
