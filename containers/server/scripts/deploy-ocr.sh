#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "OCR deployment failed; inspect the failed step before retrying." >&2' ERR
source "$(dirname -- "${BASH_SOURCE[0]}")/deploy-common.sh"
prepare_deployment ocr "$@"

echo "Pulling OCR image..."
"${compose[@]}" pull extraction-worker

echo "Updating OCR and waiting for its healthcheck..."
"${compose[@]}" up -d --wait --wait-timeout 600 --pull never --no-build extraction-worker
echo "OCR deployment completed: $IMAGE_TAG"
