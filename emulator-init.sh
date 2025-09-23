#!/usr/bin/env bash
set -euo pipefail
PROJECT=demo-project
TOPIC=demo-topic
SUB=demo-sub

export PUBSUB_EMULATOR_HOST=localhost:8085
BASE="http://localhost:8085/v1"

echo "[Init] Creating topic $TOPIC via REST"
curl -s -X PUT "$BASE/projects/$PROJECT/topics/$TOPIC" -H "Content-Type: application/json" -d '{}' || true

echo "[Init] Creating subscription $SUB via REST"
curl -s -X PUT "$BASE/projects/$PROJECT/subscriptions/$SUB" -H "Content-Type: application/json" -d '{"topic":"projects/'"$PROJECT"'/topics/'"$TOPIC"'"}' || true

echo "[Init] Done initialization"
