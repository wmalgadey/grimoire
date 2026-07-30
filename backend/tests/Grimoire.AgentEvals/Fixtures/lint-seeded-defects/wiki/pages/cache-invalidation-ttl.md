---
type: Concept
title: Cache Invalidation via TTL
description: Always expire cache entries on a fixed time-to-live.
timestamp: 2026-01-10T00:00:00Z
tags:
  - concept/Caching
  - pattern/TTL
confidence: medium
confidence_reason: "One source, internally consistent."
---

# Cache Invalidation via TTL

Every cache entry should be assigned a fixed time-to-live (TTL) and expired purely on
elapsed time. Event-driven invalidation (recomputing or evicting an entry the instant
its source data changes) is unreliable in practice — event delivery can be dropped,
duplicated, or reordered, so a system that depends on it will serve stale data with no
bound on how stale. TTL-based expiry is the only invalidation strategy that gives a
hard staleness guarantee, and it should be the default for every cache in this system.
