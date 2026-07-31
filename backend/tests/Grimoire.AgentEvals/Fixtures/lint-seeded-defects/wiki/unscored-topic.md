---
type: Concept
title: Write-Behind Caching
description: Acknowledge a write immediately and persist it to storage asynchronously.
timestamp: 2026-04-10T00:00:00Z
tags:
  - concept/Caching
  - pattern/WriteBehind
---

# Write-Behind Caching

A write is accepted and acknowledged to the caller as soon as it lands in the cache;
the cache asynchronously flushes it to the underlying system of record afterward. This
lowers write latency at the cost of a durability window in which an acknowledged write
can be lost if the cache node fails before flushing.
