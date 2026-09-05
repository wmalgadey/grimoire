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

# --- semver_identifier: a branch name has to survive being put in a version string.

identifier_cases=(
  "claude/frontend-batch-harness-wv1asv claude-frontend-batch-harness-wv1asv"
  "main main"
  "pr/95 pr-95"
  "feat/a_b.c feat-a-b-c"
  "--leading-and-trailing-- leading-and-trailing"
  "release/v1.2.3 release-v1-2-3"
)
for case in "${identifier_cases[@]}"; do
  read -r raw expected <<<"$case"
  assert_equals "$(semver_identifier "$raw")" "$expected" "semver_identifier $raw"
done

# --- format_image_version: the cases #158 had to decide, each pinned to its answer.

# The ordinary deployment: tag, branch, commits since the tag. The branch is the fact
# `git describe`'s 0.0.26-31-gcd913e4 left out.
assert_equals "$(format_image_version 0.0.26 claude/frontend-batch-harness-wv1asv 31)" \
  "0.0.26-claude-frontend-batch-harness-wv1asv.31" "format_image_version on a branch"

# `main` is not special-cased to GitVersion.yml's empty label — this string marks a
# deployment, not a release.
assert_equals "$(format_image_version 0.0.26 main 31)" "0.0.26-main.31" "format_image_version on main"

# A pull request head reads as itself.
assert_equals "$(format_image_version 0.0.26 pr/95 4)" "0.0.26-pr-95.4" "format_image_version on a pr head"

# Exactly on the tag: `0.0.26-main.0` would be a prerelease *of* 0.0.26 and would sort
# before the release it actually is.
assert_equals "$(format_image_version 0.0.26 main 0)" "0.0.26" "format_image_version exactly on the tag"

# `--force` with local changes: the one signal that happened, kept as build metadata so it
# does not change how the version sorts.
assert_equals "$(format_image_version 0.0.26 main 31 dirty)" "0.0.26-main.31+dirty" \
  "format_image_version on a dirty tree"
assert_equals "$(format_image_version 0.0.26 main 0 dirty)" "0.0.26+dirty" \
  "format_image_version on a dirty tree at the tag"

# `rollback` re-deploys a bare sha, so the label is not a branch name at all. An all-digit
# sha must not become a numeric identifier with leading zeros — that is not valid SemVer.
assert_equals "$(format_image_version 0.0.26 cd913e4a1b2c3d4e5f60718293a4b5c6d7e8f900 12)" \
  "0.0.26-gcd913e4a1b2c.12" "format_image_version on a rollback sha"
assert_equals "$(format_image_version 0.0.26 0012345678901234567890123456789012345678 12)" \
  "0.0.26-g001234567890.12" "format_image_version on an all-digit sha"

# A label with nothing usable in it still produces a parseable version.
assert_equals "$(format_image_version 0.0.26 "///" 7)" "0.0.26-unknown.7" \
  "format_image_version on a label with no usable characters"

# An all-digit prerelease identifier is a *numeric* one, and SemVer §9 forbids leading
# zeros on those. This repository numbers its feature branches, so a branch named `001` is
# not far-fetched, and `0.0.26-001.31` parses nowhere. `0` and `42` are valid numeric
# identifiers and must be left alone rather than swept up by the same rule.
assert_equals "$(format_image_version 0.0.26 001 31)" "0.0.26-ref-001.31" \
  "format_image_version on an all-digit label with leading zeros"
assert_equals "$(format_image_version 0.0.26 0 31)" "0.0.26-0.31" \
  "format_image_version on a bare zero label"
assert_equals "$(format_image_version 0.0.26 42 31)" "0.0.26-42.31" \
  "format_image_version on an all-digit label without leading zeros"

# --- The property behind every case above: whatever comes out, a SemVer parser accepts it.
#
# The enumerated cases pin the shapes we chose; this pins the reason we chose them. A future
# label class nobody thought of (the `001` case was one) fails here rather than reaching a
# consumer that parses the version.

semver_re='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-((0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(\+([0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*))?$'

