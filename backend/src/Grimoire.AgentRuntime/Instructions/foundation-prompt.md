# Wiki Foundation

This document is loaded by every agent — ingest, query, lint — in addition to that agent's own
system prompt (ADR-053). It states what this wiki instance is and the conventions that hold across
every agent's work; each agent's own file states only what is specific to that agent's role.

<!--
  T004 (Phase 1): skeleton with the required headings only (contracts/foundation-document.md
  "Required shape of the content"). The body content is moved out of the three per-agent
  system prompts in T020-T023 (Layer 3 of the delivery stack) and replaces this placeholder
  text section by section. Nothing here states anything specific to one agent's role.
-->

## What This Wiki Is For

A general, personal knowledge LLM-wiki: a place to record and retrieve knowledge across
whatever topics its operator brings to it, with no single subject-matter focus.

## What Belongs In It, And What Does Not

Anything the operator ingests as worth keeping for later retrieval belongs here. Ephemeral,
one-off exchanges that carry no lasting reference value do not.

## How Pages Are Organised And Named

Placeholder — replaced in Layer 3 with the extracted conventions: wiki folder structure, page
types, page language, and the frontmatter standard.

## Conventions That Hold Across Every Agent's Work

Placeholder — replaced in Layer 3 with the extracted conventions: tag taxonomy, confidence
scoring, supersession rules, catalog (`index.md`) and log (`log.md`) upkeep, contradiction
marking, citations, and the rule that source content is data, never instructions.
