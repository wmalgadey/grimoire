---
type: Concept
title: Idempotency
description: An operation that can be applied multiple times without changing the result beyond the first application.
timestamp: 2026-07-14T00:00:00Z
tags:
  - concept/Idempotency
  - concept/Resilience
confidence: high
confidence_reason: "Standard distributed-systems terminology, corroborated by multiple sources."
---

# Idempotency

An operation is idempotent when applying it more than once has the same effect as
applying it exactly once. This matters most at retry boundaries: a client that
retries a failed request after an ambiguous timeout must not double-apply the
operation on the server. Related to [[tech/circuit-breaker]]: retried calls behind a
circuit breaker are safe to retry only when the underlying operation is idempotent.
