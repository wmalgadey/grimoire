#!/bin/bash

source "$(dirname "$0")/../../.env"

bun install
bun run claude-nim -- --api-key $NVIDIA_API_KEY --serve-only --port 3456 --model $GRIMOIRE_INGEST_MODEL
