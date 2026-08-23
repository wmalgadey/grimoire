---
type: Concept
title: Cache TTL Defaults
description: The default time-to-live applied to cache entries when none is set explicitly.
timestamp: 2026-05-01T00:00:00Z
tags:
  - concept/Caching
  - tech/dotnet
confidence: medium
confidence_reason: "Fixture data, not a real source."
inbound_links: 1
---

# Cache TTL Defaults

When a cache entry is written without an explicit expiry, the runtime applies a default
time-to-live of 30 seconds. Callers that need an entry to survive longer must set the
expiry themselves at write time.

## Choosing a value

The default suits read-heavy workloads whose upstream data changes slowly. Workloads that
tolerate staleness poorly should set a shorter expiry explicitly rather than relying on
it. Cache entries written through the batch import path are exempt and never expire.
