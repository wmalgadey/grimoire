---
type: Concept
title: Runtime Path Configuration
description: How Grimoire resolves every runtime file location from a single composition point.
tags:
  - concept/Runtime-Paths
  - tech/dotnet
confidence: high
confidence_reason: "Directly reflects the accepted architecture decision."
---

Every file-system location the Hub touches at runtime — the wiki content root, the
internal data directory, the SQLite operational-state database, the secrets file, and
each agent's instruction directory — is resolved in exactly one place at startup: a
single path resolver reads configuration (command line, environment variables, a config
file, or code defaults, in that precedence order), validates every required input
exists, auto-creates writable data directories that are missing, and reports the
effective value and source of each location. No other part of the codebase is allowed
to read the process's ambient working directory directly; everything downstream
consumes the already-resolved path set instead of rediscovering locations on its own.

This is why adding a new runtime location — such as the Query agent's own instruction
directory or its Query Run Artifact storage — means extending that one resolver, not
scattering path logic across the codebase.
