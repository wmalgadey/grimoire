# Quickstart Validation: Single Agent System Prompt & Configurable Ingest Submission

**Feature**: `specs/004-ingest-agent-systemprompt`

Prerequisites: feature 003 merged (submission UI + pipeline), .NET 10 SDK, Node 20+,
`markitdown` CLI on PATH, Hub running locally (`dotnet run` in
`backend/src/Grimoire.Hub`), frontend dev server (`npm run dev` in `frontend/`).

Contracts referenced: [ingest-submission-api-extension.md](./contracts/ingest-submission-api-extension.md),
[ingest-agent-cli.md](./contracts/ingest-agent-cli.md). Entity shapes:
[data-model.md](./data-model.md).

## Scenario 1 — Single system prompt governs the run (US1)

1. Confirm `agents/ingest/system-prompt.md` exists and
   `agents/ingest/CLAUDE.md` / `agents/ingest/skills/` do not.
2. Add a distinctive marker instruction to `system-prompt.md` (e.g. "Begin your final
   summary with the word GRIMOIRE-MARKER.").
3. Submit a small Markdown file through the UI; wait for `completed`.
4. **Expect**: task artifact frontmatter lists exactly one instruction entry
   (`system-prompt.md` + sha256 matching `sha256sum agents/ingest/system-prompt.md`),
   and the recorded summary starts with the marker word. Revert the marker edit.

## Scenario 2 — Fail-closed on missing system prompt (SC-002)

1. Temporarily rename `agents/ingest/system-prompt.md`.
2. Submit a Markdown file.
3. **Expect**: task reaches `failed` with a human-readable reason naming the missing
   system prompt; no wiki page was created or modified. Restore the file.

## Scenario 3 — Custom user prompt steers the run (US2)

1. `GET /api/ingest-submissions/defaults` → response contains the verbatim content of
   `agents/ingest/default-user-prompt.md`; the submission form shows it prefilled.
2. Submit a source with `userPrompt`: "Only extract the definitions; ignore examples."
3. **Expect**: 202 response has `userPromptSource: "custom"`; task artifact frontmatter
   has `user_prompt_source: custom` and body section `## User Prompt` with the exact
   text; task detail view displays it; run summary reflects the steer.
4. Submit the same source with the prompt field untouched.
5. **Expect**: `userPromptSource: "default"`, artifact records the default text.

## Scenario 4 — Prompt validation (FR-010)

1. Submit with a `userPrompt` longer than 8,000 characters.
2. **Expect**: 400 `user_prompt_too_long`, no task created, board unchanged.

## Scenario 5 — Convert step disabled for a text source (US3)

1. Submit a URL with `convertSteps: { "markitdown": false }`.
2. **Expect**: 202; stored normalized artifact is byte-identical to the fetched
   content (compare checksum with an independent fetch); task artifact frontmatter has
   `convert_steps: { markitdown: disabled }`; run proceeds against the as-received
   content.

## Scenario 6 — Required step cannot be disabled (FR-013)

1. Submit a PDF with `convertSteps: { "markitdown": false }`.
2. **Expect**: 422 `convert_step_required` with a message naming the format, no task
   created.
3. Submit an unknown step name (`{ "foo": false }`) → 400 `unknown_convert_step`.

## Scenario 7 — Defaults unchanged (FR-015 / 003 parity)

1. Submit a PDF with neither `userPrompt` nor `convertSteps` (plain 003-style request).
2. **Expect**: behavior identical to 003 — conversion runs, lifecycle
   `received → converting → queued → running → completed|failed` on the board,
   response additionally carries `userPromptSource: "default"` and
   `convertSteps: { "markitdown": true }`.

## Automated verification

- Hermetic integration tests: `dotnet test backend/tests/Grimoire.IntegrationTests`
  (covers scenarios 1–7 deterministically with `FakeModelClient` / fake dispatcher).
- Architecture tests: `dotnet test backend/tests/Grimoire.ArchTests` (guarded-write
  boundary unchanged).
- Frontend: `npx vitest run` in `frontend/` (form prompt editor + toggles).
- Agent evals (SC-006/SC-007, requires eval credentials):
  `dotnet test backend/tests/Grimoire.AgentEvals`.
