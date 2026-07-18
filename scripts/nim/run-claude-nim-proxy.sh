#!/bin/bash

set -euo pipefail
set -x

source "$(dirname "$0")/../../data/.env"

bun install
bun run claude-nim -- --api-key $NVIDIA_API_KEY --serve-only --port 3456 --model $NVIDIA_MODEL
