#!/usr/bin/env bash
set -Eeuo pipefail
trap 'echo "APP deployment failed; inspect the failed step before retrying." >&2' ERR
source "$(dirname -- "${BASH_SOURCE[0]}")/deploy-common.sh"
prepare_deployment app "$@"

echo "Pulling APP images and migrations..."
"${compose[@]}" pull postgres-db seaweedfs backend frontend migrations

echo "Waiting for PostgreSQL..."
"${compose[@]}" up -d --wait --wait-timeout 120 --pull never --no-build postgres-db

echo "Applying database migrations..."
"${compose[@]}" run --rm -T --no-deps --pull never migrations

echo "Updating backend and storage..."
"${compose[@]}" up -d --wait --wait-timeout 180 --pull never --no-build --no-deps seaweedfs backend

# Recreate the frontend to render any updated Nginx template mounted from the host.
echo "Updating frontend..."
"${compose[@]}" up -d --wait --wait-timeout 120 --pull never --no-build --no-deps --force-recreate frontend

echo "Checking backend through the frontend proxy..."
ready=false
for attempt in {1..30}; do
  # An unauthenticated profile request must reach API authorization and return 401.
  if "${compose[@]}" exec -T frontend sh -c \
    'wget -S -O /dev/null -T 5 http://127.0.0.1:8080/api/users/profile 2>&1 | grep -qE "^[[:space:]]*HTTP/[^ ]+ 401([[:space:]]|$)"'; then
    ready=true
    break
  fi
  sleep 2
done
if [[ "$ready" != true ]]; then
  echo "Backend did not return the expected HTTP 401 through the frontend." >&2
  exit 1
fi
echo "APP deployment completed: $IMAGE_TAG"
