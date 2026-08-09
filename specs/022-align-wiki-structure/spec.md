# Feature Specification: Wiki Structure Truth — Retire `pages/` From Agent Instructions and Report Real Wiki State

**Feature Branch**: `022-align-wiki-structure`

**Created**: 2026-08-09

**Status**: Draft

**Input**: User description: "the llm-wiki has no pages folder. it should consist of an index.md, a log.md and folders for categories. the different tasks is an addition by this harness, something I liked to be able to see what happend or to interact with the agents. remove or ammend all messages telling about 'pages', and persist the new and more open structure of the wiki in code, docs and comments. the main issue is, that the current query agent implementation doesn't know of the current prod llm-wiki inside /Volumes/Daten/paranoid/llm-wiki and says: 'I'll check the current state of the wiki by reading the index and seeing what's available. The wiki is currently empty — there is no index.md, no pages/ directory, and no log.md file. The repository exists but has no content yet. This is a fresh start; the wiki is ready for initial ingestion.' which is wrong, the wiki is functional and contains data"

## Problem Context

Feature 014 retired the `pages/` wrapper folder: articles now live directly under topical
category folders at the wiki content root, alongside `index.md` and `log.md`. That change
was carried through the storage layout, the safety policies, and the backend source tree —
and it was locked in by a structural rule that scans backend source files for the retired
concept.

That rule never covered the artifacts where the wiki's actual behaviour lives. The agent
instruction files — the versioned system prompts that constitute the agents' judgment —
still instruct every agent to navigate a `pages/` folder that no longer exists:

- The query agent is told the only readable locations are `pages/`, `index.md`, and
  `log.md`, that listing the content root itself is forbidden, and to use `list_files("pages/")`
  to enumerate available articles.
- The ingest agent is told that `pages/` is the bundle root, that every article lives in a
  topic folder *under* `pages/`, and that each article type maps to `pages/<category>/<slug>.md`.
- The lint agent is told to enumerate the wiki by listing `pages/` and its subfolders, and
  to report finding targets as `pages/<slug>.md`.

The visible consequence is an agent that reports a false state of the wiki. Asked to check
the wiki, the query agent explores a folder that cannot exist, finds nothing, and concludes
the wiki is empty and "ready for initial ingestion" — describing the absence of a retired
folder as if it were a deficiency. Any wiki content that does exist is invisible to it,
because the one navigation instruction it was given points nowhere.

A second, related confusion sits in the same content root. The harness writes its own
bookkeeping folders there — `tasks/`, `conversations/`, `findings/`, `remediation-tasks/`.
These are the harness's observability and interaction surfaces: they exist so an operator
can see what the agents did and talk to them. They are not wiki categories and not wiki
content, but nothing in the instructions, the docs, or the layout says so — so an agent
enumerating the content root has no way to tell an article category from harness
bookkeeping.

## Clarifications

### Session 2026-08-09

- Q: May the query agent read the reserved harness surfaces (`tasks/`, `findings/`, `conversations/`) at all? → A: It is the operator's decision, not a fixed rule. The harness offers a setting controlling whether agents may read harness surfaces or only wiki content; it defaults to **not** reading them. An operator who reads all of that data themselves may reasonably decide their agents should too, so the harness must permit that choice rather than forbid it.
- Q: At what granularity does the operator make that choice — one switch for all harness surfaces, per surface, or per surface *and* per agent? → A: Per surface. Each of `tasks/`, `findings/`, `conversations/`, and `remediation-tasks/` is granted independently, all denied by default. The grant applies uniformly to every agent; per-agent differentiation is not introduced.
- Q: Once a harness surface is granted, what may the agent do with it? → A: Read it for context and answer questions about what happened, but never cite a harness record as a wiki source and never derive a new wiki article from one. Granting read access widens what an agent knows, not what counts as wiki knowledge.
- Q: Who creates `index.md` / `log.md` when a content root does not have them yet? → A: An agent creates the file on its first write to it. Both files are already inside every agent's write scope, so no separate harness bootstrap step is introduced and a fresh content root becomes usable on the first ingest.
- Q: Does "remove all messages telling about pages" mean the `pages/` folder concept, or the word "page" as such? → A: The word as such, everywhere — including metric names, task-artifact field names, and persisted record field names, not only prose and instructions. Nothing needs migrating: the project is pre-1.0, with no deployment whose persisted artifacts or telemetry series must stay readable under the old names.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The query agent reports what is actually in the wiki (Priority: P1)

