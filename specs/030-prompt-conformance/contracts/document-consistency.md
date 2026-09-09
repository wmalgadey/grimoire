# Contract — Cross-Document Consistency for Feature 030

This feature exposes no API, no CLI surface and no wire format. Its only "interface" is the
agreement between four instruction documents that agents read. This contract states, per statement,
which document is authoritative and what each other document may say about it — so a reviewer can
check the four files against one table instead of reading them in parallel.

**Verification is human review plus the final-phase completeness audit.** No deterministic test may
assert any row below: doing so would string-match instruction-file content, which Constitution
Principle V forbids and which spec.md rules out twice (Requirements preamble, Out of Scope).

## Authority table

`FOUND` = `Grimoire.AgentRuntime/Instructions/foundation-prompt.md`
`INGEST` / `LINT` / `QUERY` = the respective `Instructions/system-prompt.md`.

| Statement | Authoritative in | INGEST may | LINT may | QUERY may |
|-----------|------------------|-----------|----------|-----------|
| Frontmatter field list, including both lifecycle fields | `FOUND` | refer | refer | refer |
| Canonical definition of the inbound-link count | `FOUND` | refer | refer + state its counting *procedure* | refer |
| Which run writes each lifecycle field | `FOUND` | refer | refer | refer |
| What act qualifies as a review (D5) | `LINT` | — | **state** | — |
| Review-candidate listing does not qualify (FR-002d) | `LINT` | — | **state** | — |
| A page with no finding is not stamped (FR-002e) | `LINT` | — | **state** | — |
| Shared confidence signal set | `FOUND` | refer | refer | refer |
| Confidence thresholds | `FOUND` | refer | refer + state that they are unchanged by its extension | refer |
| Lint's link-graph extension and its scale | `LINT` | — | **state** | — |
| That a score can legitimately change when lint re-scores (FR-006) | both `FOUND` and `LINT` | — | **state** | — |
| Synthesis-page confidence treatment (FR-006a) | `QUERY` | — | — | **state** |
| Source-summary page is required per ingest run | `INGEST` | **state** | refer | — |
| Source-summary page type, path and `resource` rule | `FOUND` | refer | refer | refer |
| Integration-depth expectation and its reason | `FOUND` | **state the same value** | — | — |
| Degradation when `FOUND` omits a definition (FR-010) | `LINT` | — | **state** | — |

"refer" means the document may point at the shared statement but must not restate it in a form that
can drift. Restating a rule in two places is how #109–#111 happened.

## Invariants a reviewer checks

1. **No value appears twice with different content.** The integration-depth number is the one
   deliberate duplication (FR-009 requires both documents to state it); it must match exactly.
2. **Every deliberate per-role deviation is stated in both documents that carry it** (FR-011) — the
   deviating role document *and* the foundation document that the deviation departs from.
3. **No document grounds a requirement in `docs/foundational/llm-wiki-*`** (FR-007, FR-009).
   Those are source material and are never cited as requirements (`CLAUDE.md`).
4. **No document acquired a rule that the harness would have to enforce.** FR-010a: the harness
   never inspects instruction content.

## Known pre-existing inconsistency, out of scope

`FOUND` requires `index.md` entries to use "a markdown link — not a wikilink", while `LINT` counts
`[[wikilink]]` occurrences in `index.md` and calls dropping them "the most common mistake". Under
`FOUND`'s own rule there is nothing there to drop. Recorded in spec.md under *Findings Recorded, Not
Fixed Here*; it is a different pair of statements from the three defects in scope and is an issue
candidate rather than work for this feature. A reviewer will see it while checking row 2 of the
authority table and should not treat it as a regression introduced here.
