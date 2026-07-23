---
type: Concept
title: Guarded Write Journal
description: How the guarded write path journals prior file state for rollback on failure.
tags:
  - concept/Write-Journal
  - tech/dotnet
confidence: high
confidence_reason: "Directly reflects the accepted architecture decision."
---

Before the guarded tool executor overwrites any file, it first records that file's
prior state — its previous content if it existed, or the fact that it did not exist —
into an in-memory write journal. If the agent run later fails (a turn cap is exceeded,
an unrecoverable error occurs, or all attempted writes were denied), the journal can
roll every recorded write back in reverse order: restoring previous content for files
that existed, and deleting files that were newly created. This gives each run
all-or-nothing semantics for its writes without needing a database transaction — the
journal itself is the transaction log, held only in the process's memory for the
duration of one run.

The Query agent never uses this journal's rollback path, since it has no write tool at
all — the journal type exists in the shared agent runtime library but is only ever
populated by an agent that actually writes.
