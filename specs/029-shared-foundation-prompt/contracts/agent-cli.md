# Contract: Agent CLI instruction paths

**Feature**: 029-shared-foundation-prompt | ADR-053, ADR-036

Each agent worker takes one explicit path per instruction document. The harness composes paths; the
agent discovers nothing.

## Options

| Option | Required | Meaning |
|---|---|---|
| `--foundation-prompt-path <path>` | **yes**, all three agents | The effective foundation document for this run |
| `--system-prompt-path <path>` | yes (existing) | This agent's role document |
| `--default-user-prompt-path <path>` | only where the agent profile declares it (existing) | Per-run steering default (ADR-054) |
| `--policy-path <path>` | yes (existing) | Guardrail policy (ADR-006) |

`--foundation-prompt-path` is **required**, not optional: an absent value is a usage error, not a
silent fall-back to a single-document prompt. Fail-closed applies to the option as well as to the file.

## Load and composition

1. Load the foundation document. Missing, unreadable or effectively empty ⇒ fail before any wiki
   write, with a reason naming the foundation document specifically.
2. Load the role document. Same rules, same distinct naming.
3. Compose: `foundation + "\n\n" + role`. No other text is added.
4. Record both documents, foundation first, each with its own content hash.

Both loads happen in `AgentHost` before the `started` event, so a failure surfaces as a `failed` run
event with the naming reason, exactly as an instruction-load failure does today.

## Callers

| Caller | Passes |
|---|---|
| `AgentProcessHost` (Hub dispatch) | the effective document resolved by `GrimoirePathResolver` |
| `Grimoire.EvalRunner` invokers | the repository-source default (an eval run has no data root) |

## Compatibility

An agent worker built from this feature refuses to run without the new option; a Hub built from this
feature always passes it. Mixed versions are not a supported combination — the agent runtime and the
Hub are delivered together by the same build (ADR-043).
