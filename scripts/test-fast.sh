#!/usr/bin/env bash
# Fast developer feedback tier (spec 019-fast-test-tier, FR-001/FR-002, SC-001/SC-002).
#
# Runs the deterministic, zero-evaluation-prerequisite subset of the backend test suite:
# Grimoire.Domain.UnitTests, Grimoire.ArchTests, and the Tier=Fast-filtered hermetic
# harness-mechanics subset of Grimoire.AgentEvals. Never executes a replay-eval scenario —
# no recordings, no provider credential, no network access required
# (contracts/test-tier-commands.md "Fast tier").
#
# Usage: ./scripts/test-fast.sh
# Stops at the first failing suite so the developer immediately sees which tier failed.
set -eo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."

echo "==> Grimoire.Domain.UnitTests"
dotnet test backend/tests/Grimoire.Domain.UnitTests --configuration Release

echo "==> Grimoire.ArchTests"
dotnet test backend/tests/Grimoire.ArchTests --configuration Release

echo "==> Grimoire.AgentEvals (Tier=Fast — hermetic harness-mechanics tests only)"
dotnet test backend/tests/Grimoire.AgentEvals --configuration Release --filter "Tier=Fast"
