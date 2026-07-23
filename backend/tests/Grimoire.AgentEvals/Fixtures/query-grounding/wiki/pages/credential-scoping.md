---
type: Concept
title: Credential Scoping
description: How the Ingest agent's Anthropic API key is scoped to its own child-process environment.
tags:
  - concept/Credential-Scoping
  - tech/dotnet
confidence: high
confidence_reason: "Directly reflects the accepted architecture decision."
---

The Hub never holds the Anthropic API key in its own process environment. Instead, a
local secrets loader reads the key from a git-ignored `.env` file at startup and injects
it only into the environment of the spawned Ingest agent child process, immediately
before that process starts. The Hub's own process environment is never mutated. This
means a compromised or misbehaving Hub process cannot leak the credential simply by
inspecting its own environment variables — the key exists only inside the short-lived
child process that actually calls the Anthropic API.

The same scoping model also governs the Query agent process: it receives the credential
the same way, injected into its own environment at spawn, never inherited from or
written into the Hub's process environment.
