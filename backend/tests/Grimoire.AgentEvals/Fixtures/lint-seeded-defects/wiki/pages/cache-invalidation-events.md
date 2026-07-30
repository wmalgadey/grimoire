---
type: Concept
title: Event-Driven Cache Invalidation
description: Invalidate cache entries immediately when their source data changes.
timestamp: 2026-06-20T00:00:00Z
tags:
  - concept/Caching
  - pattern/EventDriven
confidence: medium
confidence_reason: "One source, internally consistent."
---

# Event-Driven Cache Invalidation

TTL-based expiry is unreliable under bursty writes: a fixed time window means an entry
can serve stale data for up to the full TTL after its source changes, and there is no
way to shorten that window without also discarding entries that are still fresh.
Event-driven invalidation — evicting or recomputing an entry the moment a change event
for its source data arrives — is the recommended default: it bounds staleness to the
event-delivery latency instead of an arbitrary fixed window, and should replace
TTL-based expiry everywhere in this system.
