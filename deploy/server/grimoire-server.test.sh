#!/bin/bash
# Tests for the parts of deploy/server/grimoire-server that decide something.
#
# Scope, per constitution Principle II ("test what we own"): ref-specification parsing,
# the compose version comparison that gates the `!override` overlay, the deployment state
# file, and the overlay seeding rule. Each of these can only break from a change to this
# script. Nothing here starts docker, talks to a network, or re-verifies that git,
# docker or compose behave as documented.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# shellcheck source=./grimoire-server
GRIMOIRE_SERVER_LIB=1 source "$script_dir/grimoire-server"

failures=0

fail() {
  echo "FAIL: $*" >&2
  failures=$((failures + 1))
}

assert_equals() {
  local actual="$1" expected="$2" what="$3"
  [[ "$actual" == "$expected" ]] || fail "$what: expected [$expected], got [$actual]"
}

# --- resolve_ref_spec: pull request heads are the shape git will not fetch by default.

for spec in "#95" "pr/95" "PR/95" "pr-95" "pull/95" "pull/95/head"; do
  assert_equals "$(resolve_ref_spec "$spec")" "$(printf 'pr\tpull/95/head\tpr/95')" "resolve_ref_spec $spec"
done

# --- resolve_ref_spec: everything else is handed to git as written.

assert_equals "$(resolve_ref_spec main)" "$(printf 'ref\tmain\tmain')" "resolve_ref_spec main"
assert_equals "$(resolve_ref_spec claude/deployment-topology-adr027)" \
  "$(printf 'ref\tclaude/deployment-topology-adr027\tclaude/deployment-topology-adr027')" \
  "resolve_ref_spec branch with a slash"
assert_equals "$(resolve_ref_spec a703846)" "$(printf 'ref\ta703846\ta703846')" "resolve_ref_spec commit"
assert_equals "$(resolve_ref_spec v1.2.3)" "$(printf 'ref\tv1.2.3\tv1.2.3')" "resolve_ref_spec tag"

# A branch that merely starts like a pull request reference is a branch, not PR 12.
assert_equals "$(resolve_ref_spec pr/fix-the-thing)" \
  "$(printf 'ref\tpr/fix-the-thing\tpr/fix-the-thing')" "resolve_ref_spec pr/<name>"

if (resolve_ref_spec "" >/dev/null 2>&1); then
  fail "resolve_ref_spec accepted an empty ref"
fi

# --- version_at_least: the overlay needs 2.24, and 2.9 must not look newer than 2.24.

version_cases=(
  "2.24.0 2.24.0 yes"
  "2.29.1 2.24.0 yes"
  "v2.39.4 2.24.0 yes"
  "2.9.0 2.24.0 no"
  "2.23.9 2.24.0 no"
  "1.29.2 2.24.0 no"
  "3.0 2.24.0 yes"
  "2.24 2.24.0 yes"
  "2.24.0-desktop.1 2.24.0 yes"
)
for case in "${version_cases[@]}"; do
  read -r have want expected <<<"$case"
  actual=no
  if version_at_least "$have" "$want"; then actual=yes; fi
  assert_equals "$actual" "$expected" "version_at_least $have >= $want"
done

# --- Deployment state survives a round trip, subject line and all.

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export GRIMOIRE_STATE_DIR="$work/state"

assert_equals "$(state_get ref)" "" "state_get before anything is deployed"

state_write "pr/95" "a703846e457303e8daa404c8f433fad53ae06474" \
  "deploy: run both containers rootless, with no capabilities" \
  "bbb4f84000000000000000000000000000000000"

assert_equals "$(state_get ref)" "pr/95" "state_get ref"
assert_equals "$(state_get sha)" "a703846e457303e8daa404c8f433fad53ae06474" "state_get sha"
assert_equals "$(state_get subject)" "deploy: run both containers rootless, with no capabilities" "state_get subject"
assert_equals "$(state_get previous_sha)" "bbb4f84000000000000000000000000000000000" "state_get previous_sha"
[[ "$(state_get deployed_at)" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$ ]] ||
  fail "state_get deployed_at is not an ISO-8601 UTC timestamp: $(state_get deployed_at)"

# A second deployment replaces the record rather than appending to it.
state_write "main" "1111111111111111111111111111111111111111" "later" "a703846e457303e8daa404c8f433fad53ae06474"
assert_equals "$(state_get ref)" "main" "state_get ref after a second deployment"
assert_equals "$(wc -l <"$(state_file)")" "5" "the state file holds one record"

# --- The overlay is seeded from the checkout once, then belongs to the host.
#
# This is what lets the server deploy a ref that predates this tooling — every ref until
# it merges — without silently losing the loopback binding.

