#!/bin/bash

source "$(dirname "$0")/../../.env"

export ANTHROPIC_API_KEY="${NVIDIA_API_KEY}"
export ANTHROPIC_BASE_URL="https://integrate.api.nvidia.com"

# Zwingende Überschreibung der Anthropic-Routings
export ANTHROPIC_CUSTOM_MODEL_OPTION="${MODEL_NAME}"
export ANTHROPIC_DEFAULT_HAIKU_MODEL="${MODEL_NAME}"
export ANTHROPIC_DEFAULT_OPUS_MODEL="${MODEL_NAME}"
export ANTHROPIC_DEFAULT_SONNET_MODEL="${MODEL_NAME}"
export CLAUDE_CODE_SUBAGENT_MODEL="${MODEL_NAME}"

echo "Starte Claude Code mit NIM Modell: $MODEL_NAME"
claude