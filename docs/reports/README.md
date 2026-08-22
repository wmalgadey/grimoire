# docs/reports/

Generated analysis output. Nothing here is written by hand and nothing here is a
requirement — this directory sits under the catch-all row of the document map in
`CLAUDE.md`: reports are read by whoever is deciding where to spend test effort, never
cited in a spec, plan, or ADR.

Its reader is a developer asking "how much is the suite actually asserting?", and the
answer arrives by running the tool rather than by opening a committed file.

## `mutation/` — Stryker mutation testing

Written by [`scripts/mutation-test.sh`](../../scripts/mutation-test.sh), one subdirectory
per target plus an `index.html` over all of them:

```
docs/reports/mutation/
├── index.html                  # every target, its score, links into the reports below
├── domain/
│   ├── reports/mutation-report.html
│   ├── reports/mutation-report.json
│   └── run.log
├── hub/
└── frontend/
```

**The contents are gitignored**, deliberately: a single target's report is several
megabytes of self-contained HTML and a full run is tens of them, none of it reviewable in
a diff. To share a result, publish the HTML somewhere it can be served, or quote the
numbers — do not commit the files. Removing the `docs/reports/mutation/` line from
`.gitignore` is a one-line change if a snapshot is ever genuinely wanted; think about the
repository size first.

To read a report on a headless server, serve the directory rather than copying it around:

```bash
python3 -m http.server --directory docs/reports/mutation 8099
# or, on a host that has only a container runtime — the same case
# scripts/mutation-test-docker.sh exists for:
docker run --rm -p 8099:80 -v "$PWD/docs/reports/mutation:/usr/share/nginx/html:ro" nginx:alpine
```

`CONTRIBUTING.md` (section "Mutation testing") covers what the numbers mean and which
targets exist.
