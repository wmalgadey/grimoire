"""Tests for scripts/mutation-history.py.

Same shape as scripts/test_mutation_report_index.py: fixtures in, assertions on what the
tool records and renders. Run with `python3 -m pytest scripts/test_mutation_history.py`.

What is pinned here is what the store exists for. The history is only worth committing if
two things hold: a mutant keeps its identity when the file around it moves, and a moved
denominator is called out instead of being averaged into a percentage. Both come straight
from issue #181, where two guardrail runs over identical sources produced 2.35 % and
1.38 % and nobody could say whether anything had regressed.
"""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest

SCRIPT = Path(__file__).with_name("mutation-history.py")
_spec = importlib.util.spec_from_file_location("mutation_history", SCRIPT)
hist = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(hist)

PROJECT_ROOT = "/somewhere/backend/src/Grimoire.AgentRuntime"
GUARD = f"{PROJECT_ROOT}/Guardrails/SharedFileWriteGuard.cs"
REL_GUARD = "Grimoire.AgentRuntime/Guardrails/SharedFileWriteGuard.cs"


def mutant(status: str, *, mutator="Equality mutation", replacement="==", line=1, covered=0):
    return {
        "id": "x",
        "mutatorName": mutator,
        "replacement": replacement,
        "status": status,
        "location": {"start": {"line": line, "column": 1}, "end": {"line": line, "column": 2}},
        "coveredBy": [str(i) for i in range(covered)],
    }


def report(mutants: list[dict], *, path: str = GUARD, project_root: str = PROJECT_ROOT) -> dict:
    return {
        "schemaVersion": "2",
        "projectRoot": project_root,
        "thresholds": {"high": 90, "low": 80},
        "files": {path: {"language": "cs", "source": "…", "mutants": mutants}},
    }


def write_run(tmp_path: Path, name: str, data: dict, log: str | None = None) -> Path:
    target = tmp_path / name
    (target / "reports").mkdir(parents=True)
    (target / "reports" / "mutation-report.json").write_text(json.dumps(data), encoding="utf-8")
    if log is not None:
        (target / "run.log").write_text(log, encoding="utf-8")
    return target


def record(store: Path, report_dir: Path, *, at: str, target=None, duration=None, monkeypatch=None):
    monkeypatch.setenv("MUTATION_HISTORY_NOW", at)
    argv = ["prog", "--store", str(store), "record", str(report_dir)]
    if target:
        argv += ["--target", target]
    if duration:
        argv += ["--duration", str(duration)]
    assert hist.main(argv) == 0


LOG = """
Version: 4.16.0

[07:00:23 INF] Stryker will use a max of 4 parallel testsessions.
[07:08:23 INF] 1323 mutants created
[07:19:24 INF] 347   total mutants will be tested
[07:30:11 INF] Time Elapsed 00:23:54.3608903
"""


# --------------------------------------------------------------- what a record is made of


def test_run_log_supplies_the_provenance_the_json_does_not_carry(tmp_path, monkeypatch):
    store = tmp_path / "store"
    run = write_run(tmp_path, "guardrails", report([mutant("Survived", covered=55)]), log=LOG)
    record(store, run, at="2026-08-23T07:30:11+00:00", monkeypatch=monkeypatch)

    entry = hist.read_ledger(store)[0]
    assert entry["target"] == "guardrails"
    assert entry["stryker_version"] == "4.16.0"
    assert entry["concurrency"] == 4
    assert entry["duration_seconds"] == 23 * 60 + 54
    assert entry["mutants_created"] == 1323
    assert entry["baseline_failing_tests"] is False


def test_a_red_baseline_is_recorded_because_it_silently_shrinks_the_denominator(tmp_path, monkeypatch):
    store = tmp_path / "store"
    run = write_run(
        tmp_path, "guardrails", report([mutant("Survived")]),
        log=LOG + "\n[07:10:00 WRN] 12 mutants are only covered by failing tests.\n",
    )
    record(store, run, at="2026-08-23T07:30:11+00:00", monkeypatch=monkeypatch)
    assert hist.read_ledger(store)[0]["baseline_failing_tests"] is True


def test_cost_per_scored_mutant_is_derived_because_that_is_what_a_tier_change_moves(tmp_path, monkeypatch):
    store = tmp_path / "store"
    run = write_run(tmp_path, "guardrails", report([mutant("Killed"), mutant("Survived")]))
    record(store, run, at="2026-08-23T07:30:11+00:00", duration=120, monkeypatch=monkeypatch)
    assert hist.read_ledger(store)[0]["seconds_per_scored_mutant"] == 60.0


def test_string_mutants_are_scored_separately_because_message_assertions_are_not_the_goal(tmp_path):
    data = report(
        [
            mutant("Killed", mutator="String mutation", replacement='""'),
            mutant("Killed", mutator="String mutation", replacement='"x"'),
            mutant("Survived"),
        ]
    )
    body = hist.compact_report(data)
    assert body["score"] == pytest.approx(66.6667, abs=1e-3)
    assert body["score_excluding_strings"] == 0.0


def test_file_identity_is_relative_to_the_project_so_two_checkouts_compare(tmp_path):
    body = hist.compact_report(report([mutant("Survived")]))
    assert list(body["files"]) == [REL_GUARD]


def test_every_target_under_a_report_root_is_recorded(tmp_path, monkeypatch):
    store = tmp_path / "store"
    root = tmp_path / "mutation"
    write_run(root, "domain", report([mutant("Killed")]))
    write_run(root, "hub", report([mutant("Survived")]))
    record(store, root, at="2026-08-23T07:30:11+00:00", monkeypatch=monkeypatch)
    assert sorted(e["target"] for e in hist.read_ledger(store)) == ["domain", "hub"]


