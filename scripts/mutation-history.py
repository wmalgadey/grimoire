#!/usr/bin/env python3
"""Durable, comparable record of Stryker mutation runs.

A Stryker report is written into a directory that the next run deletes, and that directory
is gitignored (docs/reports/README.md explains why: tens of megabytes of generated HTML).
The consequence was measured rather than imagined — issue #181's follow-up comment had to
end with "I cannot confirm that: docs/reports/mutation/ is gitignored and this run
overwrote the directory, so the original JSON is gone". Two scores existed for the same
sources and there was no way to tell whether the difference was a regression or a changed
denominator.

This script is the answer to that. Every run is distilled into two artefacts that survive
the next one:

    docs/reports/mutation-history/ledger.jsonl        one line per (target, run) — tracked
    docs/reports/mutation-history/snapshots/<id>.json per-mutant detail        — tracked

Both are small: the sources, the test files and the HTML are dropped, and what is kept is
the identity and status of each mutant. The full report stays where Stryker put it.

WHY PER MUTANT, NOT PER SCORE. The same issue proved that comparing two mutation scores is
unsound whenever the denominator moved: Stryker's Safe Mode drops every mutant in a method
where one mutant failed to compile, and those mutants leave the score entirely. 2.35 % over
347 scored mutants and 1.38 % over 715 are not a trend. So the comparison unit here is the
individual mutant, keyed by something that survives an edit above it:

    <mutator name> | <hash of the replacement text> | <ordinal among identical ones in the file>

Not the line number — inserting a line would then invalidate every key below it. `compare`
reports killed→survived and survived→killed over the mutants both runs share, and reports
the denominator drift separately instead of hiding it inside a percentage.

Usage:
    mutation-history.py record docs/reports/mutation/domain --target domain
    mutation-history.py record docs/reports/mutation          # every target under a root
    mutation-history.py list [--target NAME]
    mutation-history.py show <selector>
    mutation-history.py compare <selector> [<selector>] [--markdown]

Selectors are `[<target>@]<rev>`, where <rev> is `latest`, `latest~N`, a run-id prefix or a
commit prefix — `guardrails@latest~1`, `domain@84dfa3c`. `compare <target>` with no rev
compares that target's two most recent runs.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import platform
import re
import subprocess
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_STORE = REPO_ROOT / "docs" / "reports" / "mutation-history"

# The score arithmetic lives in exactly one place. mutation-report-index.py's own docstring
# is explicit that a second implementation is a second number to disagree with, and this
# script would be that second implementation.
_spec = importlib.util.spec_from_file_location(
    "mutation_report_index", Path(__file__).with_name("mutation-report-index.py")
)
index = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(index)

SCORED = index.SCORED

# String mutants are excluded from the second score on purpose. #181: "Do not chase the 74
# surviving string mutants. Assertions on message text are coupling without yield; they are
# correctly surviving." A score that counts them measures how much the suite quotes its own
# error messages, which is not the property anybody wants to move.
STRING_MUTATORS = ("String mutation",)


# --------------------------------------------------------------------------- reading a run


def rounded(score: float | None) -> float | None:
    return None if score is None else round(score, 4)


def mutant_key(mutant: dict, seen: Counter) -> str:
    """A mutant identity that survives edits elsewhere in the file.

    (mutator, replacement) is what the mutant *is*; the ordinal disambiguates the several
    identical ones a file usually contains. Line numbers are recorded for display only —
    keying on them would mark every mutant below an inserted line as new.
    """
    replacement = mutant.get("replacement") or ""
    digest = hashlib.sha256(replacement.encode("utf-8")).hexdigest()[:8]
    stem = f"{mutant['mutatorName']}|{digest}"
    key = f"{stem}|{seen[stem]}"
    seen[stem] += 1
    return key


def relative_path(path: str, project_root: str | None) -> str:
    """Machine-independent file identity: '<project>/<path inside it>'.

    Stryker records absolute paths, so a record made in /Volumes/Daten and one made in a
    container's /src would otherwise share no file at all.
    """
    root = Path(project_root) if project_root else None
    p = Path(path)
    if root:
        try:
            return str(Path(root.name) / p.relative_to(root))
        except ValueError:
            pass
    parts = p.parts
    for anchor in ("src", "tests"):
        if anchor in parts:
            return str(Path(*parts[parts.index(anchor) + 1 :]))
    return p.name


def compact(report_path: Path) -> dict:
    return compact_report(json.loads(report_path.read_text(encoding="utf-8")))


def compact_report(data: dict) -> dict:
    """Strip a Stryker report down to what a comparison needs.

    Everything dropped here is either enormous (`source`, `testFiles` — 1.9 MB of the 2 MB
    guardrail report) or unstable across runs (`id`, `coveredBy` test ids). The count of
    covering tests is kept: a survivor covered by 55 tests and one covered by none are
    different findings, and #181 turned on exactly that distinction.
    """
    project_root = data.get("projectRoot")
    files: dict[str, dict] = {}
    totals: Counter[str] = Counter()
    string_counts: Counter[str] = Counter()

    for path, entry in data.get("files", {}).items():
        rel = relative_path(path, project_root)
        seen: Counter[str] = Counter()
        counts: Counter[str] = Counter()
        mutants: dict[str, dict] = {}
        for mutant in entry.get("mutants", []):
            key = mutant_key(mutant, seen)
            status = mutant["status"]
            counts[status] += 1
            if mutant["mutatorName"] in STRING_MUTATORS:
                string_counts[status] += 1
            mutants[key] = {
                "status": status,
                "line": mutant.get("location", {}).get("start", {}).get("line"),
                # Meaningful for survivors: Stryker.NET reports coveredBy for mutants it did
                # not kill and omits it for the ones it did. A survivor with 55 covering
                # tests is #181's central finding — the tests run the code and assert
                # nothing about it — and a survivor with 0 is an ordinary coverage hole.
                "covered_by": len(mutant.get("coveredBy") or []),
            }
        totals.update(counts)
        # Files with nothing scored are the ones the --mutate filter removed. Their counts
        # stay (they are how you see the filter worked); their mutant lists would triple
        # the snapshot for no comparison value.
        scored = sum(counts[s] for s in SCORED)
        files[rel] = {
            "counts": dict(sorted(counts.items())),
            "scored": scored,
            "score": rounded(index.score(counts)),
            "mutants": mutants if scored else {},
        }

    non_string = Counter(totals)
    non_string.subtract(string_counts)
    return {
        "totals": dict(sorted(totals.items())),
        "scored": sum(totals[s] for s in SCORED),
        # Rounded, because these numbers are committed: a ledger line that differs from
        # yesterday's in the fifteenth decimal is a diff nobody can read.
        "score": rounded(index.score(totals)),
        "score_excluding_strings": rounded(index.score(non_string)),
        "thresholds": data.get("thresholds") or {},
        "files": dict(sorted(files.items())),
    }


LOG_PATTERNS = {
    "stryker_version": re.compile(r"^Version:\s*(\S+)", re.M),
    "concurrency": re.compile(r"max of (\d+) parallel testsessions"),
    "mutants_created": re.compile(r"(\d+) mutants created"),
    "mutants_tested": re.compile(r"(\d+)\s+total mutants will be tested"),
    "elapsed": re.compile(r"Time Elapsed (\d+):(\d+):(\d+)"),
}


def parse_run_log(log_path: Path) -> dict:
    """Everything the JSON does not say: tool version, concurrency, wall clock, baseline.

    The failing-baseline flag is the one that matters most. mutation-test.sh deliberately
    leaves break-on-initial-test-failure off so one flaky test cannot end a seventeen-hour
    run, which means a red baseline silently removes its mutants from the score instead of
    stopping anything. A recorded score without this flag is a number nobody can date.
    """
    if not log_path.is_file():
        return {}
    text = log_path.read_text(encoding="utf-8", errors="replace")
    out: dict = {}
    for name, pattern in LOG_PATTERNS.items():
        match = pattern.search(text)
        if not match:
            continue
        if name == "elapsed":
            h, m, s = (int(g) for g in match.groups())
            out["duration_seconds"] = h * 3600 + m * 60 + s
        elif name == "stryker_version":
            out[name] = match.group(1)
        else:
            out[name] = int(match.group(1))
    out["baseline_failing_tests"] = "only covered by failing tests" in text
    return out


def git_provenance() -> dict:
    def git(*args: str, strip: bool = True) -> str:
        try:
            out = subprocess.run(
                ["git", *args], cwd=REPO_ROOT, capture_output=True, text=True, check=True
            ).stdout
            return out.strip() if strip else out
        except (subprocess.CalledProcessError, FileNotFoundError):
            return ""

    commit = git("rev-parse", "HEAD")
    # Verbatim, not stripped: a porcelain line begins with a status column that is often a
    # space (" M path"), and stripping it would silently produce a different digest from
    # the one mutation-test.sh computes for the same tree.
    status = git("status", "--porcelain=v1", strip=False)
    # Same definition mutation-test.sh uses for its resumability fingerprint, so a record
    # and the report directory it came from agree on what "this tree" means.
    fingerprint = hashlib.sha256(((commit or "no-git") + "\n" + status).encode()).hexdigest()
    return {
        "commit": commit or None,
        "branch": git("rev-parse", "--abbrev-ref", "HEAD") or None,
        "dirty": bool(status.strip()),
        "tree_fingerprint": fingerprint,
    }


# ------------------------------------------------------------------------------- the store


def ledger_path(store: Path) -> Path:
    return store / "ledger.jsonl"


def read_ledger(store: Path) -> list[dict]:
    path = ledger_path(store)
    if not path.is_file():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def read_snapshot(store: Path, entry: dict) -> dict | None:
    path = store / entry["snapshot"]
    return json.loads(path.read_text(encoding="utf-8")) if path.is_file() else None


def find_reports(root: Path) -> list[tuple[str, Path]]:
    """A single target directory, a directory of them, or a report file — all accepted."""
    if root.is_file():
        return [(root.parent.parent.name, root)]
    direct = sorted(root.rglob("mutation-report.json"))
    if not direct:
        return []
    # Both layouts a caller has: docs/reports/mutation (the target is the subdirectory each
    # report sits in) and docs/reports/mutation/domain (the root *is* the target). Stryker's
    # own "reports/" level is not a target name in either.
    out = []
    for report in direct:
        parts = [p for p in report.relative_to(root).parts[:-1] if p != "reports"]
        out.append((parts[0] if parts else root.name, report))
    return out


def now_utc() -> datetime:
    stamp = os.environ.get("MUTATION_HISTORY_NOW")
    return datetime.fromisoformat(stamp) if stamp else datetime.now(timezone.utc)


def record(args: argparse.Namespace) -> int:
    store: Path = args.store
    root = Path(args.report)
    reports = find_reports(root)
    if not reports:
        print(f"no mutation-report.json under {root} — nothing to record.", file=sys.stderr)
        return 1

    provenance = git_provenance()
    if args.commit:
        # Backfilling an old report: today's HEAD is not what that run measured, and a
        # record that claims otherwise is worse than no record. Say what is known (the
        # commit) and leave the rest empty rather than inventing it.
        provenance = {"commit": args.commit, "branch": None, "dirty": None, "tree_fingerprint": None}
    recorded_at = now_utc()
    known = {(e["target"], e["run_id"]) for e in read_ledger(store)}

    for target, report in reports:
        target = args.target or target
        log = parse_run_log(report.parent.parent / "run.log")
        if args.log:
            log = {**log, **parse_run_log(Path(args.log))}
        body = compact(report)

        short = (provenance["commit"] or "unknown")[:7]
        stamp = recorded_at.strftime("%Y%m%dT%H%M%SZ")
        run_id = f"{stamp}-{short}"
        suffix = 1
        while (target, run_id) in known:  # two records of one target in the same second
            run_id = f"{stamp}-{short}-{suffix}"
            suffix += 1
        known.add((target, run_id))

        # mutation-test.sh drops a `fingerprint` file beside every report saying which tree
        # it was measured against. If it disagrees with the tree we are standing in, the
        # provenance about to be written is today's HEAD for somebody else's measurement —
        # the exact mislabelling the fingerprint exists to prevent.
        stamped = (report.parent.parent / "fingerprint")
        stale = (
            stamped.is_file()
            and provenance["tree_fingerprint"] is not None
            and stamped.read_text(encoding="utf-8").strip() != provenance["tree_fingerprint"]
        )
        if stale:
            print(
                f"warning: {target}'s report was measured against a different tree than the "
                "one checked out. Recording today's commit as its provenance — pass "
                "--commit <sha> if you know which one it really was.",
                file=sys.stderr,
            )

        duration = args.duration if args.duration is not None else log.get("duration_seconds")
        entry = {
            "run_id": run_id,
            "target": target,
            "recorded_at": recorded_at.isoformat().replace("+00:00", "Z"),
            **provenance,
            "score": body["score"],
            "score_excluding_strings": body["score_excluding_strings"],
            "scored": body["scored"],
            "counts": body["totals"],
            "mutants_created": log.get("mutants_created"),
            "mutants_tested": log.get("mutants_tested"),
            "duration_seconds": duration,
            # The sustainability number. Mutation cost is (mutants x test time), so this is
            # the figure that says whether a target can ever run anywhere but overnight —
            # and the figure a change of test strategy has to move. See the README.
            "seconds_per_scored_mutant": round(duration / body["scored"], 2)
            if duration and body["scored"]
            else None,
            "concurrency": log.get("concurrency"),
            "stryker_version": log.get("stryker_version"),
            "baseline_failing_tests": log.get("baseline_failing_tests"),
            # Not this machine's, when backfilling: the run happened elsewhere, and the
            # concurrency the log reports is the part that actually explains its wall clock.
            "host": None if args.commit else {"cores": os.cpu_count(), "platform": platform.system()},
            "note": args.note,
            "report_tree_matches_checkout": None if not stamped.is_file() else not stale,
            "snapshot": f"snapshots/{target}/{run_id}.json",
        }

        snapshot = {"run_id": run_id, "target": target, "entry": entry, **body}
        (store / entry["snapshot"]).parent.mkdir(parents=True, exist_ok=True)
        (store / entry["snapshot"]).write_text(
            json.dumps(snapshot, indent=1, sort_keys=True, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )
        with ledger_path(store).open("a", encoding="utf-8") as fh:
            fh.write(json.dumps(entry, sort_keys=True, ensure_ascii=False) + "\n")

        warn = " [BASELINE HAD FAILING TESTS]" if entry["baseline_failing_tests"] else ""
        print(
            f"recorded {run_id}: {fmt_pct(entry['score'])} over {entry['scored']} scored "
            f"mutants ({fmt_duration(duration)}){warn}"
        )
    return 0


# -------------------------------------------------------------------------------- querying


def select(entries: list[dict], selector: str, default_target: str | None = None) -> dict:
    target, _, rev = selector.rpartition("@")
    target = target or default_target
    if rev in ("", "latest"):
        rev = "latest"
    pool = [e for e in entries if not target or e["target"] == target]
    if not pool:
        raise SystemExit(
            f"no recorded run for target '{target}'. Known targets: "
            + ", ".join(sorted({e['target'] for e in entries}) or ["(none)"])
        )
    pool.sort(key=lambda e: e["recorded_at"])
    match = re.fullmatch(r"latest(?:~(\d+))?", rev)
    if match:
        back = int(match.group(1) or 0)
        if back >= len(pool):
            raise SystemExit(f"only {len(pool)} run(s) recorded for '{target}' — no latest~{back}.")
        return pool[-1 - back]
    hits = [e for e in pool if e["run_id"].startswith(rev) or (e["commit"] or "").startswith(rev)]
    if not hits:
        raise SystemExit(f"no recorded run matches '{selector}'.")
    return hits[-1]


def fmt_pct(score: float | None) -> str:
    return "—" if score is None else f"{score:.2f} %"


def fmt_duration(seconds: float | None) -> str:
    if not seconds:
        return "—"
    if seconds < 90:
        return f"{seconds:.0f} s"
    return f"{seconds / 60:.0f} min" if seconds < 5400 else f"{seconds / 3600:.1f} h"


def cmd_list(args: argparse.Namespace) -> int:
    entries = read_ledger(args.store)
    if args.target:
        entries = [e for e in entries if e["target"] == args.target]
    if not entries:
        print("nothing recorded yet — run mutation-history.py record <report dir>.")
        return 0
    header = (
        f"{'RUN':<25} {'TARGET':<26} {'SCORE':>8} {'NO-STR':>8} {'SCORED':>7} "
        f"{'TIME':>7} {'s/MUT':>7}  PROVENANCE"
    )
    print(header)
    for e in sorted(entries, key=lambda e: (e["target"], e["recorded_at"])):
        flags = []
        if e.get("dirty"):
            flags.append("dirty")
        if e.get("baseline_failing_tests"):
            flags.append("RED BASELINE")
        if e.get("note"):
            flags.append(e["note"])
        print(
            f"{e['run_id']:<25} {e['target']:<26} {fmt_pct(e['score']):>8} "
            f"{fmt_pct(e['score_excluding_strings']):>8} {e['scored']:>7} "
            f"{fmt_duration(e.get('duration_seconds')):>7} "
            f"{(e.get('seconds_per_scored_mutant') or 0):>7.1f}  "
            f"{(e.get('commit') or '')[:7]}{' (' + ', '.join(flags) + ')' if flags else ''}"
        )
    return 0


def cmd_show(args: argparse.Namespace) -> int:
    entries = read_ledger(args.store)
    entry = select(entries, args.selector)
    snapshot = read_snapshot(args.store, entry)
    print(json.dumps(snapshot or entry, indent=2, sort_keys=True))
    return 0


# ------------------------------------------------------------------------------- comparing


def mutant_index(snapshot: dict) -> dict[tuple[str, str], dict]:
    return {
        (path, key): mutant
        for path, entry in snapshot["files"].items()
        for key, mutant in entry["mutants"].items()
    }


KILLED = ("Killed", "Timeout")


def compare_snapshots(a: dict, b: dict) -> dict:
    left, right = mutant_index(a), mutant_index(b)
    shared = left.keys() & right.keys()

    def bucket(key):
        was, now = left[key]["status"], right[key]["status"]
        if was == now:
            return None
        if was in KILLED and now not in KILLED:
            return "regressed"
        if was not in KILLED and now in KILLED:
            return "fixed"
        return "reclassified"

    moves: dict[str, list] = {"regressed": [], "fixed": [], "reclassified": []}
    for key in sorted(shared):
        where = bucket(key)
        if where:
            moves[where].append(
                {
                    "file": key[0],
                    "mutant": key[1],
                    "line": right[key]["line"],
                    "from": left[key]["status"],
                    "to": right[key]["status"],
                    "covered_by": right[key]["covered_by"],
                }
            )

    def survived(f: dict | None) -> int:
        """"Survived" here means "not killed": NoCoverage is a survivor nobody even ran."""
        return (f["counts"].get("Survived", 0) + f["counts"].get("NoCoverage", 0)) if f else 0

    files = []
    for path in sorted(set(a["files"]) | set(b["files"])):
        fa, fb = a["files"].get(path), b["files"].get(path)
        if not fa or not fb or fa["counts"] != fb["counts"]:
            files.append(
                {
                    "path": path,
                    "scored_a": fa["scored"] if fa else 0,
                    "scored_b": fb["scored"] if fb else 0,
                    "survived_a": survived(fa),
                    "survived_b": survived(fb),
                    "score_a": fa["score"] if fa else None,
                    "score_b": fb["score"] if fb else None,
                }
            )

    # Two different reasons a mutant leaves the denominator, kept apart because they mean
    # different things: a compile error is Stryker's Safe Mode dropping a whole method
    # (#181's unresolved question), an Ignored mutant is a filter doing its job.
    compile_a, compile_b = a["totals"].get("CompileError", 0), b["totals"].get("CompileError", 0)
    return {
        "shared": len(shared),
        "only_a": len(left.keys() - right.keys()),
        "only_b": len(right.keys() - left.keys()),
        "moves": moves,
        "files": files,
        # The denominator check the #181 comment had to do by hand and could not finish.
        "denominator_drift": a["scored"] != b["scored"] or compile_a != compile_b,
        "compile_errors_a": compile_a,
        "compile_errors_b": compile_b,
    }


def render_compare(ea: dict, eb: dict, a: dict, b: dict, diff: dict, markdown: bool) -> str:
    bullet = "- " if markdown else "  "
    lines: list[str] = []
    head = f"Mutation history: {ea['target']}"
    lines += [f"## {head}", ""] if markdown else [head, "=" * len(head), ""]

    def provenance(label: str, e: dict) -> str:
        flags = []
        if e.get("dirty"):
            flags.append("dirty tree")
        if e.get("baseline_failing_tests"):
            flags.append("RED BASELINE")
        return (
            f"{bullet}**{label}** `{e['run_id']}` — {fmt_pct(e['score'])} "
            f"({fmt_pct(e['score_excluding_strings'])} excl. strings) over {e['scored']} scored "
            f"mutants, {fmt_duration(e.get('duration_seconds'))}"
            f"{', ' + ', '.join(flags) if flags else ''}"
        )

    lines += [provenance("A", ea), provenance("B", eb), ""]

    if diff["denominator_drift"]:
        lines += [
            "> **The two scores are not a trend.** The denominator moved: "
            f"{ea['scored']} → {eb['scored']} scored mutants, with "
            f"{diff['compile_errors_a']} → {diff['compile_errors_b']} compile errors "
            "(Stryker's Safe Mode drops every mutant in a method where one failed to "
            "compile, and those leave the score entirely). A percentage over a different "
            "set of mutants is a different measurement. The per-mutant rows below are keyed "
            "by identity and do compare.",
            "",
        ]

    lines += [
        f"{bullet}mutants in both runs: **{diff['shared']}**  ·  only in A: {diff['only_a']}  ·  "
        f"only in B: {diff['only_b']}",
        f"{bullet}killed → survived (regressions): **{len(diff['moves']['regressed'])}**",
        f"{bullet}survived → killed (wins): **{len(diff['moves']['fixed'])}**",
        f"{bullet}other status changes: {len(diff['moves']['reclassified'])}",
        "",
    ]

    for title, key in (("Regressions", "regressed"), ("Newly killed", "fixed")):
        rows = diff["moves"][key]
        if not rows:
            continue
        lines.append(f"### {title}" if markdown else f"{title}:")
        if markdown:
            lines += ["", "| File | Line | Mutant | A | B | Covered by |", "|---|--:|---|---|---|--:|"]
            lines += [
                f"| `{r['file']}` | {r['line']} | {r['mutant'].split('|')[0]} | {r['from']} | "
                f"{r['to']} | {r['covered_by']} |"
                for r in rows[:40]
            ]
        else:
            lines += [
                f"  {r['file']}:{r['line']}  {r['mutant'].split('|')[0]}  {r['from']} → {r['to']}"
                for r in rows[:40]
            ]
        if len(rows) > 40:
            lines.append(f"{bullet}… and {len(rows) - 40} more (see `show`)")
        lines.append("")

    if diff["files"]:
        lines.append("### Per file" if markdown else "Per file:")
        if markdown:
            lines += ["", "| File | Scored A→B | Survived A→B | Score A→B |", "|---|--:|--:|--:|"]
            lines += [
                f"| `{f['path']}` | {f['scored_a']} → {f['scored_b']} | "
                f"{f['survived_a']} → {f['survived_b']} | {fmt_pct(f['score_a'])} → {fmt_pct(f['score_b'])} |"
                for f in diff["files"]
            ]
        else:
            lines += [
                f"  {f['path']:<60} scored {f['scored_a']:>4} → {f['scored_b']:<4} "
                f"survived {f['survived_a']:>4} → {f['survived_b']:<4} "
                f"{fmt_pct(f['score_a'])} → {fmt_pct(f['score_b'])}"
                for f in diff["files"]
            ]
        lines.append("")

    cost_a, cost_b = ea.get("seconds_per_scored_mutant"), eb.get("seconds_per_scored_mutant")
    if cost_a and cost_b:
        lines += [
            f"{bullet}cost per scored mutant: {cost_a:.1f} s → {cost_b:.1f} s "
            f"(A on {ea.get('concurrency') or '?'} runners, B on {eb.get('concurrency') or '?'}). "
            "This is what a change of test strategy moves; the score is what a change of "
            "assertions moves.",
            "",
        ]
    return "\n".join(lines)


def cmd_compare(args: argparse.Namespace) -> int:
    entries = read_ledger(args.store)
    if not entries:
        print("nothing recorded yet.", file=sys.stderr)
        return 1
    if args.b is None:
        # `compare <target>` — the two most recent runs of it, which is the question
        # somebody asks nine times out of ten.
        target = args.a.split("@")[0] if "@" in args.a else args.a
        ea, eb = select(entries, f"{target}@latest~1"), select(entries, f"{target}@latest")
    else:
        ea, eb = select(entries, args.a), select(entries, args.b)
    if ea["target"] != eb["target"]:
        print(
            f"refusing to compare different targets ({ea['target']} vs {eb['target']}): their "
            "mutants are different code.",
            file=sys.stderr,
        )
        return 1
    # Run ids are unique per target, not globally: two targets recorded in one invocation
    # share a timestamp and a commit, and are still different runs.
    if ea["run_id"] == eb["run_id"]:
        print("both selectors resolved to the same run.", file=sys.stderr)
        return 1

    a, b = read_snapshot(args.store, ea), read_snapshot(args.store, eb)
    if not a or not b:
        missing = ea["run_id"] if not a else eb["run_id"]
        print(f"snapshot for {missing} is missing — only its ledger line survives.", file=sys.stderr)
        return 1

    sys.stdout.write(render_compare(ea, eb, a, b, compare_snapshots(a, b), args.markdown) + "\n")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--store", type=Path, default=DEFAULT_STORE, help="history directory")
    sub = parser.add_subparsers(dest="command", required=True)

    rec = sub.add_parser("record", help="distil a Stryker report into the history store")
    rec.add_argument("report", help="a target directory, a directory of them, or the JSON itself")
    rec.add_argument("--target", help="override the target name inferred from the path")
    rec.add_argument("--duration", type=float, help="wall clock in seconds (else read from run.log)")
    rec.add_argument("--log", help="run.log to parse, if it is not beside the report")
    rec.add_argument("--note", help="free text kept with the entry")
    rec.add_argument("--commit", help="backfill: the commit this report was measured on")
    rec.set_defaults(func=record)

    lst = sub.add_parser("list", help="every recorded run")
    lst.add_argument("--target")
    lst.set_defaults(func=cmd_list)

    shw = sub.add_parser("show", help="the full snapshot of one run")
    shw.add_argument("selector")
    shw.set_defaults(func=cmd_show)

    cmp_ = sub.add_parser("compare", help="two runs of one target, mutant by mutant")
    cmp_.add_argument("a")
    cmp_.add_argument("b", nargs="?")
    cmp_.add_argument("--markdown", action="store_true", help="paste-ready for an issue or PR")
    cmp_.set_defaults(func=cmd_compare)

    args = parser.parse_args(argv[1:])
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main(sys.argv))
