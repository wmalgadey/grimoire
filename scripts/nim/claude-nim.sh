#!/bin/bash

source "$(dirname "$0")/../../.env"

export ANTHROPIC_API_KEY="${NVIDIA_API_KEY}"
export ANTHROPIC_BASE_URL="https://integrate.api.nvidia.com"

# Zwingende Überschreibung der Anthropic-Routings
export ANTHROPIC_CUSTOM_MODEL_OPTION="${NVIDIA_MODEL}"
export ANTHROPIC_DEFAULT_HAIKU_MODEL="${NVIDIA_MODEL}"
export ANTHROPIC_DEFAULT_OPUS_MODEL="${NVIDIA_MODEL}"
export ANTHROPIC_DEFAULT_SONNET_MODEL="${NVIDIA_MODEL}"
export CLAUDE_CODE_SUBAGENT_MODEL="${NVIDIA_MODEL}"

echo "Starte Claude Code mit NIM Modell: $NVIDIA_MODEL"
claude