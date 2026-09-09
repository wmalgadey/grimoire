#!/usr/bin/env python3
"""
Counts the repository's tracked text files in LLM tokens, grouped by area (backend
production code, backend tests, frontend, SDD artefacts, docs, ...).

Answers "how much context would this cost to read?" — useful when sizing what an agent can
plausibly be handed at once, and for watching the ratio between production code, tests and
specification prose over time.

CAVEAT — this is an approximation, not a Claude token count. `tiktoken` implements
OpenAI's BPE; Anthropic's tokenizer is a different one and is not available through it, so
the absolute numbers will not match what a Claude request is billed for. Ratios between
areas, and the same area measured over time, are the parts worth reading. For an exact
Claude count use Anthropic's `/v1/messages/count_tokens` endpoint.

Requires `tiktoken` (not a repository dependency — `pip install tiktoken`). Only the
counting itself needs it; the bucketing and aggregation below are pure and self-tested by
scripts/test_token_report.py.

Usage:
    python3 scripts/token-report.py                     # whole repository, by area
    python3 scripts/token-report.py --exclude-generated # drop recorded fixtures etc.
    python3 scripts/token-report.py --by-project        # per .csproj / frontend subtree
    python3 scripts/token-report.py --json              # machine-readable
"""

import argparse
import fnmatch
import json
import os
import subprocess
import sys

# Ordered longest-prefix-first: the first entry whose prefix matches a path wins, so
# "backend/src/" is consulted before the "backend/" catch-all.
AREA_PREFIXES = [
    ("backend/src/", "backend-src"),
    ("backend/tests/", "backend-tests"),
    ("backend/", "backend-buildconfig"),
    ("frontend/", "frontend"),
    ("specs/", "sdd-specs"),
    ("docs/", "docs"),
    (".claude/", "agent-instructions"),
    (".specify/", "agent-instructions"),
    (".github/", "ci"),
    ("scripts/", "scripts-deploy"),
    ("deploy/", "scripts-deploy"),
]

AREA_FILES = {
    "CLAUDE.md": "agent-instructions",
    "AGENTS.md": "agent-instructions",
    "CONTRIBUTING.md": "agent-instructions",
    "README.md": "agent-instructions",
}

AREA_ORDER = [
    "backend-src", "backend-tests", "backend-buildconfig", "frontend",
    "sdd-specs", "docs", "agent-instructions", "scripts-deploy", "ci", "other",
]

# Always skipped: binary or vendored, never worth a token count.
ALWAYS_EXCLUDED = ["frontend/bun.lock", "**/*.trx", "**/*.flf", "**/*.svg", "**/*.ico", "**/*.png"]

# Skipped only under --exclude-generated: machine-produced records that live in the tree but
# are not authored content, and would otherwise dominate their area's total.
GENERATED = [
    "backend/tests/Grimoire.AgentEvals/Fixtures/recordings/**",
    "docs/code-complexity-analysis.json",
]


def area_for(path):
    """The reporting area one repository-relative path belongs to."""
    if path in AREA_FILES:
        return AREA_FILES[path]
    for prefix, area in AREA_PREFIXES:
        if path.startswith(prefix):
            return area
    return "other"


def project_for(path):
    """
    The finer-grained unit a path belongs to, or None if it is outside the code trees:
    one .NET project for backend paths, one top-level subtree for the frontend.
    """
    parts = path.split("/")
    if len(parts) > 2 and parts[0] == "backend" and parts[1] in ("src", "tests"):
        return f"{parts[1]}/{parts[2]}"
    if path.startswith("frontend/src/") and len(parts) > 3:
        return f"frontend/{parts[2]}"
    if path.startswith("frontend/"):
        return "frontend/(config)"
    return None


def is_excluded(path, patterns):
    return any(fnmatch.fnmatch(path, pattern) for pattern in patterns)


def tracked_files(repo):
    output = subprocess.run(
        ["git", "-C", repo, "ls-files", "-z"], capture_output=True, check=True).stdout
    return [entry.decode() for entry in output.split(b"\0") if entry]


def read_text(repo, path):
    """The file's text, or None when it is absent, a symlink, or not UTF-8 text."""
    full = os.path.join(repo, path)
    if not os.path.isfile(full) or os.path.islink(full):
        return None
    with open(full, "rb") as handle:
        data = handle.read()
    if b"\0" in data[:8192]:
        return None
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        return None