As an operator asking the query agent about the wiki, I want it to explore the real content
root and describe what is genuinely there, so that I get an accurate picture of my wiki
instead of a confident report about a folder layout that was retired.

**Why this priority**: This is the failure the operator actually hit. An agent that reports
a populated wiki as empty is worse than one that refuses to answer — it invites the operator
to re-ingest content that already exists. Every other story in this feature is groundwork
for this one.

**Independent Test**: Can be fully tested by pointing the query agent at a content root that
contains category folders and articles and asking it what the wiki covers, then confirming
the answer names real categories and real articles and does not claim the wiki is empty.

**Acceptance Scenarios**:

1. **Given** a wiki content root containing `index.md`, `log.md`, and category folders with
   articles, **When** an operator asks the query agent what the wiki currently covers,
   **Then** the answer describes the real categories and articles found, and does not
   assert that the wiki is empty.
2. **Given** a wiki content root with articles but no `index.md`, **When** an operator asks
   what the wiki covers, **Then** the agent enumerates the content root directly, reports
   the articles it found, and notes the missing catalog as a gap — rather than concluding
   there is no content.
3. **Given** a genuinely empty wiki content root, **When** an operator asks what the wiki
   covers, **Then** the agent reports that no articles exist yet and describes what it did
   find, without referring to a `pages/` folder or framing its absence as the reason.
4. **Given** any wiki content root, **When** the query agent describes the wiki's state,
   **Then** the description contains no reference to a `pages/` directory.

---

### User Story 2 - Every agent instruction describes the real wiki layout (Priority: P1)

As the operator who owns the agents' instructions, I want the ingest, query, and lint system
prompts to describe the wiki exactly as it is on disk — `index.md`, `log.md`, and category
folders at the root — so that agents create, find, and cite articles at paths that exist.

**Why this priority**: This is the root cause of User Story 1 and it equally affects ingest
(articles written to a path under a non-existent wrapper) and lint (a scan that enumerates
nothing). Fixing the query agent alone would leave two of three agents navigating a fiction.

**Independent Test**: Can be fully tested by having the ingest agent create an article from a
source and confirming it lands at `<content-root>/<category>/<slug>.md` with no wrapper
segment, and that the catalog entry it writes links to that same path.

**Acceptance Scenarios**:

1. **Given** the current agent instruction files, **When** each one is read end to end,
   **Then** none of them instructs an agent to read, list, or write a `pages/` path, and each
   describes the content root as `index.md`, `log.md`, and category folders.
2. **Given** an ingest run over a new source, **When** the agent creates an article,
   **Then** the article file path is `<content-root>/<category>/<slug>.md` with no wrapper
   segment between the content root and the category folder.
3. **Given** an ingest run that creates an article, **When** the catalog entry is written to
   `index.md`, **Then** the entry links to the article's real content-root-relative path,
   which resolves to an existing file.
4. **Given** a lint run over a populated wiki, **When** the agent enumerates articles to scan,
   **Then** it discovers every article present under the content root's category folders.

---

### User Story 3 - Harness surfaces are recognisable as harness surfaces (Priority: P2)

As an operator, I want the harness's own folders in the content root — the ones that let me
see what happened and interact with the agents — to be named and documented as harness
surfaces distinct from wiki content, so that neither I nor an agent mistakes task
bookkeeping or conversation transcripts for wiki articles.

**Why this priority**: Independently valuable and independently testable, but the wiki reads
correctly without it as long as the agents navigate the right root. It becomes important the
moment an agent enumerates the content root directly — which User Story 1 makes it do.

**Independent Test**: Can be fully tested by placing both a real category folder and populated
harness folders in a content root, asking the query agent what the wiki covers, and confirming
it presents the category as wiki content and does not present harness records as articles.

**Acceptance Scenarios**:

1. **Given** a content root containing both category folders and the harness's own folders,
   **When** the query agent describes what the wiki covers, **Then** harness records are not
   presented as wiki articles and are not cited as sources for a wiki answer.
2. **Given** the harness's set of reserved top-level folder names, **When** an agent chooses a
   category folder for a new article, **Then** it does not place the article inside a reserved
   harness folder.
3. **Given** the project's documentation, **When** a reader looks for what lives in the wiki
   content root, **Then** one named place lists both the wiki's own parts (`index.md`,
   `log.md`, category folders) and the harness's reserved folders, stating which is which and
   why the harness folders are there.
