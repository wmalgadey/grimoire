#!/usr/bin/env bash
set -euo pipefail

# Bun's devcontainer Feature does not pin as precisely as frontend/package.json's
# "packageManager" field requires, so install the exact version manually
# (research.md R4). Downloaded as a pinned release artifact and checksum-verified
# against Bun's own published SHASUMS256.txt for that release, rather than piping
# a remote install script directly into bash (supply-chain risk).
BUN_VERSION="1.3.14"
case "$(uname -m)" in
  x86_64) BUN_ARCH="linux-x64" ;;
  aarch64 | arm64) BUN_ARCH="linux-aarch64" ;;
  *)
    echo "Unsupported architecture for pinned Bun install: $(uname -m)" >&2
    exit 1
    ;;
esac

bun_tmp="$(mktemp -d)"
trap 'rm -rf "$bun_tmp"' EXIT
release_url="https://github.com/oven-sh/bun/releases/download/bun-v${BUN_VERSION}"
curl -fsSL -o "$bun_tmp/bun-${BUN_ARCH}.zip" "${release_url}/bun-${BUN_ARCH}.zip"
curl -fsSL -o "$bun_tmp/SHASUMS256.txt" "${release_url}/SHASUMS256.txt"
(cd "$bun_tmp" && grep " bun-${BUN_ARCH}.zip\$" SHASUMS256.txt | sha256sum -c -)
unzip -q "$bun_tmp/bun-${BUN_ARCH}.zip" -d "$bun_tmp"
mkdir -p "$HOME/.bun/bin"
mv "$bun_tmp/bun-${BUN_ARCH}/bun" "$HOME/.bun/bin/bun"
chmod +x "$HOME/.bun/bin/bun"
ln -sf "$HOME/.bun/bin/bun" "$HOME/.bun/bin/bunx"

grep -qxF 'export PATH="$HOME/.bun/bin:$PATH"' "$HOME/.bashrc" 2>/dev/null \
  || echo 'export PATH="$HOME/.bun/bin:$PATH"' >> "$HOME/.bashrc"
export PATH="$HOME/.bun/bin:$PATH"

dotnet restore backend/Grimoire.slnx

cd frontend
bun install --frozen-lockfile

# CI installs this separately (bunx playwright install --with-deps chromium) so
# `bun run test`'s @vitest/browser-playwright suite can launch headless Chromium;
# match that here so the devcontainer needs no separate host-level step (SC-002).
bunx playwright install --with-deps chromium