assert_semver() {
  local value="$1" what="$2"
  [[ "$value" =~ $semver_re ]] || fail "$what: [$value] is not valid SemVer"
}

for label in main "claude/frontend-batch-harness-wv1asv" pr/95 001 0 42 007 "release/v1.2.3" \
  "///" "feat/a_b.c" cd913e4a1b2c3d4e5f60718293a4b5c6d7e8f900 0012345678901234567890123456789012345678; do
  for count in 0 1 31; do
    assert_semver "$(format_image_version 0.0.26 "$label" "$count")" "format_image_version $label/$count"
    assert_semver "$(format_image_version 0.0.26 "$label" "$count" dirty)" "format_image_version $label/$count dirty"
  done
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
# `tr -d` because BSD wc pads its count and GNU wc does not — the assertion is about the
# number of lines, not about which coreutils an operator's laptop ships.
assert_equals "$(wc -l <"$(state_file)" | tr -d ' ')" "5" "the state file holds one record"

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

# The port lands in `tailscale serve --https=` and in the sentence naming the `tcp:<port>`
# endpoint the tailnet policy needs. Neither is a useful place to discover a typo.
assert_equals "$(GRIMOIRE_TAILSCALE_PORT=8443 tailscale_port)" "8443" "tailscale_port explicit"
assert_equals "$(GRIMOIRE_TAILSCALE_PORT=65535 tailscale_port)" "65535" "tailscale_port at the top of the range"
assert_equals "$(GRIMOIRE_TAILSCALE_PORT=0443 tailscale_port)" "443" "tailscale_port normalises a leading zero"

for bad in "0" "65536" "99999" "443/tcp" "https" "-1" "8.443" "44 3"; do
  if (GRIMOIRE_TAILSCALE_PORT="$bad" tailscale_port >/dev/null 2>&1); then
    fail "tailscale_port accepted [$bad], which is not a TCP port"
  fi
done

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

# --- The tmux session name `status` reports and `tmux` attaches to are the same one.
#
# Two callers reading the same environment variable is exactly the shape that drifts: a
# `status` that reports on one session while `tmux` attaches to another is worse than no
# report at all, because it is a wrong answer rather than a missing one.

assert_equals "$(tmux_session)" "grimoire" "tmux_session default"
assert_equals "$(GRIMOIRE_TMUX_SESSION=ops tmux_session)" "ops" "tmux_session from the environment"

# --- This script's own version, and reading it back out of a copy.
#
# `update` names the version it moves from and to by reading the constant out of two files
# it does not run. That only works while the declaration keeps the shape the reader expects,
# so the reader is pointed at this very script — the copy that ships.

assert_equals "$(script_version_of "$script_dir/grimoire-server")" "$GRIMOIRE_SERVER_VERSION" \
  "script_version_of reads this script's own constant"
