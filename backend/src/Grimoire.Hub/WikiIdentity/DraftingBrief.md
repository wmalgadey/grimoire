# Foundation Document Drafting Brief

## The operator's description (verbatim)

{{DESCRIPTION}}

## Required shape

Draft one markdown document (no frontmatter), written in the second person and
addressed to the agents that will read it — for example, "You are maintaining a
wiki that…". State:
- What this wiki is for
- What belongs in it and what does not
- How pages are organised and named
- The conventions that hold across every agent's work (folder structure, page types,
  page language, frontmatter standard, tag taxonomy, confidence scoring, `index.md`/
  `log.md` entry conventions, and how source content is treated)

It states nothing about any one agent's role, write scope, or steps — those stay in
each agent's own instruction file. ("Query should combine wiki history with external
research" is role behaviour, not a wiki-wide convention, even for a wiki whose stated
purpose implies it.)

Read backend/src/Grimoire.AgentRuntime/Instructions/foundation-prompt.md first if the
required shape is unclear — it is a worked example of exactly this document, not just
a description of one.

## Handing the result back

Save the drafted document to a file — its path and name make no difference, only the
bytes are read — and read it back once before handing it over: it is persisted
verbatim, with nothing downstream to template, reformat or correct a mistake in it.
Then run:

    wiki-identity set --from-file <path-to-drafted-document>

Add --replace if an instance document is already in place and should be overwritten.
