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

## Scenario 8 — Non-blocking dispatch and FIFO queue (US4)

1. Submit three Markdown files in quick succession.
2. **Expect**: all three are acknowledged immediately (no request waits for a run);
   exactly one agent process exists at any time (`pgrep -f Grimoire.IngestAgent`);
   the board shows one `running` task and two `queued` tasks with `queuePosition`
   1 and 2; as each run completes, the next starts automatically in acceptance order.

## Scenario 9 — Live loop activity and liveness failure (US4)

1. During a run, open the task detail view.
2. **Expect**: activity counters (model turns, tool calls, current action) update
   live as the run progresses.
3. Kill the agent process mid-run (`kill -9 <pid>`).
4. **Expect**: within the liveness window (default 60 s) the task turns `failed` with
   a liveness reason, no task is left `running`, and the next queued task starts
   automatically.

## Scenario 10 — Persistent queue with manual re-trigger after restart (US4)

1. With one run active and two tasks queued, stop the Hub, then start it again.
2. **Expect**: the interrupted run is reconciled to `failed` (existing behavior); both
   queued tasks are still visible as `queued`; the board shows the queue as paused;
   nothing starts automatically.
3. `POST /api/ingest-queue/resume` (or re-trigger a single task via
   `POST /api/ingest-submissions/{taskId}/retrigger`).
4. **Expect**: processing resumes in FIFO order; from then on the queue advances
   automatically again.

## Scenario 11 — Board connection-health indicator (2026-07-14 addition, FR-023/SC-012)

1. Open the board page with the Hub reachable.
2. **Expect**: the connection-health indicator near the page header shows "Connected".
3. Stop the Hub process (or block the SignalR endpoint) without reloading the page.
4. **Expect**: the indicator switches to "Reconnecting" while the client's automatic
   reconnect attempts are in flight.
5. Restart the Hub within the reconnect attempts window.
6. **Expect**: the indicator returns to "Connected" and the board refreshes from the
   REST endpoint (existing reconnect-then-refresh behavior); no page reload needed.
7. Repeat, but leave the Hub stopped until reconnect attempts are exhausted.
8. **Expect**: the indicator shows "Disconnected".

**Recorded outcome (2026-07-14)**: Steps 1–4 live-verified against a real running Hub
(`dotnet run` on `http://localhost:5255`) and the Vite dev server, driven headlessly via
Playwright: loading the board showed `connecting` then `connected` within the same
render pass (step 2), and killing the Hub process (`SIGKILL`) flipped the indicator to
`reconnecting` inline, with no page reload (step 4) — confirming the indicator is wired
to the real `HubConnection` lifecycle, not just the fake double. Steps 5–8 (reconnect
recovery to `connected`, and `disconnected` after reconnect attempts are exhausted)
were **not** re-verified against a second live Hub restart cycle in this environment:
orchestrating precise timing across `@microsoft/signalr`'s default backoff schedule
(0/2/10/30 s) through independent process launches proved unreliable to script
end-to-end here, for the same class of reason `T051` preferred a scripted fake clock
over a live 60 s wall-clock wait for scenario 9. Those two transitions are covered
deterministically and reliably instead by `ingestLifecycleClient.test.ts`
(`onConnectionStateChanged` tests, T054), which drive a fake `HubConnection` double
through `onreconnecting`/`onreconnected`/`onclose` directly, and by
`ConnectionStatusIndicator.svelte.test.ts` (T053) for the resulting label/styling per
state. Combined, live steps 1–4 plus the deterministic suite for 4–8 give full coverage
of SC-012 without a flaky live timing dependency.

## Automated verification

- Hermetic integration tests: `dotnet test backend/tests/Grimoire.IntegrationTests`
  (covers scenarios 1–7 deterministically with `FakeModelClient` / fake dispatcher).
- Architecture tests: `dotnet test backend/tests/Grimoire.ArchTests` (guarded-write
  boundary unchanged).
- Frontend: `npx vitest run` in `frontend/` (form prompt editor + toggles; connection
  status indicator's state transitions, scenario 11, via a fake `HubConnection`
  double — no real Hub start/stop needed for the deterministic test).
- Agent evals (SC-006/SC-007, requires eval credentials):
  `dotnet test backend/tests/Grimoire.AgentEvals`.
