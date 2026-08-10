---
type: Concept
title: Circuit Breaker
description: Fail fast against a downstream dependency that is already struggling.
timestamp: 2026-07-02T00:00:00Z
tags:
  - concept/Resilience
  - pattern/CircuitBreaker
confidence: high
confidence_reason: "Book source, corroborated by official docs."
---

# Circuit Breaker

Track the failure rate of calls to a downstream dependency; once it crosses a
threshold, "open" the circuit and fail every call immediately for a cooldown period,
then allow a small number of trial calls through to test whether the dependency has
recovered before fully closing the circuit again. This protects a struggling
downstream dependency from being kept down by a thundering herd of retrying callers.
