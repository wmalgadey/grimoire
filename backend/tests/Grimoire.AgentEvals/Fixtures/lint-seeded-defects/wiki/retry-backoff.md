---
type: Concept
title: Exponential Backoff for Retries
description: Retry failed requests with exponentially increasing delay and jitter.
timestamp: 2026-03-01T00:00:00Z
tags:
  - concept/Resilience
  - pattern/Retry
confidence: high
confidence_reason: "Book source, corroborated by official docs."
---

# Exponential Backoff for Retries

When a request fails transiently, retry it after a delay that grows exponentially with
each attempt, with random jitter added to avoid synchronized retry storms across many
clients. This bounds the load a struggling downstream service sees from retrying
clients, at the cost of higher latency for the eventually-successful request.