4. **Given** an installation where the operator has configured nothing, **When** an agent
   attempts to read a reserved harness folder, **Then** the read is denied, the denial is
   recorded with a reason, and the run continues with the wiki content it can reach.
5. **Given** an operator who has granted read access to one harness surface but not the others,
   **When** an agent runs, **Then** it can read the granted surface, reads of the other
   surfaces are still denied and recorded, and the run's record shows which surfaces were
   permitted.
6. **Given** a granted harness surface, **When** the agent answers a question about what
   happened, **Then** it draws on the surface for context without citing a harness record as a
   wiki source and without creating a wiki article derived from one.

---

### User Story 4 - One canonical term, and the retired one cannot silently return (Priority: P2)

As a maintainer, I want "article" to be the single word the project uses for a unit of wiki
content — in instructions, documentation, comments, metric names, artifact fields, and
persisted records alike — and I want the automated rule forbidding the retired concept to cover
the artifacts that actually steer agent behaviour, so that the same drift cannot recur the next
time the layout changes.

**Why this priority**: This is the durability half of the feature. Without it the fix is a
one-time sweep with nothing preventing regression — which is precisely how the current
divergence arose, since the existing rule scans only backend source files and deliberately
tolerates the retired term in field and metric names.

**Independent Test**: Can be fully tested by deliberately reintroducing a `pages/` navigation
instruction into an agent instruction file and confirming the automated check fails, then
removing it and confirming the check passes.

**Acceptance Scenarios**:

1. **Given** the automated structural check, **When** a `pages/` navigation instruction is
   introduced into any agent instruction file, **Then** the check fails and names the
   offending file.
2. **Given** the automated structural check, **When** the offending instruction is removed,
   **Then** the check passes.
3. **Given** the check runs in the standard pull-request pipeline, **When** a change
   reintroduces the retired concept as a live instruction anywhere it is covered, **Then** the
   build fails before merge.
4. **Given** historical decision records that document the retirement itself, **When** the
   check runs, **Then** those records are not reported as violations — a record of a past
   decision is not a live instruction.
5. **Given** the automated structural check, **When** the retired term is introduced as a new
   metric name, artifact field name, or persisted record field name, **Then** the check fails
   and names the offending file.
6. **Given** the renamed observability signals, **When** an identical run is executed before and
   after the rename, **Then** each signal reports the same value under its new name as it did
   under its old one.

---

### Edge Cases

- **Content root with no catalog and no log**: the agent must enumerate the root directly and
  report what it found, rather than treating a missing `index.md` as proof of emptiness — and
  when it next needs to write a catalog or log entry, it creates the missing file as part of
  that write.
- **Content root that genuinely contains no articles**: the agent must say so plainly and
  describe what it did find, without attributing the emptiness to a missing wrapper folder.
- **A legacy content root that still has a literal `pages/` folder**: encountered on disk it is
  an ordinary top-level folder with articles in it, not a special wrapper — the agent reads it
  like any other category rather than failing or ignoring the rest of the root.
- **A category name that collides with a reserved harness folder name**: the reserved names win
  at the top level; an agent must choose a different category folder rather than write an
  article into a harness surface.
- **Harness folders present but empty**: their presence alone must not be reported as wiki
  content, and their emptiness must not be reported as the wiki being empty.
- **Documentation that must keep the word for historical accuracy**: accepted decision records
  describing the retirement remain readable as history and are not rewritten into inaccuracy.
- **An agent reaching for a harness surface it was not granted**: the read is denied with a
  recorded reason and the run continues on the wiki content it can reach, rather than failing.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The wiki content root's structure MUST be defined, in one named place, as
  `index.md` (the catalog), `log.md` (the activity log), and topical category folders holding
  articles — with no wrapper folder between the content root and a category folder.
- **FR-002**: The set of category folders MUST be open-ended: agents create a new category
  folder when no existing one fits, and no fixed list of category names is imposed by the
  harness.
- **FR-003**: Every agent instruction file MUST describe article locations, navigation, and
  citation paths in terms of the structure in FR-001, and MUST NOT instruct any agent to read,
  list, write, or cite a `pages/` path.
- **FR-004**: The query agent MUST be able to enumerate the wiki content root itself to
  discover what exists, rather than depending on a fixed folder name or on `index.md` being
  present.
- **FR-005**: When the query agent reports the state of the wiki, the report MUST be grounded
  in what it actually enumerated and read, and MUST distinguish "no articles found" from
  "expected location missing".
