# Contract: Recording Store Format

**Feature**: 009-agent-eval-replay

Root: `data/evals/recordings/` (versioned in git). One directory per scenario.

## `<scenario>/manifest.json`

```json
{
  "schema_version": 1,
  "scenario_id": "update-over-duplicate",
  "captured_at": "2026-07-23T18:00:00Z",
  "model": "nvidia/llama-3.3-nemotron-super-49b-v1",
  "provider_kind": "affordable",
  "fingerprints": {
    "system_prompt": "sha256:…",
    "default_user_prompt": "sha256:…",
    "policy": "sha256:…",
    "fixture": "sha256:…",
    "scenario_definition": "sha256:…",
    "judge_prompt": "sha256:…"
  },
  "samples": [
    { "file": "sample-01.json", "sha256": "…", "task_id": "eval-…" }
  ]
}
```

Rules:
- `fingerprints.judge_prompt` present only for judge-scored scenarios.
- `fixture` is a canonical hash over the fixture tree (sorted relative path + content
  hash per file). `scenario_definition` hashes the definition's stable serialization.
- `samples[].sha256` is the content hash of the sample file — a mismatch at replay time
  means tamper/hand-edit → trust status `mismatch` (never replayed as trusted).
- Wholesale replacement: a capture run rewrites the whole scenario directory atomically
  (write to temp dir, swap); no mixed capture generations.

## `<scenario>/sample-NN.json`

```json
{
  "schema_version": 1,
  "sample": 1,
  "task_id": "capture-…",
  "model": "nvidia/llama-3.3-nemotron-super-49b-v1",
  "turns": [
    {
      "turn": 1,
      "system_prompt_sha256": "…",
      "conversation": [ { "role": "user", "content_sha256": "…" } ],
      "tool_names": ["write_page", "…"],
      "stop_reason": "tool_use",
      "tool_uses": [ { "tool_use_id": "…", "tool_name": "…", "input_json": "…" } ],
      "assistant_text": null,
      "input_tokens": 1234,
      "output_tokens": 256
    }
  ],
  "judge_verdicts": [
    { "judge_prompt_sha256": "…", "verdict": "adopted", "rationale": "…" }
  ],
  "outcome": { "status": "completed", "checks": { "…": true } }
}
```

Rules:
- `model` names the model that produced the sample (`IModelClient.ModelId` at capture);
  the replay adapter presents it as its own model id so the task artifact stays honest.
- `task_id` is baked into the captured conversation's first message — replay reuses it
  verbatim so the replayed conversation matches byte-for-byte. The source ref is the
  stable value `eval://<scenario>/sample-NN` for the same reason.
- `turns` is the full ordered interaction; `tool_uses`, `assistant_text`, `stop_reason`
  and token counts are stored verbatim (they are what replay serves back);
  conversation *requests* are stored as hashes only (match-checking, R2) — recorded
  responses are the payload, recorded requests are fingerprints.
- `judge_verdicts` only for judge-scored scenarios; replay consumes them verbatim.
- No credential material anywhere (scanned at write; FR-011).
- Schema evolution: `schema_version` bump ⇒ old recordings read as `mismatch` with an
  actionable re-capture message (never silently reinterpreted).

## Replay env contract (composition root, ADR-011)

| Variable | Effect in `Grimoire.IngestAgent` `Program.cs` |
|----------|-----------------------------------------------|
| `GRIMOIRE_MODEL_REPLAY_PATH=<sample file>` | Construct `ReplayModelClient` on the sample file instead of `AnthropicModelClient`; no credential read |
| `GRIMOIRE_MODEL_CAPTURE_PATH=<sample file>` | Wrap the live adapter in `TurnCaptureModelClient`, writing the recording turn stream |
| both set | Configuration error → agent exits non-zero with named conflict (no silent pick) |
| neither | Production behavior, unchanged |
