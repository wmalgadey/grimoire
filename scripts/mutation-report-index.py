#!/usr/bin/env python3
"""Builds the index page over a set of Stryker report directories.

Stryker writes one self-contained HTML report per run, and this repository runs it once
per target (scripts/mutation-test.sh). That leaves a directory of unrelated reports with
no way to see the whole picture, which is what this fills: one page listing every target
with its score and a link into its own report, plus the files that survived the most
mutants across all of them.

The score is Stryker's own definition, recomputed here from the JSON so the index never
disagrees with the report it links to:

    (killed + timeout) / (killed + timeout + survived + no_coverage)

CompileError and Ignored mutants are outside the denominator — Stryker never ran a test
for them, so they say nothing about the suite. Watch the CompileError count anyway: it
triggers Stryker's Safe Mode, which drops *every* mutant in the affected method, and a
target with a large one is scored on less code than it looks.

Two renderings, one arithmetic: the HTML page a developer opens after a full manual run,
and — with --markdown — the PR comment .github/workflows/mutation.yml maintains for the
fast tier. Keeping them in one script is the point; a second implementation of the score
is a second number to disagree with.

Usage:
    python3 scripts/mutation-report-index.py docs/reports/mutation
    python3 scripts/mutation-report-index.py docs/reports/mutation --markdown --commit abc1234
"""

from __future__ import annotations

import html
import json
import sys
from collections import Counter
from pathlib import Path

SCORED = ("Killed", "Timeout", "Survived", "NoCoverage")


def pct(score: float | None) -> str:
    """A target whose mutants were all CompileError or Ignored has no score at all."""
    return "&mdash;" if score is None else f"{score:.1f}&thinsp;%"


def load(report: Path) -> dict:
    data = json.loads(report.read_text(encoding="utf-8"))
    counts: Counter[str] = Counter()
    files = []
    for path, entry in data.get("files", {}).items():
        per_file: Counter[str] = Counter(m["status"] for m in entry["mutants"])
        counts.update(per_file)
        scored = sum(per_file[s] for s in SCORED)
        if scored:
            files.append(
                {
                    "path": path,
                    "survived": per_file["Survived"] + per_file["NoCoverage"],
                    "scored": scored,
                    "score": 100.0 * (per_file["Killed"] + per_file["Timeout"]) / scored,
                }
            )
    scored = sum(counts[s] for s in SCORED)
    return {
        "counts": counts,
        "scored": scored,
        "score": 100.0 * (counts["Killed"] + counts["Timeout"]) / scored if scored else None,
        "thresholds": data.get("thresholds") or {},
        "files": files,
    }


def band(score: float | None, thresholds: dict | None = None) -> str:
    """Colour band for a score, using the thresholds the report itself declares.

    Hard-coding a band here would put the index at odds with the report it links to: the
    Stryker configs in this repository declare high=90/low=80, so an 82.9 shown as "good"
    on this page is "needs work" one click away.
    """
    if score is None:
        return "none"
    high = (thresholds or {}).get("high", 80)
    low = (thresholds or {}).get("low", 60)
    return "high" if score >= high else "low" if score >= low else "poor"


MARKDOWN_MARKER = "<!-- grimoire-mutation-report -->"


