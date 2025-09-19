#!/usr/bin/env bash
set -euo pipefail
PROJECT=demo-project
TOPIC=demo-topic
SUB=demo-sub

export PUBSUB_EMULATOR_HOST=localhost:8085

echo "[Init] Creating topic $TOPIC"
gcloud pubsub topics create $TOPIC --project=$PROJECT || true

echo "[Init] Creating subscription $SUB"
gcloud pubsub subscriptions create $SUB --topic=$TOPIC --project=$PROJECT || true

echo "[Init] Done initialization"