fake_repo="$work/repo"
mkdir -p "$fake_repo/deploy/server"
printf 'seeded\n' >"$fake_repo/deploy/server/compose.server.yaml"

assert_equals "$(overlay_file "$fake_repo" 2>/dev/null)" "$GRIMOIRE_STATE_DIR/compose.server.yaml" "overlay_file path"
assert_equals "$(cat "$GRIMOIRE_STATE_DIR/compose.server.yaml")" "seeded" "overlay seeded from the checkout"

# An operator edit to the host copy is kept, and a checkout without the file still works.
printf 'edited by the operator\n' >"$GRIMOIRE_STATE_DIR/compose.server.yaml"
rm -rf "$fake_repo/deploy"
assert_equals "$(overlay_file "$fake_repo" 2>/dev/null)" "$GRIMOIRE_STATE_DIR/compose.server.yaml" "overlay_file path without a seed"
assert_equals "$(cat "$GRIMOIRE_STATE_DIR/compose.server.yaml")" "edited by the operator" "the host copy is not overwritten"

# With neither a host copy nor a seed there is nothing to run, and it says so.
rm -f "$GRIMOIRE_STATE_DIR/compose.server.yaml"
if (overlay_file "$fake_repo" >/dev/null 2>&1); then
  fail "overlay_file succeeded with no overlay and nothing to seed from"
fi

# --- The tailnet service name, port and URL this script derives from the environment.
#
# Nothing here runs `tailscale`: what is tested is the translation from an operator's
# environment into the arguments tailscale is handed, which is the only part of it this
# script decides. Whether `tailscale serve` then works is tailscale's own contract.

unset GRIMOIRE_TAILSCALE_SERVICE GRIMOIRE_TAILSCALE_PORT GRIMOIRE_TAILSCALE_DOMAIN

if tailscale_enabled; then fail "the tailnet service is on with GRIMOIRE_TAILSCALE_SERVICE unset"; fi
assert_equals "$(tailscale_service)" "" "tailscale_service with nothing configured"
assert_equals "$(tailscale_domain)" "" "tailscale_domain with nothing configured"
assert_equals "$(tailscale_url)" "" "tailscale_url with nothing configured"

# The `svc:` prefix is what tailscale validates against, so a bare name grows one.
for configured in grimoire svc:grimoire; do
  export GRIMOIRE_TAILSCALE_SERVICE="$configured"
  assert_equals "$(tailscale_service)" "svc:grimoire" "tailscale_service $configured"
done

export GRIMOIRE_TAILSCALE_SERVICE=grimoire-server
assert_equals "$(tailscale_service)" "svc:grimoire-server" "tailscale_service with a hyphen"

# Names tailscale would reject are rejected here instead — before a full image build.
for bad in "svc:" "Grimoire" "-grimoire" "grimoire-" "grim_oire" "grimoire.wiki" "svc:svc:grimoire"; do
  export GRIMOIRE_TAILSCALE_SERVICE="$bad"
  if (tailscale_service >/dev/null 2>&1); then
    fail "tailscale_service accepted an invalid service name: $bad"
  fi
done

# --- MagicDNSSuffix comes out of `tailscale status --json` without a jq dependency.

status_json='{
  "Version": "1.90.0",
  "MagicDNSSuffix": "crested-centauri.ts.net",
  "CurrentTailnet": {"Name": "crested-centauri.ts.net", "MagicDNSSuffix": "crested-centauri.ts.net"}
}'
assert_equals "$(printf '%s' "$status_json" | magic_dns_suffix)" "crested-centauri.ts.net" "magic_dns_suffix"
assert_equals "$(printf '%s' '{"Version":"1.90.0"}' | magic_dns_suffix)" "" "magic_dns_suffix when the host is logged out"

# --- The URL an operator is told to open.

export GRIMOIRE_TAILSCALE_SERVICE=svc:grimoire
export GRIMOIRE_TAILSCALE_DOMAIN=grimoire.crested-centauri.ts.net

assert_equals "$(tailscale_domain)" "grimoire.crested-centauri.ts.net" "tailscale_domain override"
assert_equals "$(tailscale_port)" "443" "tailscale_port default"
assert_equals "$(tailscale_url)" "https://grimoire.crested-centauri.ts.net" "tailscale_url on 443"

# A service endpoint on another port has to say so; 443 is the one that stays implicit.
export GRIMOIRE_TAILSCALE_PORT=8443
assert_equals "$(tailscale_url)" "https://grimoire.crested-centauri.ts.net:8443" "tailscale_url off 443"

unset GRIMOIRE_TAILSCALE_SERVICE GRIMOIRE_TAILSCALE_PORT GRIMOIRE_TAILSCALE_DOMAIN

if ((failures > 0)); then
  echo "$failures assertion(s) failed" >&2
  exit 1
fi

echo "grimoire-server: all assertions passed"
