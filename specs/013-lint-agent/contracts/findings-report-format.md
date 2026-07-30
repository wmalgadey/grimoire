# Contract: Findings Report File Format (`grimoire-findings/1`)

The on-disk format of a Findings Report. Humans read it top-to-bottom as a report
(US1); nothing re-parses it back into structured findings today (unlike the
Conversation Record, which the Hub re-parses for follow-up context) — but it still
adopts the Conversation Record's sentinel-safety discipline because its body is
agent-authored prose derived from an autonomous whole-wiki read, the same
prompt-injection-adjacent surface. Writer lives in `Grimoire.Hub.LintFindings`
(`FindingsReportStore`/`FindingsReportFormat`).

- **Location**: `<base>/data/findings/<runId>.md` (`ResolvedGrimoirePaths.FindingsReportPathFor`).
- **Encoding**: UTF-8, no BOM (matches `GuardedToolExecutor`'s existing write convention).
- **Lifecycle**: written exactly once, at the run's terminal transition. Never
  appended to, never rewritten — each Lint Run gets its own file; earlier reports
  remain readable (spec edge case: "What happens to previous reports?").

## Layout

```markdown
---
run_id: 2026-07-30-lint-a1b2c3d4
record_format: grimoire-findings/1
triggered_at: 2026-07-30T10:00:00.0000000+00:00
completed_at: 2026-07-30T10:04:12.0000000+00:00
outcome_state: completed
failure_reason: null
partial: false
instruction_file:
  path: "agents/lint/system-prompt.md"
  sha256: "7f2a…"
denied_actions: []
inbound_links_refreshed: 42
-->

# Lint Run 2026-07-30-lint-a1b2c3d4 — completed

## Content Quality

### Contradiction: [[adr-004]] vs [[credential-scoping]]

[[adr-004]] states the API key is injected at spawn time; [[credential-scoping]]'s
"Rotation" section implies it is re-read per request. These describe different
mechanisms for the same credential.

**Proposed remediation**: Reconcile by updating [[credential-scoping]]'s Rotation
section to match ADR-004's spawn-time injection model, or supersede it if the
mechanism has genuinely changed.

*(further findings in this category, or "No content-quality findings." if none)*

## Metadata Hygiene

### Missing tags: [[write-journal]]

...

## Structure

### Orphan page: [[single-composition-point]]

...
```

- **Bookkeeping block**: a single `<!-- grimoire:findings ... -->` HTML comment
  (opens the document, immediately after the frontmatter, mirroring the Conversation
  Record's `<!-- grimoire:turn ... -->` idiom) carrying every run-level fact from
  data-model.md's "Run-level facts" table as YAML. Unlike the Conversation Record,
  this block appears exactly once per file (one run, not many turns).
- **Body**: three fixed `## <Category>` headings, in the spec's stated severity
  order (Content Quality, Metadata Hygiene, Structure), each containing zero or more
  `### <finding title>` sections with free-text description and a
  `**Proposed remediation**:` line. A category with no findings states so explicitly
  (`No <category> findings.`) — never an empty/missing heading, so an honest "healthy
  wiki" report is structurally indistinguishable from an unfinished one only by
  reading actual sentences, never by silence (FR-006 acceptance scenario 4).
- **Sentinel safety**: the bookkeeping comment's string values (`failure_reason`,
  denied-action targets/reasons) are JSON-escaped with `-->` neutralized to
  `-->`, identical to `ConversationRecordFormat`'s existing escaping
  rule — agent-authored finding descriptions/remediations in the body are plain
  Markdown text with no delimited structure of their own to forge (there is no
  further block boundary inside the body a hostile finding description could fake its
  way out of, unlike a turn block's `## Turn N` boundary).
- **Partial reports**: `partial: true` in the bookkeeping block when a run did not
  reach a clean terminal state (e.g. liveness failure mid-analysis); the body still
  contains whatever findings were produced before the failure, clearly headed
  `# Lint Run <id> — failed (partial)`.

## Parsing

No production code parses this format back into structured data today (Findings
Reports are read-only-for-humans; there is no follow-up-context use case the way
Conversation Records have one). A parser is not required by this feature's tasks —
`FindingsReportFormat` exposes only a writer. If a future feature needs to browse or
sample Findings Reports programmatically (e.g. an evaluation scorer), it should reuse
this same sentinel-safe layout and add a parser then, following
`ConversationRecordFormat`'s parser as the precedent to imitate.

**Feature 013 forward-compatibility**: the bookkeeping block is an open YAML mapping;
future optional keys (e.g. a structured findings list, once/if a parser is added) can
be added without restructuring, following the Conversation Record's own
forward-compatibility precedent (ADR-014).
