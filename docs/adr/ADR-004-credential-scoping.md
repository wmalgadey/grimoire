---
status: accepted
---

# ADR-004: Credential Scoping for the LLM API Key

## Context and Problem Statement

The Ingest agent calls the Anthropic Claude API, which requires an API key. A naive setup
injects such secrets as environment variables available to the whole Hub process, meaning
any current or future agent (or a compromised one) could read credentials meant for a
different service. Grimoire is introducing its first external credential here, so the
injection model needs to be decided deliberately rather than defaulting to "put it in the
process environment."

## Decision Drivers

- No agent should hold credentials beyond its own operational scope — an agent that has
  no reason to call a given service should have no way to read that service's secret.
- The mechanism must remain auditable and extensible as new agents and credentials are
  added later.
- Operational overhead must stay proportional to what is currently exactly one credential
  and one credential-consuming agent.

## Considered Options

1. Hub reads the secret from a local, git-ignored file and injects it only into the
   Ingest child process's environment at spawn time
2. Whole-Hub-process environment variables, inherited by every child process
3. A dedicated credential-gateway proxy: a separate process that all agents call through,
   which injects the correct secret at the network boundary based on which agent is
   calling, so no agent ever holds a raw secret itself

## Decision Outcome

Chosen option: **Option 1.**

The Hub reads the Claude API key from a local secrets file (e.g. `.env`, excluded from
git via `.gitignore`) and sets it only in the environment block passed to the Ingest
child process (ADR-002) at spawn time. No other in-process code path or channel has
access to it.

### Consequences

- Good, because it satisfies the least-privilege goal ("no agent holds secrets beyond its
  own scope") without introducing a new network-facing proxy service.
- Good, because it is trivially auditable — one file, one injection point, one consumer.
- Bad, because it does not scale cleanly to many agents/many credentials without manual
  per-spawn wiring; explicitly deferred: Option 3 (credential gateway) should be
  revisited once a second credential or a second agent creates a real cross-agent
  leakage risk to defend against, not preemptively.

## More Information

This is the general pattern for any future credential: the Hub reads it from a local,
git-ignored secrets file and injects it only into the specific child process that needs
it, never into its own process environment or any other agent's. A dedicated
credential-gateway proxy (Option 3) should be reconsidered once a second credential or a
second credential-consuming agent creates a real cross-agent leakage risk to defend
against, not adopted preemptively.

