---
type: Concept
title: Read-Through Caching
description: Populate the cache lazily on a miss, from the system of record.
timestamp: 2026-04-02T00:00:00Z
confidence: medium
confidence_reason: "One source, internally consistent."
---

# Read-Through Caching

On a cache miss, the caching layer itself (not the caller) fetches the value from the
system of record, stores it in the cache, and returns it — so every caller sees the
same simple "read from cache" interface regardless of whether the value was already
cached.