def render_markdown(targets: list[dict], worst: list[dict], commit: str | None) -> str:
    """The PR-comment body — same shape as scripts/ci/format-complexity-report's."""
    overall = sum(t["counts"]["Killed"] + t["counts"]["Timeout"] for t in targets)
    scored = sum(t["scored"] for t in targets)
    survived = sum(t["counts"]["Survived"] for t in targets)
    uncovered = sum(t["counts"]["NoCoverage"] for t in targets)

    lines = [
        MARKDOWN_MARKER,
        "## Mutation Report",
        "",
        f"**{100.0 * overall / scored:.1f} %** of {scored} scored mutants killed · "
        f"**{survived}** survived · **{uncovered}** not covered by any test"
        if scored
        else "No mutant was scored — every one of them was a compile error or filtered out.",
        "",
        "A survivor is a mutation no test noticed: a line the suite runs without asserting "
        "anything about.",
        "",
        "| Target | Score | Killed | Survived | No cov. | Scored |",
        "|---|--:|--:|--:|--:|--:|",
    ]
    for t in targets:
        score = "—" if t["score"] is None else f"{t['score']:.1f} %"
        lines.append(
            f"| `{t['name']}` | {score} | {t['counts']['Killed'] + t['counts']['Timeout']} "
            f"| {t['counts']['Survived']} | {t['counts']['NoCoverage']} | {t['scored']} |"
        )

    if worst:
        lines += [
            "",
            "<details>",
            "<summary>Files with unkilled mutants</summary>",
            "",
            "| File | Survived | Score |",
            "|---|--:|--:|",
        ]
        for f in worst:
            path = f["path"].split("/src/")[-1].replace("|", "\\|")
            lines.append(f"| `{path}` | {f['survived']} | {f['score']:.1f} % |")
        lines += ["", "</details>"]

    lines += [
        "",
        "_Fast tier only — the guardrail policy and the remediation state machine. The Hub, "
        "the agent runtimes and the frontend take hours and are measured by hand "
        "(`./scripts/mutation-test.sh --group all`, see CONTRIBUTING.md). This is a report, "
        "not a gate: no score fails this job._",
    ]
    if commit:
        lines[-1] = lines[-1][:-1] + f" Measured on commit `{commit[:7]}`._"
    return "\n".join(lines) + "\n"


def main(argv: list[str]) -> int:
    args = [a for a in argv[1:] if not a.startswith("--")]
    markdown = "--markdown" in argv
    commit = None
    if "--commit" in argv:
        commit = argv[argv.index("--commit") + 1]
        args = [a for a in args if a != commit]

    root = Path(args[0] if args else "docs/reports/mutation")
    if not root.is_dir():
        print(f"{root} does not exist — nothing to index.", file=sys.stderr)
        return 1

    targets = []
    for target_dir in sorted(p for p in root.iterdir() if p.is_dir()):
        reports = sorted(target_dir.rglob("mutation-report.json"))
        if not reports:
            continue
        result = load(reports[-1])
        page = sorted(target_dir.rglob("mutation-report.html"))
        result["name"] = target_dir.name
        result["href"] = str(page[-1].relative_to(root)) if page else None
        targets.append(result)

    if not targets:
        print(f"no mutation-report.json under {root} — nothing to index.", file=sys.stderr)
        return 1

    worst = sorted(
        (f | {"target": t["name"], "thresholds": t["thresholds"]} for t in targets for f in t["files"]),
        key=lambda f: (-f["survived"], f["score"]),
    )[:20]

    if markdown:
        sys.stdout.write(render_markdown(targets, [f for f in worst if f["survived"]], commit))
        return 0

    rows = "\n".join(
        f"""      <tr>
        <td>{'<a href="' + html.escape(t["href"]) + '">' + html.escape(t["name"]) + "</a>" if t["href"] else html.escape(t["name"])}</td>
        <td class="num {band(t['score'], t['thresholds'])}">{pct(t['score'])}</td>
        <td class="num">{t['counts']['Killed']}</td>
        <td class="num">{t['counts']['Timeout']}</td>
        <td class="num warn">{t['counts']['Survived']}</td>
        <td class="num warn">{t['counts']['NoCoverage']}</td>
        <td class="num">{t['counts']['CompileError'] + t['counts']['RuntimeError']}</td>
        <td class="num">{t['scored']}</td>
      </tr>"""
        for t in targets
    )

    worst_rows = "\n".join(
        f"""      <tr>
        <td>{html.escape(f['target'])}</td>
        <td class="path">{html.escape(f['path'].split('/src/')[-1])}</td>
        <td class="num warn">{f['survived']}</td>
        <td class="num {band(f['score'], f['thresholds'])}">{pct(f['score'])}</td>
      </tr>"""
        for f in worst
    )

    overall = sum(t["counts"]["Killed"] + t["counts"]["Timeout"] for t in targets)
    overall_scored = sum(t["scored"] for t in targets)

    out = root / "index.html"
    out.write_text(
        INDEX_TEMPLATE.format(
            rows=rows,
            worst_rows=worst_rows,
            targets=len(targets),
            overall=f"{100.0 * overall / overall_scored:.1f}" if overall_scored else "—",
            scored=overall_scored,
        ),
        encoding="utf-8",
    )
    print(f"index written: {out}  ({len(targets)} targets, {overall_scored} scored mutants)")
    return 0


