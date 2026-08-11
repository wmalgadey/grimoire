---
type: Concept
title: Autoscaling
description: Automatically adjusting the number of running replicas of a service based on observed load.
timestamp: 2026-01-01T00:00:00Z
tags:
  - concept/Autoscaling
  - concept/Kubernetes
confidence: medium
confidence_reason: "Single existing source; not yet corroborated."
---

Autoscaling adjusts replica count for a service automatically instead of requiring a
human to resize it manually. The controller observes some signal — CPU/memory
utilization, queue depth, or a custom metric — and computes a desired replica count on
an interval.
