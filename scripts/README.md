# scripts/

Developer and CI helper scripts. They are not part of the build: nothing under `backend/`
or `frontend/` references them, and the complexity gate scans `backend/src frontend/src`
only.

- [`test-fast.sh`](test-fast.sh) — the Fast test tier (see CONTRIBUTING.md).
- [`ci/`](ci) — helpers the workflows in `.github/workflows/` call.
- [`nim/`](nim) — the LiteLLM/NVIDIA NIM proxy used to run agent evals without an
  Anthropic subscription (`specs/007-eval-tests-nim-endpoint/quickstart.md`).

## `probe-models.cs`

Answers one question: **which models can this credential actually run?**

An OAuth-style token (`sk-ant-oat…`) answers a request for a model it is not entitled to
with `429 rate_limit_error` — the same status a genuine rate limit produces, and the reason
`deploy/README.md` warns that a misconfigured `GRIMOIRE_*_MODEL` does not present as a
configuration error. The two cases are only distinguishable by the response headers: a real
rate limit carries `anthropic-ratelimit-unified-*`, an entitlement gap carries none.

The script sends one 1-token message per model through the Anthropic C# SDK and classifies
each answer on that discriminator, then reports whether the models `GRIMOIRE_INGEST_MODEL`,
`GRIMOIRE_QUERY_MODEL` and `GRIMOIRE_LINT_MODEL` name are among the ones that work. Listing
`/v1/models` alone does not answer this — a model can be listed and still be denied.

Run it from the repository root, with `.env` sourced:

```bash
set -a; . .env; set +a
dotnet run scripts/probe-models.cs
```

It is a .NET 10 file-based app, so it needs no project file. The header comment carries the
container invocation for hosts without a local .NET 10 SDK, the full list of environment
variables it reads, and its exit codes.

Note that every successful probe spends a few tokens from the 5-hour and 7-day windows.
