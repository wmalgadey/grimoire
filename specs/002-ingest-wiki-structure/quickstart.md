# Quickstart: Validate Ingest Wiki Structure

This guide validates feature 002 end-to-end behavior for autonomous wiki structure updates with deterministic auditing.

## Prerequisites

- .NET 10 SDK installed
- Local repository checkout at project root
- Test source document prepared under a temporary path
- No dependency on live Anthropic API for validation tests in this guide

## 1) Build backend components

From repository root:

```bash
cd backend
dotnet build Grimoire.slnx
```

Expected outcome:
- Build succeeds for Hub, IngestAgent, and test projects.

## 2) Run deterministic integration tests for tooling behavior

From repository root:

```bash
cd backend
dotnet test tests/Grimoire.IntegrationTests/Grimoire.IntegrationTests.csproj --filter "FullyQualifiedName~Guardrail|FullyQualifiedName~CredentialScoping|FullyQualifiedName~TaskArtifact|FullyQualifiedName~OperationalState"
```

Expected outcome:
- Tests pass without external API calls.
- No test requires real ANTHROPIC credentials.
- Coverage targets repository-owned wrapper and orchestration behavior.

## 3) Validate architecture boundaries

From repository root:

```bash
cd backend
dotnet test tests/Grimoire.ArchTests/Grimoire.ArchTests.csproj
```

Expected outcome:
- Domain boundary rules pass.
- No forbidden dependency from domain to infrastructure/adapters is introduced.

## 4) Run ingest agent with policy-governed autonomous mode

Use the contract in specs/002-ingest-wiki-structure/contracts/ingest-agent-cli.md and policy format in specs/002-ingest-wiki-structure/contracts/guardrail-policy-file.md.

Example invocation (adapt paths):

```bash
cd backend
dotnet run --project src/Grimoire.IngestAgent/Grimoire.IngestAgent.csproj -- \
  --task-id quickstart-002 \
  --source-ref ../docs/decision-context-overview.md \
  --source-kind file \
  --pages-dir ../wiki/pages \
  --tasks-dir ../wiki/tasks \
  --index-path ../wiki/index.md \
  --log-path ../wiki/log.md \
  --guardrail-policy-path ../wiki/policy/ingest-guardrails.yml \
  --instructions-root ..
```

Expected outcome:
- Run loads CLAUDE.md and declared SKILL context before write planning.
- Only policy-allowed paths are written.
- Denied actions are recorded and do not abort unrelated allowed actions.
- Task artifact includes created/updated/superseded/denied outputs.

## 5) Validate wiki and artifact contract

- Confirm touched wiki pages are connected and discoverable from wiki/index.md.
- Confirm non-source pages contain required frontmatter metadata.
- Confirm supersession fields exist only for explicit supersession cases.
- Confirm artifact format matches specs/002-ingest-wiki-structure/contracts/task-artifact-format.md.

## 6) Negative-path validation: denied write

- Configure a policy that excludes one candidate target path.
- Re-run ingest with same source.

Expected outcome:
- Denied action appears in artifact with path and reason.
- Remaining allowed actions still complete.
- Run status reflects completed unless an independent fatal error occurs.

## Notes

- Validation in this guide intentionally tests project code behavior, not Anthropic SDK correctness.
- Any test that directly asserts live Claude API behavior is out of scope and should be removed or replaced with deterministic wrapper tests.
