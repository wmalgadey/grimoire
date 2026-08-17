---
name: deploy-server
description: Put a git ref live on the self-hosted server and report what it does — use when asked to deploy, redeploy, roll back, or check what is running there ("deploy pr/95", "put main on the server", "was läuft gerade auf dem Server?"). Only for a Claude Code session running ON that server.
allowed-tools: Bash, Read, Grep, Glob
---

# deploy-server — Put a ref live and say what it does

This skill belongs to the Claude Code session running on the deployment host, usually
driven over Remote Control from a phone or browser. The operator's message is short
("deploy pr/95"); the useful reply is what actually happened.

`deploy/server/grimoire-server` does the work. **Never** reimplement its steps with
individual `git`/`docker compose` commands — it owns the checkout, the compose overlay
and the deployment record, and running the pieces by hand desynchronises them.

## Are you on the server?

If `grimoire-server` is not on `PATH` and `deploy/server/grimoire-server` does not exist
in this checkout, this is not the deployment host. Say so instead of deploying anything —
a developer laptop runs the stack straight from `docker compose up`, per `deploy/README.md`.

## Deploy

Map what the operator said to a ref and run one command:

| They say | Ref |
| --- | --- |
| "PR 95", "#95", "the deployment PR" | `pr/95` |
| "main", "latest" | `main` |
| a branch name | that name |
| "the previous one", "roll back" | `grimoire-server rollback` |

```bash
grimoire-server deploy pr/95
```

It fetches, refuses to move a dirty tree, checks out the ref, rebuilds the images, replaces
the containers, and runs four smoke checks. It is slow — several minutes on a cold cache —
and its progress goes to stderr. Do not add a timeout that is shorter than the build.

Then report, in this order and briefly:

1. what is now live — ref, short sha, commit subject;
2. the smoke result, and for a failure **which** check failed;
3. the URL, noting it is reachable through their SSH tunnel (`ssh -L 8080:127.0.0.1:8080`).

## When a deployment fails

The stack is deliberately left running — it is usually the thing the operator wanted to
look at. Diagnose before proposing anything:

```bash
grimoire-server logs hub          # or: dashboard
```

Then say what broke and offer the choice: fix it on the branch, or
`grimoire-server rollback` to the commit that ran before. **Do not roll back on your own
initiative** — that discards the state they asked to see. Ask first.

Two failures have known causes worth naming immediately rather than investigating:

- Only the SignalR check failed → the realtime upgrade is not getting through. If
  anything sits in front of the stack, that is the first place to look; otherwise the Hub
  is up but its SignalR mapping is not answering.
- The Hub restarts in a loop and the log names `secrets_file` → `.env` is missing, empty,
  or unreadable by the container's uid.

## Status

For "what is running?" or "is there anything new?":

```bash
grimoire-server status
```

It fetches first and prints the new commits on the deployed ref, if any. Report the
deployed ref and age, whether origin has moved, and whether the stack answers its checks.
Offer the deployment; do not start one that was not asked for.

## Rules for this host

- **This checkout is a deployment target, not a workspace.** Never commit, never push,
  never edit tracked files here. The fix for a broken deployment belongs on the developer
  machine and on GitHub; this host only runs refs that already exist on origin.
- **Never print `.env` or any part of it.** It holds the Anthropic token. Checking that it
  exists (`test -f .env`) is fine; reading it back over Remote Control is not.
- **Never widen the port binding** (`GRIMOIRE_BIND=0.0.0.0`) or remove a volume. The stack
  has no TLS and no authentication, and the volumes hold the wiki. If the operator asks
  for either, confirm they mean it and repeat what it exposes.
- **Ask before destroying state.** `down --volumes` deletes the wiki.

## Testing the deployment for them

"Check whether the board renders" is a request to exercise the running stack, not to read
the source. Prefer HTTP against `http://127.0.0.1:8080` (`/api/board`, `/api/tasks`, the
SPA document) and the Hub's own CLI for anything deeper:

```bash
docker compose --project-directory "$GRIMOIRE_REPO" -f "$GRIMOIRE_REPO/compose.yaml" \
  exec hub dotnet /app/Grimoire.Hub.dll --help
```

Report what you observed — status codes, payload shape, log lines — not what the code says
should happen.
