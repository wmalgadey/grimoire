#!/usr/bin/env bash
# Runs scripts/probe-models.cs in a .NET 10 SDK container — for hosts with no local .NET
# SDK, such as the deployment server.
#
# Usage: ./scripts/probe-models.sh [model-id ...]
#   ./scripts/probe-models.sh                    # catalog + built-in candidates
#   ./scripts/probe-models.sh claude-opus-5      # only these model ids
#
# Reads the repository .env (if present) for ANTHROPIC_AUTH_TOKEN and the GRIMOIRE_*_MODEL
# values, then forwards them into the container by name. Two reasons not to use
# --env-file .env here: docker does not strip the quotes around the model values that .env
# writes, and forwarding by name keeps the token off the command line.
#
# Exit codes are the probe's own: 0 = at least one model available, 1 = none, 2 = no
# credential configured.
#
# Environment:
#   GRIMOIRE_DOTNET_IMAGE   override the SDK image (default mcr.microsoft.com/dotnet/sdk:10.0)
#   GRIMOIRE_PROBE_CACHE    override the NuGet/dotnet cache directory kept outside the repo
set -eo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

if ! command -v docker >/dev/null 2>&1; then
  echo "docker not found — install it, or run 'dotnet run scripts/probe-models.cs' directly." >&2
  exit 2
fi

if [ -f .env ]; then
  set -a
  # shellcheck disable=SC1091
  . ./.env
  set +a
else
  echo "note: no .env in the repository root — relying on the ambient environment." >&2
fi

image="${GRIMOIRE_DOTNET_IMAGE:-mcr.microsoft.com/dotnet/sdk:10.0}"

# Kept out of the repository so the run leaves no untracked files behind, and reused across
# runs so the package restore happens once rather than on every probe.
cache="${GRIMOIRE_PROBE_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/grimoire-probe-models}"
mkdir -p "$cache"

exec docker run --rm \
  --user "$(id -u):$(id -g)" \
  -e HOME=/cache \
  -e DOTNET_NOLOGO=1 \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  -e DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=1 \
  -e ANTHROPIC_AUTH_TOKEN \
  -e ANTHROPIC_API_KEY \
  -e ANTHROPIC_BASE_URL \
  -e GRIMOIRE_INGEST_BASE_URL \
  -e GRIMOIRE_INGEST_MODEL \
  -e GRIMOIRE_QUERY_MODEL \
  -e GRIMOIRE_LINT_MODEL \
  -e GRIMOIRE_PROBE_OAUTH_BETA \
  -v "$PWD:/repo" \
  -v "$cache:/cache" \
  -w /repo \
  "$image" \
  dotnet run scripts/probe-models.cs "$@"
