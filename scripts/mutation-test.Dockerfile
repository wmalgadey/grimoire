# Image for running scripts/mutation-test.sh on a host that has only a container runtime —
# the deployment server, for instance (deploy/README.md: "You do not need the .NET SDK,
# Bun, or a ..."). Built and used by scripts/mutation-test-docker.sh.
#
# This is developer tooling, not a runtime image: it is never deployed, never published,
# and nothing in the product depends on it. deploy/Dockerfile remains the only image that
# ships, and it deliberately carries no SDK and no test dependencies — which is exactly
# what a mutation run needs, hence a second image rather than a flag on the first.
#
# Build context is the repository root:
#   docker build -f scripts/mutation-test.Dockerfile -t grimoire-mutation .
FROM mcr.microsoft.com/dotnet/sdk:10.0

# markitdown (CLI) is a real, external conversion dependency several
# Grimoire.IntegrationTests cases exercise directly. Pinned in step with
# .github/workflows/ci.yml, .devcontainer/Dockerfile and deploy/Dockerfile — all four move
# together. Leaving it out does not fail loudly: those tests go red, and a red test under
# Stryker reports as mutants "only covered by failing tests", quietly dropped from the score.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3-pip curl unzip xz-utils ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && pip3 install --break-system-packages "markitdown==0.1.7"

# Bun, installed the way .devcontainer/post-create.sh installs it and for the same stated
# reason: a pinned release artifact verified against Bun's published SHASUMS256.txt, rather
# than piping a remote install script into a shell. The version is the one
# frontend/package.json declares as its packageManager.
ARG BUN_VERSION=1.3.14
ENV BUN_INSTALL=/usr/local/bun
ENV PATH=$BUN_INSTALL/bin:$PATH
# bun-linux-x64 assumes AVX2 and SIGILLs on a CPU without it (older/virtualized hosts,
# e.g. the deploy server's QEMU CPU model) — bun-linux-x64-baseline is Bun's own answer to
# that, published under the same SHASUMS256.txt for every release. Detected at build time
# rather than always taking baseline, because baseline gives up the AVX2 code paths on the
# hosts that do have them — and the build runs on the machine that will run the image (#190).
RUN set -eux; \
    case "$(uname -m)" in \
      x86_64) if grep -qw avx2 /proc/cpuinfo; then arch="linux-x64"; else arch="linux-x64-baseline"; fi ;; \
      aarch64|arm64) arch="linux-aarch64" ;; \
      *) echo "unsupported architecture: $(uname -m)" >&2; exit 1 ;; \
    esac; \
    tmp="$(mktemp -d)"; \
    base="https://github.com/oven-sh/bun/releases/download/bun-v${BUN_VERSION}"; \
    curl -fsSL -o "$tmp/bun-${arch}.zip" "$base/bun-${arch}.zip"; \
    curl -fsSL -o "$tmp/SHASUMS256.txt" "$base/SHASUMS256.txt"; \
    (cd "$tmp" && grep " bun-${arch}.zip\$" SHASUMS256.txt | sha256sum -c -); \
    unzip -q "$tmp/bun-${arch}.zip" -d "$tmp"; \
    mkdir -p "$BUN_INSTALL/bin"; \
    mv "$tmp/bun-${arch}/bun" "$BUN_INSTALL/bin/bun"; \
    chmod 755 "$BUN_INSTALL/bin/bun"; \
    ln -sf "$BUN_INSTALL/bin/bun" "$BUN_INSTALL/bin/bunx"; \
    rm -rf "$tmp"

# #194: `bunx` runs a package's bin script through whatever interpreter its shebang names
# (`#!/usr/bin/env node` for Stryker and Playwright's CLIs alike) when that interpreter
# resolves on PATH — and falls back to Bun's own engine, silently, when it does not. With
# no Node in this image, `bunx stryker run` executed Stryker under Bun, where
# `@stryker-mutator/vitest-runner` throws loading its plugin ("First argument must be an
# Error object") and Stryker degrades that to a warning rather than failing. Installing
# Node — the version frontend/.nvmrc pins, the same one `actions/setup-node@v4` resolves
# in CI — is the whole fix; nothing about how `bunx` is invoked elsewhere needs to change.
# Verified against the published SHASUMS256.txt, same reasoning as the Bun install above.
ARG NODE_VERSION=22.23.2
RUN set -eux; \
    case "$(uname -m)" in \
      x86_64) arch="linux-x64" ;; \
      aarch64|arm64) arch="linux-arm64" ;; \
      *) echo "unsupported architecture: $(uname -m)" >&2; exit 1 ;; \
    esac; \
    tmp="$(mktemp -d)"; \
    base="https://nodejs.org/dist/v${NODE_VERSION}"; \
    curl -fsSL -o "$tmp/node.tar.xz" "$base/node-v${NODE_VERSION}-${arch}.tar.xz"; \
    curl -fsSL -o "$tmp/SHASUMS256.txt" "$base/SHASUMS256.txt"; \
    (cd "$tmp" && grep " node-v${NODE_VERSION}-${arch}.tar.xz\$" SHASUMS256.txt | sha256sum -c -); \
    mkdir -p /usr/local/node; \
    tar -xJf "$tmp/node.tar.xz" -C /usr/local/node --strip-components=1; \
    rm -rf "$tmp"
ENV PATH=/usr/local/node/bin:$PATH

# The frontend's `client` Vitest project drives a real Chromium through Playwright, so the
# browser and its system libraries belong in the image — the same
# `playwright install --with-deps chromium` that CI and the devcontainer run.
#
# The version comes out of bun.lock, not package.json: Playwright pins each release to one
# browser revision, so installing the range's lower bound (^1.60.0 -> 1.60.0) while the
# lockfile resolves 1.61.1 downloads a build the tests will then fail to find. Reading the
# lockfile also makes the layer cache do the right thing — bumping the dependency
# invalidates this step by itself, which is why scripts/mutation-test-docker.sh always
# calls `docker build` rather than only when the image is absent.
#
# PLAYWRIGHT_BROWSERS_PATH puts the browser somewhere every user can read: the container
# runs as the invoking user, not root, and the default location under /root would be
# unreachable. frontend/vite.config.ts already honors this variable.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
COPY frontend/bun.lock /tmp/bun.lock
RUN set -eux; \
    version="$(grep -oE '"playwright@[0-9]+\.[0-9]+\.[0-9]+"' /tmp/bun.lock | head -1 | tr -d '"' | cut -d@ -f2)"; \
    test -n "$version"; \
    echo "installing Playwright browsers for playwright@$version (from bun.lock)"; \
    bunx "playwright@${version}" install --with-deps chromium; \
    chmod -R a+rX /ms-playwright; \
    rm -f /tmp/bun.lock

# dotnet writes its first-run marker, the NuGet cache and the local tool payloads under
# $HOME; the wrapper mounts a writable cache there, so the container needs no home of its
# own and the restore happens once instead of on every run.
ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK=1

WORKDIR /repo
ENTRYPOINT ["./scripts/mutation-test.sh"]