# ------------------------------------------------------------------------ what compare says


def compare_of(a: dict, b: dict) -> dict:
    return hist.compare_snapshots(hist.compact_report(a), hist.compact_report(b))


def test_a_mutant_keeps_its_identity_when_the_code_above_it_moves(tmp_path):
    before = report([mutant("Survived", line=185)])
    after = report([mutant("Killed", line=203)])
    diff = compare_of(before, after)
    assert diff["shared"] == 1 and diff["only_a"] == 0 and diff["only_b"] == 0
    assert [m["file"] for m in diff["moves"]["fixed"]] == [REL_GUARD]


def test_killed_to_survived_is_a_regression_and_survived_to_killed_is_a_win(tmp_path):
    before = report([mutant("Killed", replacement="=="), mutant("Survived", replacement="!=")])
    after = report([mutant("Survived", replacement="=="), mutant("Killed", replacement="!=")])
    diff = compare_of(before, after)
    assert len(diff["moves"]["regressed"]) == 1
    assert len(diff["moves"]["fixed"]) == 1


def test_identical_mutations_in_one_file_are_told_apart_by_their_order(tmp_path):
    before = report([mutant("Killed", line=10), mutant("Killed", line=20)])
    after = report([mutant("Killed", line=10), mutant("Survived", line=20)])
    diff = compare_of(before, after)
    assert diff["shared"] == 2
    assert [m["line"] for m in diff["moves"]["regressed"]] == [20]


def test_a_moved_denominator_is_reported_instead_of_being_hidden_in_the_percentage(tmp_path):
    """#181: 2.35 % over 347 mutants and 1.38 % over 715 are not a trend.

    Stryker's Safe Mode removes every mutant in a method where one caused a compile error,
    and those mutants leave the score. The tool must say so; a reader comparing two
    percentages has no other way to find out.
    """
    before = report([mutant("Killed"), mutant("CompileError", replacement="!=")])
    after = report([mutant("Killed"), mutant("Survived", replacement="!=")])
    diff = compare_of(before, after)
    assert diff["denominator_drift"] is True
    assert (diff["compile_errors_a"], diff["compile_errors_b"]) == (1, 0)

    rendered = hist.render_compare(
        {"target": "guardrails", "run_id": "a", "score": 100.0, "score_excluding_strings": 100.0,
         "scored": 1, "duration_seconds": 60},
        {"target": "guardrails", "run_id": "b", "score": 50.0, "score_excluding_strings": 50.0,
         "scored": 2, "duration_seconds": 60},
        hist.compact_report(before), hist.compact_report(after), diff, markdown=True,
    )
    assert "not a trend" in rendered


def test_an_unchanged_run_reports_no_movement(tmp_path):
    data = report([mutant("Survived"), mutant("Killed", replacement="!=")])
    diff = compare_of(data, data)
    assert diff["moves"] == {"regressed": [], "fixed": [], "reclassified": []}
    assert diff["files"] == [] and diff["denominator_drift"] is False


# --------------------------------------------------------------------- selecting a record


def ledger(*entries: dict) -> list[dict]:
    return [
        {"target": "guardrails", "commit": "abc1234def", "recorded_at": at, "run_id": rid, **rest}
        for at, rid, rest in entries
    ]


def test_selectors_reach_the_latest_the_one_before_it_and_a_commit(tmp_path):
    entries = ledger(
        ("2026-08-01T00:00:00Z", "one", {}),
        ("2026-08-02T00:00:00Z", "two", {"commit": "9999999"}),
        ("2026-08-03T00:00:00Z", "three", {}),
    )
    assert hist.select(entries, "guardrails@latest")["run_id"] == "three"
    assert hist.select(entries, "guardrails@latest~2")["run_id"] == "one"
    assert hist.select(entries, "guardrails@9999")["run_id"] == "two"


def test_an_unknown_selector_names_the_targets_that_do_exist(tmp_path):
    with pytest.raises(SystemExit) as raised:
        hist.select(ledger(("2026-08-01T00:00:00Z", "one", {})), "typo@latest")
    assert "guardrails" in str(raised.value)


def test_comparing_two_targets_is_refused_because_their_mutants_are_different_code(tmp_path, monkeypatch, capsys):
    store = tmp_path / "store"
    root = tmp_path / "mutation"
    write_run(root, "domain", report([mutant("Killed")]))
    write_run(root, "hub", report([mutant("Survived")]))
    record(store, root, at="2026-08-23T07:30:11+00:00", monkeypatch=monkeypatch)
    assert hist.main(["prog", "--store", str(store), "compare", "domain@latest", "hub@latest"]) == 1
    assert "refusing to compare different targets" in capsys.readouterr().err


def test_a_report_measured_against_another_tree_is_flagged_not_silently_relabelled(
    tmp_path, monkeypatch, capsys
):
    """mutation-test.sh stamps each report with the tree it measured.

    Recording one whose stamp disagrees with the checkout would put today's commit on
    yesterday's numbers — the same mislabelling the stamp was introduced to prevent.
    """
    store = tmp_path / "store"
    run = write_run(tmp_path, "guardrails", report([mutant("Survived")]))
    (run / "fingerprint").write_text("a-tree-that-is-not-this-one\n", encoding="utf-8")
    record(store, run, at="2026-08-23T07:30:11+00:00", monkeypatch=monkeypatch)

    assert "measured against a different tree" in capsys.readouterr().err
    assert hist.read_ledger(store)[0]["report_tree_matches_checkout"] is False
