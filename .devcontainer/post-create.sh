#!/usr/bin/env bash
set -euo pipefail

# Bun's devcontainer Feature does not pin as precisely as frontend/package.json's
# "packageManager" field requires, so install the exact version via the official
# install script instead (research.md R4).
BUN_VERSION="1.3.14"
curl -fsSL https://bun.sh/install | bash -s "bun-v${BUN_VERSION}"
echo 'export PATH="$HOME/.bun/bin:$PATH"' >> "$HOME/.bashrc"
export PATH="$HOME/.bun/bin:$PATH"

dotnet restore backend/Grimoire.slnx

cd frontend
bun install

# CI installs this separately (bunx playwright install --with-deps chromium) so
# `bun run test`'s @vitest/browser-playwright suite can launch headless Chromium;
# match that here so the devcontainer needs no separate host-level step (SC-002).
bunx playwright install --with-deps chromium