[[ "$GRIMOIRE_SERVER_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] ||
  fail "GRIMOIRE_SERVER_VERSION is not a semantic version: $GRIMOIRE_SERVER_VERSION"

assert_equals "$(script_version_of "$work/not-a-script")" "" "script_version_of on a missing file"
printf 'GRIMOIRE_SERVER_VERSION="0.1.0"\n' >"$work/older"
assert_equals "$(script_version_of "$work/older")" "0.1.0" "script_version_of on another copy"
printf '#!/usr/bin/env bash\necho hello\n' >"$work/unversioned"
assert_equals "$(script_version_of "$work/unversioned")" "" "script_version_of on a copy without the constant"

# --- #202: a checkout only changes files on disk, never the already-running interpreter —
# `warn_tool_drift` is deploy's and status's self-check that the two have actually diverged.

drift_repo="$work/drift-repo"
mkdir -p "$drift_repo/deploy/server"

# RUNNING_SCRIPT_CONTENT stands in for "what this process actually loaded at startup" —
# captured once, the same way the real script captures it before `cmd_deploy` can ever run
# a `git checkout`. Overriding it here is what lets these tests simulate "running an older
# copy" without needing a second real file on disk.
# shellcheck disable=SC2034 # RUNNING_SCRIPT_CONTENT is read by the sourced script's own functions.
RUNNING_SCRIPT_CONTENT="$(cat "$script_dir/grimoire-server")"

# The common case: the running copy and the checked-out copy have identical content.
cp "$script_dir/grimoire-server" "$drift_repo/deploy/server/grimoire-server"
assert_equals "$(warn_tool_drift "$drift_repo" main 2>&1)" "" \
  "warn_tool_drift is silent when the checkout matches the running copy"

# A checkout whose grimoire-server predates this tool: nothing to compare against.
rm "$drift_repo/deploy/server/grimoire-server"
assert_equals "$(warn_tool_drift "$drift_repo" main 2>&1)" "" \
  "warn_tool_drift is silent when the checkout has no grimoire-server at all"

# The drift case: the checkout carries a different copy than the one currently running.
printf 'GRIMOIRE_SERVER_VERSION="0.9.0"\n' >"$drift_repo/deploy/server/grimoire-server"
drift_output="$(warn_tool_drift "$drift_repo" main 2>&1)"
case "$drift_output" in
*"0.9.0"*"$GRIMOIRE_SERVER_VERSION"*"grimoire-server update && grimoire-server deploy main"*) ;;
*) fail "warn_tool_drift on a mismatch: expected old/new versions and the update+deploy hint, got [$drift_output]" ;;
esac
case "$drift_output" in
*"older"*) fail "warn_tool_drift asserted a direction ('older') it never verified: [$drift_output]" ;;
esac

# Same drift, no ref to suggest a follow-up deploy for (status with nothing deployed yet).
no_ref_output="$(warn_tool_drift "$drift_repo" 2>&1)"
case "$no_ref_output" in
*"grimoire-server update"*) ;;
*) fail "warn_tool_drift without a ref: expected the update hint, got [$no_ref_output]" ;;
esac

# The case a same-path `cmp` would miss entirely: this tool invoked straight out of the
# checkout, so $SCRIPT_PATH and the checked-out file are the *same path* — cmd_deploy's own
# `git checkout` overwrites that path with the new commit's content, and re-reading
# $SCRIPT_PATH at that point would just compare the file to itself. Comparing against the
# startup snapshot instead of $SCRIPT_PATH is what has to catch this.
self_path_repo="$work/self-path-repo"
mkdir -p "$self_path_repo/deploy/server"
cp "$script_dir/grimoire-server" "$self_path_repo/deploy/server/grimoire-server"
RUNNING_SCRIPT_CONTENT="$(cat "$self_path_repo/deploy/server/grimoire-server")"
printf 'GRIMOIRE_SERVER_VERSION="0.9.0"\n' >"$self_path_repo/deploy/server/grimoire-server"
same_path_output="$(warn_tool_drift "$self_path_repo" main 2>&1)"
case "$same_path_output" in
*"0.9.0"*) ;;
*) fail "warn_tool_drift missed drift when the checked-out path equals the running path: got [$same_path_output]" ;;
esac
case "$no_ref_output" in
*"deploy"*) fail "warn_tool_drift without a ref should not suggest a deploy target: got [$no_ref_output]" ;;
esac

# --- The installed copy is replaced by rename, not written in place.
#
# Bash reads a script as it runs it, so `update` overwriting the running file in place can
# leave a shell executing half of each version. What is asserted is the observable
# consequence: the inode changes, the mode is executable, and the content is the new one.

inode_of() { stat -c '%i' "$1" 2>/dev/null || stat -f '%i' "$1"; }

printf 'GRIMOIRE_SERVER_VERSION="9.9.9"\n' >"$work/source"
printf 'old\n' >"$work/installed"
chmod 0755 "$work/installed"
before_inode="$(inode_of "$work/installed")"

replace_script "$work/source" "$work/installed"

assert_equals "$(cat "$work/installed")" 'GRIMOIRE_SERVER_VERSION="9.9.9"' "replace_script copies the content"
assert_equals "$(script_version_of "$work/installed")" "9.9.9" "replace_script's result reads as the new version"
[[ -x "$work/installed" ]] || fail "replace_script left the copy without its executable bit"
[[ "$(inode_of "$work/installed")" != "$before_inode" ]] ||
  fail "replace_script wrote in place — the running script's inode was truncated under it"