def collect(repo, paths, count_tokens, exclude_generated=False, read=read_text):
    """
    Aggregates token/file/line/byte totals per area and per project.

    `count_tokens` takes the file's text and returns its token count; injecting it keeps
    this function free of the tiktoken dependency (and testable without it).
    """
    excluded = list(ALWAYS_EXCLUDED) + (GENERATED if exclude_generated else [])
    areas, projects, skipped = {}, {}, []
    totals = {"tokens": 0, "files": 0, "lines": 0, "bytes": 0}

    for path in paths:
        if is_excluded(path, excluded):
            skipped.append(path)
            continue
        text = read(repo, path)
        if text is None:
            skipped.append(path)
            continue

        entry = {
            "tokens": count_tokens(text),
            "files": 1,
            "lines": text.count("\n"),
            "bytes": len(text.encode("utf-8")),
        }
        for bucket, key in ((areas, area_for(path)), (projects, project_for(path))):
            if key is None:
                continue
            target = bucket.setdefault(key, dict.fromkeys(entry, 0))
            for field, value in entry.items():
                target[field] += value
        for field, value in entry.items():
            totals[field] += value

    return {"areas": areas, "projects": projects, "totals": totals, "skipped": skipped}


def _sorted_items(bucket, order=None):
    if order:
        known = [(k, bucket[k]) for k in order if k in bucket]
        rest = sorted(
            ((k, v) for k, v in bucket.items() if k not in order), key=lambda kv: -kv[1]["tokens"])
        return known + rest
    return sorted(bucket.items(), key=lambda kv: -kv[1]["tokens"])


def render(result, encoding, by_project=False):
    lines = []
    totals = result["totals"]
    tokens_total = totals["tokens"] or 1

    lines.append(f"Encoding: {encoding} (approximation — not a Claude token count)")
    lines.append(f"{totals['files']:,} files, {len(result['skipped'])} skipped\n")
    header = f"{'Area':<22}{'Tokens':>12}{'Files':>8}{'Lines':>9}{'KB':>8}{'Tok/Line':>10}{'Share':>8}"
    lines.append(header)
    lines.append("-" * len(header))
    for name, row in _sorted_items(result["areas"], AREA_ORDER):
        per_line = row["tokens"] / row["lines"] if row["lines"] else 0.0
        lines.append(
            f"{name:<22}{row['tokens']:>12,}{row['files']:>8,}{row['lines']:>9,}"
            f"{row['bytes'] / 1024:>8,.0f}{per_line:>10.1f}{row['tokens'] / tokens_total * 100:>7.1f}%")
    lines.append("-" * len(header))
    per_line = totals["tokens"] / totals["lines"] if totals["lines"] else 0.0
    lines.append(
        f"{'TOTAL':<22}{totals['tokens']:>12,}{totals['files']:>8,}{totals['lines']:>9,}"
        f"{totals['bytes'] / 1024:>8,.0f}{per_line:>10.1f}{100:>7.1f}%")

    if by_project:
        lines.append("\nBy project:")
        for name, row in _sorted_items(result["projects"]):
            lines.append(f"  {name:<40}{row['tokens']:>10,}{row['files']:>6} files")
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[1])
    parser.add_argument("--repo", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    parser.add_argument("--encoding", default="o200k_base", help="tiktoken encoding (default: o200k_base)")
    parser.add_argument("--exclude-generated", action="store_true",
                        help="skip recorded eval fixtures and generated analysis JSON")
    parser.add_argument("--by-project", action="store_true", help="also break down per .NET project / frontend subtree")
    parser.add_argument("--json", action="store_true", help="emit JSON instead of a table")
    args = parser.parse_args(argv)

    try:
        import tiktoken
    except ImportError:
        print("token-report.py needs tiktoken: pip install tiktoken", file=sys.stderr)
        return 2

    encoder = tiktoken.get_encoding(args.encoding)
    result = collect(
        args.repo,
        tracked_files(args.repo),
        lambda text: len(encoder.encode(text, disallowed_special=())),
        exclude_generated=args.exclude_generated)

    if args.json:
        print(json.dumps({"encoding": args.encoding, **result}, indent=2, sort_keys=True))
    else:
        print(render(result, args.encoding, by_project=args.by_project))
    return 0


if __name__ == "__main__":
    sys.exit(main())