INDEX_TEMPLATE = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Grimoire — mutation testing</title>
<style>
  :root {{
    color-scheme: light dark;
    --bg: #fbfbfa; --fg: #1b1b19; --muted: #6a6a63; --line: #e2e2dc; --card: #ffffff;
    --high: #227a4b; --low: #9a6b00; --poor: #a33232;
  }}
  @media (prefers-color-scheme: dark) {{
    :root {{
      --bg: #16161a; --fg: #e8e8e4; --muted: #9a9a92; --line: #2c2c33; --card: #1d1d22;
      --high: #62c48d; --low: #d6a83a; --poor: #e07a6f;
    }}
  }}
  * {{ box-sizing: border-box; }}
  body {{ margin: 0; padding: 2.5rem 1.5rem 4rem; background: var(--bg); color: var(--fg);
    font: 15px/1.6 ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif; }}
  main {{ max-width: 68rem; margin: 0 auto; }}
  h1 {{ font-size: 1.5rem; margin: 0 0 .25rem; letter-spacing: -.01em; }}
  h2 {{ font-size: 1.05rem; margin: 2.5rem 0 .75rem; }}
  p {{ color: var(--muted); margin: 0 0 1rem; max-width: 46rem; }}
  .scroll {{ overflow-x: auto; border: 1px solid var(--line); border-radius: 10px; background: var(--card); }}
  table {{ border-collapse: collapse; width: 100%; font-size: 14px; }}
  th, td {{ text-align: left; padding: .55rem .8rem; border-bottom: 1px solid var(--line); white-space: nowrap; }}
  th {{ font-weight: 600; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; color: var(--muted); }}
  tr:last-child td {{ border-bottom: 0; }}
  .num {{ text-align: right; font-variant-numeric: tabular-nums; }}
  .path {{ font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 13px; white-space: normal; }}
  .high {{ color: var(--high); font-weight: 600; }}
  .low {{ color: var(--low); font-weight: 600; }}
  .poor {{ color: var(--poor); font-weight: 600; }}
  .warn {{ color: var(--poor); }}
  a {{ color: inherit; text-decoration-color: var(--muted); text-underline-offset: 3px; }}
  footer {{ margin-top: 3rem; color: var(--muted); font-size: 13px; }}
  code {{ font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: .92em; }}
</style>
</head>
<body>
<main>
  <h1>Mutation testing</h1>
  <p>{targets} targets, {scored} scored mutants, {overall}&thinsp;% killed overall. A
  survivor is a mutation no test noticed — a line the suite runs without asserting
  anything about. Click a target for its own report with the mutants inline in the source.</p>

  <div class="scroll">
  <table>
    <thead>
      <tr><th>Target</th><th class="num">Score</th><th class="num">Killed</th>
      <th class="num">Timeout</th><th class="num">Survived</th><th class="num">No cov.</th>
      <th class="num">Errors</th><th class="num">Scored</th></tr>
    </thead>
    <tbody>
{rows}
    </tbody>
  </table>
  </div>

  <h2>Where the survivors are</h2>
  <p>The twenty files with the most unkilled mutants. These are the places where adding an
  assertion buys the most — not necessarily the places with the worst score.</p>

  <div class="scroll">
  <table>
    <thead>
      <tr><th>Target</th><th>File</th><th class="num">Survived</th><th class="num">Score</th></tr>
    </thead>
    <tbody>
{worst_rows}
    </tbody>
  </table>
  </div>

  <footer>Generated by <code>scripts/mutation-report-index.py</code>. Timeouts count as
  killed — the mutant broke the code badly enough to hang a test. Errors (compile and
  runtime) are outside the score, but a large count means Stryker's Safe Mode dropped
  whole methods from the run, so the score covers less code than it looks. Colour bands
  follow the thresholds each report declares.</footer>
</main>
</body>
</html>
"""


if __name__ == "__main__":
    sys.exit(main(sys.argv))
