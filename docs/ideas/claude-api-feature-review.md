# Review: Claude API features (GA and beta) against Grimoire's agent runtime

> **Role of this document.** Decision context (source material), in the sense of the
> Document Map in `CLAUDE.md`: it is **not binding** for SDD. Its declared reader is the
> author of `/speckit-specify` input or of an ADR for any of the issues listed in
> §6 — the review exists to be absorbed into those, not cited as a requirement.
> Statements here become enforceable only once extracted into the constitution or an
> Accepted ADR.

**Date:** 2026-08-18
**Trigger:** Question whether the beta and GA features of the Claude API / SDK offer
anything Grimoire should be using — reviewed against
[the API overview](https://platform.claude.com/docs/en/api/overview) and the current
`Grimoire.AgentRuntime` source.

**Verdict (TL;DR):** One GA feature is being left on the table for no reason (prompt
caching). A handful of small, adapter-local corrections are worth making regardless of
anything else. Everything genuinely interesting beyond that — thinking/effort, task
budgets, compaction, mid-conversation system messages — is **gated on the model tier**,
and we run the tier with the fewest of those features. The managed/hosted end of the
platform (Managed Agents, Agent Skills, MCP connector) is a poor fit by construction:
adopting it means giving up the guarded-write boundary that Principle V exists to protect.

---

## 1. What we use today

`Grimoire.AgentRuntime` talks to the Anthropic C# SDK (`Anthropic` 12.40.0) through
`IModelClient` → `AnthropicModelClient`. The request it builds is the minimum viable one:

```csharp
// backend/src/Grimoire.AgentRuntime/Core/Adapters/Anthropic/AnthropicModelClient.cs:84-91
var createParams = new MessageCreateParams
{
    Model = ModelId,
    MaxTokens = 8096,
    System = systemPrompt,
    Messages = messages,
    Tools = toolsList,
};
```

Model, output cap, system prompt, messages, tools. No caching, no thinking, no effort, no
output config, no context management, no strict schemas. Around it:

- a hand-rolled tool-use loop (`Core/AgentLoop.cs`) with a turn cap of 50 and a token cap
  of 200,000, both hardcoded;
- three guarded tools — `list_files`, `read_file`, `write_file`
  (`Guardrails/ToolRegistry.cs`), narrowed per agent;
- streaming on the Query path only (`onTextDelta`), non-streaming for Ingest and Lint;
- a replay adapter at the port for evals (ADR-012).

**Which model actually runs matters for everything below.** `.env-example:2` sets
`GRIMOIRE_INGEST_MODEL="claude-haiku-4-5"` and every committed eval recording was captured
with it, while `AnthropicModelClient.cs:21` hardcodes a *different* tier
(`claude-opus-4-8`) as the fallback default. The practical answer is Haiku 4.5 — see #102
for why the ceiling ended up there.

---

## 2. What the API offers

From the overview page, split as the platform itself splits it:

**General Availability** — Messages API, Message Batches, Token Counting, Models API. On
top of the Messages API, also GA: prompt caching, adaptive thinking and
`output_config.effort`, structured outputs / strict tool use, citations, PDF and document
input, fine-grained tool streaming, programmatic tool calling, the client-implemented
bash / text-editor / memory tools, server-side web search and web fetch, `stop_details`,
mid-conversation system messages, and Workload Identity Federation.

**Beta** — Files API, Skills API, and the Managed Agents triad (Agents, Sessions,
Environments); plus, on the Messages API, context editing, compaction, task budgets, the
MCP connector, the advisor tool, cache diagnostics, and fast mode.

---

## 3. Worth adopting

Ordered by value per unit of disruption. Everything in this section stays **below the
`IModelClient` port** except where noted, so ADR-010 containment holds and no boundary
moves.

### 3.1 Prompt caching (GA, works on the tier we run) — #115

The largest single lever, and the one with no argument against it. Every turn re-sends and
re-pays for a prefix the project already forces to be stable: the system prompt is loaded
byte-exact and never mutated per turn (ADR-007), and the tool set is fixed per run. The
three agents' system prompts are 2,276 / 2,624 / 3,071 words — comfortably past the
~1024-token minimum, so the system block alone would cache. With a turn cap of 50, that
prefix can be billed 50 times.

One coupling to respect: `usage.input_tokens` excludes cached reads, so the numbers the
token cap sees change. That makes this inseparable from the cap-accounting decision in
#107 — decide them together.

### 3.2 Small corrections found on the way — #119, #120, #122, #123, #126

None of these is an API feature adoption; they are places where the current code is
already wrong or careless about the provider contract.

- **A refusal is reported as a protocol error** (#119). `ModelStopReason.Refusal` exists in
  the port and is normalized correctly, but `AgentLoop.cs:178` has no case for it, so a
  documented outcome arrives as "unexpected stop_reason" — and the `stop_details` the API
  sends with it (category, explanation) are never read. The Ingest agent processes
  untrusted external documents by design, which is the input class most likely to trigger
  one.
- **All provider errors are terminal** (#120). One `catch (AnthropicApiException)` on both
  paths, and nothing downstream branches on the status code that `ModelApiException`
  already carries — a 429 fails a run as permanently as a 400, discarding the work, while
  ADR-025's reactivation machinery sits unused for lack of a signal.
- **`MaxTokens = 8096` is hardcoded** (#122) — not per-agent, not configurable, not derived
  from the model's capability, and it reads like a typo for 8192. A truncated `write_file`
  is still a syntactically valid tool call, so the failure is silent.
- **Full request and response bodies are logged at `Information`** (#123). Every system
  prompt, every ingested source document and every wiki page body, duplicated per turn into
  the process log. A debugging aid left switched on; ADR-005's real telemetry does not
  depend on it, and `GRIMOIRE_MODEL_CAPTURE_PATH` already exists for capture.
- **The harness's "Continue the task." shares a channel with untrusted content** (#126).
  `AgentLoop` tells the agent not to trust the `user` channel, then uses the `user` channel
  to give it orders. Harmless today; wrong shape for the boundary.

### 3.3 Strict tool use (GA, no tier restriction) — #127

`strict: true` makes the provider guarantee schema-valid `tool_use.input`, removing a class
of wasted turn. It is explicitly **not** a guardrail: `strict` is about shape,
`GuardedToolExecutor` is about authorization, and a schema-valid write to a forbidden path
must still be denied by us. Worth a test that says exactly that.

### 3.4 The model tier is the real gate — #117

Four of the features that would most obviously help are unavailable on Haiku 4.5:

| Feature | Haiku 4.5 |
| --- | --- |
| Prompt caching | available |
| Context editing (beta) | available (not tier-restricted) |
| Strict tool use | available |
| Adaptive thinking + `effort` | **no** — `effort` errors; thinking only via deprecated fixed budgets |
| Task budgets (beta) | **no** |
| Compaction (beta) | **no** |
| Mid-conversation system messages | **no** |

This is worth stating as its own decision rather than discovering it one feature at a time.
Adaptive thinking in particular maps directly onto what Principle V calls agent judgment —
update-vs-create, supersession, categorization, confidence — and today none of that work is
done with thinking enabled, because the tier cannot.

There is a second, independent blocker for two of these: the port's conversation vocabulary
has only text, tool_use and tool_result blocks, and `AgentLoop` rebuilds the assistant turn
from those three (#118). Thinking blocks and compaction blocks must be echoed back
*unchanged* on the following turn, so both features are blocked at the port rather than at
the adapter. Worth settling separately, so that whichever feature is adopted afterwards
stays adapter-local.

### 3.5 Context editing (beta, not tier-restricted) — #124

`AgentLoop` never discards anything: the twentieth turn still carries the full text of the
first nineteen `read_file` results. Context editing clears stale tool results server-side
while the run continues.

Its interest is that it is a **third option** for #108, which currently frames the choice as
(A) teach the agent to read less via the instruction file, or (B) shard the run in the
harness. Unlike B, context editing moves no judgment into the harness — the agent still
decides what to read; only the transcript is pruned. And unlike compaction it is reachable
from the tier we are on today.

### 3.6 Task budgets (beta, tier-gated) — #125

Grimoire's caps are hard, invisible ceilings: crossing one destroys the run
(`AgentLoopCapException`, "Rolled back"). The agent cannot pace itself against a limit it
cannot see. A task budget is the advisory counterpart — the model sees a countdown and
wraps up. The hard caps stay as the backstop. Blocked on #117, and it presumes #107's cap
contract is settled first.

---

## 4. Deliberately not adopting

Recorded with reasons so the next reviewer does not re-derive them.

- **Managed Agents (Agents / Sessions / Environments).** Anthropic would own both the agent
  loop and the sandbox. We would give up the guarded-tool boundary (Principle V),
  cross-process write coordination (ADR-015), run supervision (ADR-008), our own telemetry
  (ADR-005) and recorded-replay evals (ADR-012). The harness *is* the product here; this
  replaces it.
- **Agent Skills on the Messages API.** Requires the `code_execution` container — a
  server-side sandbox, incompatible with local guarded writes. Also collides with ADR-007's
  "exactly one system prompt file per agent".
- **MCP connector.** Tool calls would bypass `GuardedToolExecutor` — a direct Principle V
  violation, not discussable without a new ADR. (Note the contrast with #102, where MCP
  points the *other* way: there we would *host* the server so our handlers stay the
  interception point. That is the opposite direction of trust.)
- **Server-side web search / web fetch.** Would bypass `IUrlContentFetcher` (ADR-010 P3)
  and its submission-time semantics — fetch once, persist the result, never re-fetch
  (FR-010). Conceivable for a future verification scenario; not for ingest.
- **Message Batches.** 50% cheaper, but incompatible with a tool loop — every turn needs
  tool results back. Our queue is asynchronous; the loop is not batchable.
- **Native PDF/document input.** Could replace the MarkItDown subprocess behind
  `IMarkdownConverter`, but should not: conversion is deliberately deterministic harness
  work producing reproducible markdown, and moving it into agent context trades that away
  for nothing we need.
- **Tool search.** We have three tools.
- **Fast mode.** Relevant to Query latency (SC-003), but a research preview at premium
  pricing on a tier we do not run. Revisit if #117 lands on Opus.
- **Files API, citations, programmatic tool calling, memory tool.** No current use that
  justifies the surface. The memory tool is the most interesting of these — cross-run agent
  memory — but it is a feature with its own ADR, not an upgrade. Note it is unrelated to
  our *memory directory* (ADR-024), which is harness bookkeeping, not agent memory.

---

## 5. Standing caveat: this all assumes we keep the in-process loop

#102 (`claude -p` headless as the agent process) and #105 (a TypeScript runner on the Claude
Agent SDK) both propose replacing the loop this review optimizes. If either lands,
§3.1–§3.6 mostly evaporate: caching, thinking, context editing and budgets become the
CLI's or the SDK's concern, not ours.

That is not a reason to wait. §3.2's corrections are worth making either way, and prompt
caching pays for itself in the meantime — but nothing here should be planned as a
multi-quarter investment while the runtime question is open.

---

## 6. Issue map

| Area | Issue | Kind |
| --- | --- | --- |
| Prompt caching | #115 | spec-candidate |
| Model tier decision (gates thinking, budgets, compaction, system messages) | #117 | spec-candidate |
| Port carries only text/tool_use blocks (prerequisite for thinking + compaction) | #118 | spec-candidate |
| Refusal reported as protocol error | #119 | bug |
| All provider errors terminal | #120 | bug |
| `MaxTokens` hardcoded | #122 | housekeeping |
| Full bodies logged at Information | #123 | housekeeping |
| Context editing | #124 | spec-candidate |
| Task budgets | #125 | spec-candidate |
| "Continue the task." on the untrusted channel | #126 | enhancement |
| Strict tool use | #127 | enhancement |

Pre-existing issues this review touches without duplicating: #107 (token-cap accounting),
#108 (Lint context pressure), #88 (prompt-too-long on ingest), #84 (model selection in the
UI), #53 (usage tracking), #102 / #103 / #105 (alternative runtimes).

---

## 7. Verification status

The API surface was read from the live documentation on 2026-08-18. The Grimoire side was
read from source at `1bdfc20`; nothing in §3.2 was reproduced at runtime, so each of those
issues is filed as "not yet verified — code reading alone".

The exact C# SDK member names for the features in §3 (`strict`, task-budget and
context-management types) could **not** be checked against the installed `Anthropic`
package: no .NET SDK is available in the environment this review ran in and `dotnet
restore` fails. Whoever implements one of these should confirm the binding against the
package before relying on the shape named here.