# Nothing of the swap is left behind in the destination directory.
assert_equals "$(find "$work" -maxdepth 1 -name '.grimoire-server.*' | wc -l | tr -d ' ')" "0" \
  "replace_script leaves no temporary file behind"

replace_script "$work/source" "$work/fresh/grimoire-server"
assert_equals "$(script_version_of "$work/fresh/grimoire-server")" "9.9.9" "replace_script creates a missing directory"

if (replace_script "$work/does-not-exist" "$work/installed" >/dev/null 2>&1); then
  fail "replace_script accepted a source that does not exist"
fi

# --- Where `update` looks for the copy to refresh.

fake_checkout="$work/checkout"
mkdir -p "$fake_checkout/deploy/server"

# Running the checkout's own script: there is nothing to update, and no record says there is.
# SCRIPT_PATH is read by the sourced script, which is what `installed_copy` resolves — the
# assignments below are the input to these assertions, not dead stores.
# shellcheck disable=SC2034
rm -f "$(tool_file)"
SCRIPT_PATH="$fake_checkout/deploy/server/grimoire-server"
assert_equals "$(installed_copy "$fake_checkout")" "" "installed_copy while running from the checkout"

# The same run, once `install` has written down where it put a copy: that copy is the one
# an `update` from inside the checkout has to refresh.
tool_write "1.0.0" "a703846e457303e8daa404c8f433fad53ae06474" "$HOME/.local/bin/grimoire-server"
assert_equals "$(installed_copy "$fake_checkout")" "$HOME/.local/bin/grimoire-server" \
  "installed_copy falls back to the path install recorded"
assert_equals "$(record_get "$(tool_file)" version)" "1.0.0" "the install record keeps the version"
assert_equals "$(record_get "$(tool_file)" commit)" "a703846e457303e8daa404c8f433fad53ae06474" \
  "the install record keeps the commit"
assert_equals "$(record_get "$(tool_file)" missing)" "" "record_get on an absent key"

# Running an installed copy: that copy is the one, whatever the record says.
SCRIPT_PATH="$work/installed"
assert_equals "$(installed_copy "$fake_checkout")" "$work/installed" "installed_copy while running an installed copy"

# --- Invoked by bare name off $PATH, which is how an installed copy is actually run.
#
# `installed_copy` resolves the running script from BASH_SOURCE[0], and everything `update`
# does hangs off getting that right: a wrong answer here means refreshing the wrong file, or
# a file that does not exist. The case worth pinning is the one an operator uses — `PATH` set
# by ~/.bashrc, command typed bare — because it is the one where BASH_SOURCE[0] could
# plausibly be just the command name.
#
# It is not: the kernel resolves a shebang script's path before the interpreter ever sees it,
# so BASH_SOURCE[0] arrives absolute. That is a property of how the script is launched rather
# than of anything this file does, which is exactly why it is worth a test — it cannot be
# read off the source, and `installed_copy` would need a `command -v` fallback if it were
# ever untrue. Run as a real subprocess, because that is the only way to exercise it;
# `version` is used because it reports the resolved path and touches no network or docker.

path_dir="$work/bin"
mkdir -p "$path_dir"
cp "$script_dir/grimoire-server" "$path_dir/grimoire-server"
chmod 0755 "$path_dir/grimoire-server"

reported="$(PATH="$path_dir:$PATH" GRIMOIRE_REPO="$fake_checkout" GRIMOIRE_STATE_DIR="$work/state-bare" \
  grimoire-server version 2>&1 | sed -n 's/^ *script *//p' | tail -n 1)"

assert_equals "$reported" "$path_dir/grimoire-server" \
  "a bare-name invocation off \$PATH resolves to the absolute installed copy"

if ((failures > 0)); then
  echo "$failures assertion(s) failed" >&2
  exit 1
fi

echo "grimoire-server: all assertions passed"
