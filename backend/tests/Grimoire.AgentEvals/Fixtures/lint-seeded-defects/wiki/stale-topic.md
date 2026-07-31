---
type: Concept
title: Sticky Sessions
description: Route a client's requests to the same backend instance for session affinity.
timestamp: 2025-01-05T00:00:00Z
tags:
  - concept/LoadBalancing
  - pattern/StickySessions
confidence: low
confidence_reason: "Single blog-post source, no corroboration; likely superseded practice."
last_reviewed: "2025-01-05"
---

# Sticky Sessions

A load balancer routes all of a given client's requests to the same backend instance
(via a cookie or client IP hash), so per-instance in-memory session state stays valid
across requests without a shared session store.
