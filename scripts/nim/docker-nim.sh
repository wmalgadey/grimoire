#!/bin/bash

source "$(dirname "$0")/../../data/.env"

if [ -z "$NVIDIA_API_KEY" ]; then
  echo "Fehler: NVIDIA_API_KEY Umgebungsvariable fehlt."
  exit 1
fi

export NGC_API_KEY="$NVIDIA_API_KEY"
export LOCAL_NIM_CACHE=~/.cache/nim

echo "$NGC_API_KEY" | docker login nvcr.io --username '$oauthtoken' --password-stdin

#docker pull nvcr.io/nim/nvidia/model-free-nim:2.0.8
#docker pull nvcr.io/nim/${NVIDIA_MODEL}:latest

mkdir -p "$LOCAL_NIM_CACHE"
chmod -R a+w "$LOCAL_NIM_CACHE"
docker run -it --rm \
    --gpus all \
    --shm-size=16GB \
    -e NGC_API_KEY \
    -v "$LOCAL_NIM_CACHE:/opt/nim/.cache" \
    -p 8000:8000 \
    nvcr.io/nim/${NVIDIA_MODEL}:latest

curl -X 'POST' \
'http://0.0.0.0:8000/v1/chat/completions' \
-H 'accept: application/json' \
-H 'Content-Type: application/json' \
-d '{
    "model": "${NVIDIA_MODEL}",
    "messages": [{"role":"user", "content":"Which number is larger, 9.11 or 9.8?"}],
    "max_tokens": 64
}'
