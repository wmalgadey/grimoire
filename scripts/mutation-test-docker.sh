#!/usr/bin/env bash
# Runs scripts/mutation-test.sh inside a container — for hosts with no local .NET SDK or
# Bun, such as the deployment server. Same arguments, same output, same exit codes:
#
#   ./scripts/mutation-test-docker.sh --group all
#   ./scripts/mutation-test-docker.sh --only hub
#   ./scripts/mutation-test-docker.sh --no-cache --group all   # rebuild the image from scratch
#
# The image (scripts/mutation-test.Dockerfile) carries the .NET SDK, markitdown, Bun and a
# Playwright Chromium, because the run needs the real versions of all four (Constitution
# Principle II: real infrastructure, no doubles). The first build takes a few minutes and
# about 3 GB.
#
# `docker build` runs on every invocation rather than only when the image is missing. That
# is not wasted work: with the layer cache warm and nothing changed it finishes in about a
# second, and when frontend/bun.lock moves it rebuilds exactly the layer that pins the
# Playwright browser to the lockfile's version. An "is the image there?" check would
# instead keep serving an image built for the dependency set of an older checkout.
# --no-cache forces the whole thing from scratch.
#
# Two mounts matter:
#   $PWD -> /repo          the checkout itself, so reports land in docs/reports/mutation/
#                          on the host and the run is resumable across invocations.
#   cache dirs over        frontend/node_modules and frontend/.svelte-kit. The host's
#                          node_modules holds binaries for the host's platform; a Linux
#                          container cannot use a macOS install and must not overwrite it,
#                          so it gets its own copy under the cache directory — created on
#                          the host so it belongs to you rather than to root, and kept
#                          between runs so `bun install` happens once.
#
# The container runs as the invoking user, so every generated file belongs to you and not
# to root.
#
# Environment:
#   MUTATION_IMAGE                 image tag to build/use (default: grimoire-mutation)
#   MUTATION_CACHE                 host directory for the NuGet/dotnet cache
#   MUTATION_CONCURRENCY           forwarded — parallel test runners for the .NET lane
#   MUTATION_FRONTEND_CONCURRENCY  forwarded — same for the frontend lane
set -eo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

if ! command -v docker >/dev/null 2>&1; then
  echo "docker not found — install it, or run ./scripts/mutation-test.sh directly on a host with the .NET SDK and Bun." >&2
  exit 2
fi

image="${MUTATION_IMAGE:-grimoire-mutation}"

build_args=()
args=()
for arg in "$@"; do
  if [ "$arg" = "--no-cache" ]; then build_args+=(--no-cache); else args+=("$arg"); fi
done

echo "==> building $image (cached unless the repository's dependency pins moved)"
docker build "${build_args[@]}" -f scripts/mutation-test.Dockerfile -t "$image" .

# Kept outside the repository so a run leaves no untracked files behind, and reused across
# runs so the package restore happens once. Same idea as scripts/probe-models.sh.
cache="${MUTATION_CACHE:-${XDG_CACHE_HOME:-$HOME/.cache}/grimoire-mutation}"
mkdir -p "$cache" "$cache/frontend-node_modules" "$cache/frontend-svelte-kit"

# A server invocation over ssh or from a scheduler has no TTY, and `docker run -it` fails
# outright there rather than degrading.
tty_flags=()
[ -t 0 ] && [ -t 1 ] && tty_flags=(-it)

exec docker run --rm "${tty_flags[@]}" \
  --user "$(id -u):$(id -g)" \
  -e HOME=/cache \
  -e MUTATION_CONCURRENCY \
  -e MUTATION_FRONTEND_CONCURRENCY \
  -v "$PWD:/repo" \
  -v "$cache:/cache" \
  -v "$cache/frontend-node_modules:/repo/frontend/node_modules" \
  -v "$cache/frontend-svelte-kit:/repo/frontend/.svelte-kit" \
  -w /repo \
  "$image" \
  "${args[@]}"
