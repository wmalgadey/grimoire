---
status: accepted
---

# ADR-006: Agent Tool-Use Loop and Guarded Tool Boundary

## Context and Problem Statement

Constitution v1.1.0 (Principle V) requires that all wiki-content judgment be exercised by
an LLM agent under versioned instruction files, while backend code owns only a
deterministic harness that enforces guardrails "at the moment the agent invokes the
tool". Feature 002-agentic-ingest-core replaces the deterministic ingest pipeline with
exactly such an agent. This introduces structural boundaries no existing ADR covers: how
the agent loop executes, where guardrails physically sit, what tool surface the agent
gets, how a versioned deny-by-default policy is represented and evaluated, and how a
failed multi-file agent run is rolled back. These are cross-cutting shapes that every
future agentic feature (Query, Lint) will inherit, so they must be fixed by ADR rather
than decided implicitly inside one feature.

## Decision Drivers

- Guardrails must intercept every tool call at invocation time (Principle V) — not as
  post-hoc validation — and denials must be recorded while the run continues (FR-008).
- Harness contracts must be hermetically testable without live LLM calls or API keys
  (Principle II).
- The ADR-002 Hub↔agent contract (child process, CLI args, file-based results, exit
  code) must not be broken; containerizing later must stay a deployment change.
- No new infrastructure or language runtime (Principle IV, ADR-001).
- A failed run must leave the wiki exactly as it was (FR-013), for arbitrary multi-file
  agent activity, without risking unrelated user edits in the working tree.
- Wiki-maintenance conventions must live solely in instruction files; the tool surface
  must not smuggle content semantics back into backend code (Principle V, FR-010).

## Considered Options

1. **In-process manual tool-use loop** over the Anthropic Messages API, with a guarded
   tool executor, a JSON deny-by-default path-prefix policy, and a write journal with
   reverse rollback
2. SDK-managed automatic tool runner (`BetaToolRunner`), guardrails as pre-registered
   validators
3. External agent runtime (Claude Code CLI headless / Agent SDK subprocess), guardrails
   as hook-based filters
4. Semantic wiki tools (`create_wiki_page`, `supersede_page`, …) instead of file-level
   tools

## Decision Outcome

Chosen option: **Option 1 — in-process manual tool-use loop with a guarded file-tool
boundary.**

- **Loop**: `Grimoire.IngestAgent` drives the Messages API directly (stable, non-beta
  surface): system prompt = instruction set loaded verbatim; each `tool_use` block is
  dispatched through the harness's `GuardedToolExecutor`; results (including denials as
  `is_error` tool results) are returned; the loop ends at `end_turn` or a turn/token cap.
  The loop depends on an `IModelClient` seam so harness tests script model behavior
  deterministically with no API key.
- **Tool surface**: exactly three file-level tools — `list_files`, `read_file`,
  `write_file` — so all content semantics stay in instruction files. Adding a tool is
  the sanctioned form of backend extension; adding content judgment is not.
- **Policy**: a git-tracked, versioned JSON file (`agents/ingest/policy.json`),
  deny-by-default, path-prefix allow rules for `read` and `write` scopes, evaluated by
  dependency-free domain logic against canonicalized absolute paths (traversal and
  symlink tricks collapse before matching). No rule → denied. Missing/unparseable
  policy → run fails before any wiki change. Policy identity (version + SHA-256) is
  recorded in every task artifact.
- **Rollback**: the executor keeps an in-memory write journal (path + prior state) and
  restores journaled paths in reverse order on failure. The task artifact and failure
  log entry are harness-owned and exempt.
- **Structural enforcement** (Principle III/V): an architecture test verifies that,
  within the agent, filesystem-write APIs are reachable only from the guarded-tool and
  harness-record namespaces, proven live by a Red/Green probe.

### Consequences

- Good, because the guardrail sits at the single physical point every agent action must
  pass through — deny-by-default is structural, and prompt-injected content can never
  widen authority (the policy is evaluated in harness code the conversation cannot
  touch).
- Good, because the journal at the same chokepoint makes rollback complete by
  construction: no write can bypass it without also bypassing the arch-tested boundary.
- Good, because `IModelClient` keeps every harness guarantee (SC-001…SC-005) hermetically
  testable, and the ADR-002 CLI contract is preserved (two new arguments, same shape).
- Bad, because a manual loop is more code than an SDK-managed runner and must track API
  evolution (tool schema format, stop reasons) ourselves; accepted as the price of
  owning the interception point.
- Bad, because whole-file `write_file` makes large-page edits token-expensive; accepted
  for v1 — a patch/edit tool is a compatible later addition to the registry.
- Neutral, because Query and Lint agents inherit this pattern (loop + guarded tools +
  per-agent policy file) when they arrive; their ADRs need only declare their tool
  registries and policy scopes.

## More Information

Detailed rationale and rejected alternatives: `specs/002-agentic-ingest-core/research.md`
(R1, R2, R5, R6, R7). Contracts: `specs/002-agentic-ingest-core/contracts/`
(guarded-tools.md, safety-policy.md). This ADR must be **Accepted** before
`/speckit-tasks` runs for feature 002 (Constitution, Spec-Kit Workflow step 4).
