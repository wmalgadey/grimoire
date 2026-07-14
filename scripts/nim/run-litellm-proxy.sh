#!/bin/bash

set -euo pipefail

source "$(dirname "$0")/../../.env"

pip install uv
uv tool install 'litellm[proxy]' --with python-dotenv
uv tool update-shell

export NVIDIA_NIM_API_KEY=$NVIDIA_API_KEY
litellm --config litellm_config.yaml --port 4000
