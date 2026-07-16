#!/bin/bash

source "$(dirname "$0")/../../.env"

# Voraussetzung: NVIDIA_API_KEY muss gesetzt sein
# Exportiere deinen Key vorher: export NVIDIA_API_KEY="nvapi-..."

if [ -z "$NVIDIA_API_KEY" ]; then
  echo "Fehler: NVIDIA_API_KEY Umgebungsvariable fehlt."
  exit 1
fi

PROMPT="${1:-Erkläre Kubernetes in einem Satz.}"
MODEL=${NVIDIA_MODEL:-"meta/llama-3.1-8b-instruct"}
ENDPOINT="https://integrate.api.nvidia.com/v1/chat/completions"

# Sicheres JSON-Payload-Building
PAYLOAD=$(jq -n --arg model "$MODEL" --arg prompt "$PROMPT" '{
  model: $model,
  messages: [{"role": "user", "content": $prompt}],
  temperature: 0.3,
  max_tokens: 1024,
  stream: false
}')

curl -s -X POST "$ENDPOINT" \
  -H "Authorization: Bearer $NVIDIA_API_KEY" \
  -H "Content-Type: application/json" \
  -d "$PAYLOAD" | jq -r '.choices[0].message.content'