- **FR-006**: The harness's own top-level folders in the content root (`tasks/`,
  `conversations/`, `findings/`, `remediation-tasks/`) MUST be identified as reserved harness
  surfaces — records of what the agents did and the means to interact with them — distinct from
  wiki content.
- **FR-007**: Agents MUST NOT create articles inside a reserved harness folder, and MUST NOT
  present harness records as wiki articles or cite them as sources for a wiki answer.
- **FR-008**: Project documentation, configuration comments, and code comments that describe
  the wiki layout MUST describe the structure in FR-001, and MUST NOT present the retired
  wrapper folder as current.
- **FR-009**: An automated check MUST fail the build when the retired wrapper folder or the
  retired term is reintroduced as a live instruction, a current description of the layout, or a
  new name, in any covered artifact — including agent instruction files, project documentation,
  comments, metric names, artifact field names, and persisted record field names — and MUST
  name the offending file.
- **FR-010**: The automated check in FR-009 MUST NOT flag accepted decision records or feature
  specifications that document the retirement as a past decision.
- **FR-011**: The automated check in FR-009 MUST run in the standard pull-request pipeline.
- **FR-012**: Existing wiki content MUST NOT be moved, rewritten, or deleted by this feature —
  the change is to instructions, documentation, and enforcement, not to stored content.
- **FR-013**: A content root that is missing `index.md` or `log.md` MUST NOT cause an agent to
  fail; the agent reports the gap and proceeds with what it can enumerate. When an agent first
  needs to write to a missing catalog or log, it MUST create the file as part of that write —
  no separate bootstrap step is required of the operator, and a fresh content root becomes
  usable on the first ingest.
- **FR-014**: The harness MUST expose an operator-controlled setting that governs whether agents
  may read the reserved harness surfaces, or only wiki content. The operator owns this decision
  — the harness MUST NOT hard-code either answer. Each reserved surface (`tasks/`, `findings/`,
  `conversations/`, `remediation-tasks/`) MUST be grantable independently of the others; a grant
  applies uniformly to every agent.
- **FR-015**: The setting in FR-014 MUST default to denying agent reads of every reserved
  harness surface, so that an installation that has configured nothing exposes only wiki content
  to its agents.
- **FR-016**: While harness-surface reads are not granted, an agent's attempt to read one MUST
  be denied at the guarded tool boundary and recorded with a reason, and the run MUST continue
  with the actions that remain allowed.
- **FR-017**: The effective harness-surface read scope MUST be observable for a given run — an
  operator can determine from the run's record which surfaces the agent was permitted to read.
- **FR-018**: A granted harness surface MUST remain outside the wiki's knowledge base: an agent
  may read it for context and answer questions about what happened, but MUST NOT cite a harness
  record as a source for a wiki answer and MUST NOT create or update a wiki article whose
  content is derived from one. Granting read access widens what an agent knows, not what counts
  as wiki knowledge.
- **FR-019**: "Article" MUST be the project's canonical term for a unit of wiki content, and
  "page" MUST be retired as project terminology. The canonical term MUST be used consistently
  across agent instruction files, project documentation, code comments, business metric names,
  task-artifact field names, and persisted record field names.
- **FR-020**: The terminology change in FR-019 MUST NOT carry any migration or
  backward-compatibility obligation: previously persisted artifacts and previously emitted
  telemetry series need not remain readable or queryable under the retired names, and no
  dual-name or alias period is required.
- **FR-021**: Renaming under FR-019 MUST NOT change the meaning, cardinality, or trigger
  conditions of any observability signal — a renamed metric counts exactly what it counted
  before, and a renamed artifact field carries exactly the same values.

### Key Entities

- **Wiki Content Root**: the single directory an operator points the harness at; holds the
  wiki's own parts and the harness's reserved surfaces side by side.
- **Catalog** (`index.md`): the wiki's table of contents at the content root, linking each
  article by its content-root-relative path.
- **Activity Log** (`log.md`): the append-only, human-readable record of what each agent did
  to the wiki, at the content root.
- **Category Folder**: a topical folder directly under the content root holding articles; the
  set of categories is open-ended and grows with the wiki.
- **Article**: a single markdown document inside a category folder — the unit of wiki content,
  and the project's canonical term for it. "Page" is retired as project terminology.
- **Harness Surface**: a reserved top-level folder the harness owns (`tasks/`,
  `conversations/`, `findings/`, `remediation-tasks/`) recording what agents did and enabling
  operator interaction; not wiki content.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of agent instruction files describe the content root as catalog, log, and
  category folders, and contain zero live `pages/` navigation, write, or citation instructions.
