---
type: Concept
title: Idempotency Keys
description: Deduplicate retried requests using a client-supplied idempotency key.
timestamp: 2026-02-14T00:00:00Z
tags:
  - concept/Resilience
  - pattern/Idempotency
confidence: medium
confidence_reason: "One source, internally consistent."
---

# Idempotency Keys

A client attaches a unique idempotency key to a request; the server records which keys
it has already processed and returns the original result for a repeated key instead of
re-executing the operation. This makes retried requests (e.g. after a timeout with an
unknown outcome) safe to resend without risking a duplicate side effect.
