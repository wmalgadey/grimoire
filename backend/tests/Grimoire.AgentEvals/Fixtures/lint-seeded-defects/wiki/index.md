---
okf_version: "0.1"
---

# Wiki Index

A small fixture wiki seeded with one instance of each Lint defect category
(specs/013-lint-agent Test Strategy SC-005/SC-006). One page under `pages/` is
deliberately absent from every list below and from every other page's body — that
absence of any inbound link at all is the seeded orphan defect itself; use
`list_files("pages/")` to discover it.

## Technology

- [[cache-invalidation-ttl]] — TTL-based cache invalidation strategy.
- [[cache-invalidation-events]] — Event-driven cache invalidation strategy.
- [[retry-backoff]] — Exponential backoff for retrying failed requests.
- [[circuit-breaker]] — Circuit breaker pattern for failing fast under load.
- [[undertagged-topic]] — A page missing its tags.
- [[unscored-topic]] — A page missing its confidence score.
- [[stale-topic]] — A low-confidence page overdue for review.