- **SC-002**: 100% of articles created by an ingest run land at `<content-root>/<category>/<slug>.md`
  with no wrapper segment, and 100% of catalog entries written in the same run link to a path
  that resolves to an existing file.
- **SC-003**: 100% of reintroductions of the retired wrapper or the retired term — as a live
  instruction, a current layout description, or a new name — in any covered artifact are
  detected by the automated check and fail the build, with the offending file named.
- **SC-004**: 100% of accepted decision records documenting the retirement pass the automated
  check without modification.
- **SC-005**: The wiki content root's composition — its own parts and the reserved harness
  surfaces — is documented in exactly one place, referenced from every other place that needs it.
- **SC-006**: In sampled query runs against a content root containing articles, ≥ 95% name at
  least one real category and at least one real article present on disk, and ≤ 2% assert the
  wiki is empty or has no content.
- **SC-007**: In sampled query runs against a genuinely empty content root, ≥ 90% report the
  absence of articles without referring to a `pages/` folder.
- **SC-008**: In sampled query runs against a content root holding both articles and populated
  harness surfaces, ≥ 95% present no harness record as a wiki article and cite none as a source.
- **SC-009**: In sampled ingest runs, ≥ 95% place a new article in a category folder that is
  not a reserved harness folder.
- **SC-010**: On an installation that has configured nothing, 100% of agent attempts to read a
  reserved harness surface are denied and recorded with a reason, and the run continues.
- **SC-011**: 100% of runs record which harness surfaces the agent was permitted to read, so an
  operator can reconstruct the effective read scope after the fact.
- **SC-012**: In sampled runs where a harness surface has been granted, ≥ 95% answer the
  operator's question without citing a harness record as a wiki source, and ≤ 2% create a wiki
  article whose content is derived from a harness record.
- **SC-013**: 100% of ingest runs against a content root that has neither `index.md` nor
  `log.md` leave both files present and populated afterwards, without any operator setup step.
- **SC-014**: 100% of business metric names, task-artifact field names, and persisted record
  field names that denote a unit of wiki content use the canonical term, with zero remaining
  uses of the retired term.
- **SC-015**: For every renamed observability signal, the value it reports for an identical run
  is identical before and after the rename — the rename changes the name and nothing else.

## Assumptions

- The wiki content root keeps its current location and the harness keeps writing its own
  folders there. The user describes the harness surfaces as something they want ("something I
  liked to be able to see what happened or to interact with the agents"), so they stay — this
  feature makes them recognisable rather than relocating them.
- The reserved harness folder names are exactly the four the harness writes today: `tasks`,
  `conversations`, `findings`, `remediation-tasks`. Changing that set is out of scope.
- No migration of any kind is required — not of content, not of persisted artifacts, and not of
  telemetry. The project is pre-1.0, with no deployment whose stored records or metric series
  must stay readable under the retired names, so the terminology rename is a clean break with no
  alias or dual-write period.
- No content migration is required. Verified on 2026-08-09: the operator's content root at
  `/Volumes/Daten/paranoid/llm-wiki` contains the four harness folders and one conversation
  transcript, and no `index.md`, `log.md`, or category folders — so there are no articles to
  move, and no `pages/` folder exists on disk to remove. See the note below.
- The safety policies already grant content-root-wide read and write scope, so no policy
  widening is needed for agents to enumerate the root; only the instructions telling them not
  to are wrong.
- Historical accuracy in accepted decision records outweighs terminological uniformity: records
  of the retirement keep the word, marked as retired, rather than being rewritten.
- Wiki content itself remains in the operator's configured content language; this feature
  changes the harness's own instructions and documentation, which stay English per the project
  language policy.

### Note on the reported production state

The user's report states the wiki at `/Volumes/Daten/paranoid/llm-wiki` "is functional and
contains data" and that the query agent's "empty wiki" answer is wrong. Inspection on
2026-08-09 found that content root to hold only `tasks/`, `conversations/`, `findings/`, and
`remediation-tasks/`, with a single conversation transcript and no articles, catalog, or log.

The agent's answer is still defective, and this feature still addresses it: the agent reached
its conclusion by exploring a folder that cannot exist, never enumerated the actual content
root, and framed the absence of the retired wrapper as the finding. Against a content root
that does hold articles, the same instructions would report it as empty. The requirements above
cover both the populated and the genuinely-empty case, so they hold either way — but if the
operator expected articles to be present at that path, that is a separate question about where
their content went, not something this feature resolves.
