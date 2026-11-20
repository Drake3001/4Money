#!/usr/bin/env bash

prepare_deployment() {
  local target="$1"
  shift
  if [[ $# != 1 || ! "$1" =~ ^sha-[0-9a-f]{40}$ ]]; then
    echo "Usage: bash deploy-$target.sh sha-<full lowercase commit SHA>" >&2
    return 1
  fi

  export IMAGE_TAG="$1"
  local directory
  directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../$target" && pwd)
  if [[ ! -f "$directory/.env" ]]; then
    echo "Create $directory/.env before deployment." >&2
    return 1
  fi
  command -v flock > /dev/null
  if [[ "${DEPLOY_LOCK_HELD:-false}" != true ]]; then
    exec 9>"$directory/.deploy.lock"
    if ! flock -n 9; then
      echo "Another deployment is already running on this VM." >&2
      return 1
    fi
  fi

  compose=(docker compose --project-directory "$directory" --env-file "$directory/.env" -f "$directory/docker-compose.yml")
  "${compose[@]}" config --quiet
  echo "Deploying $target with IMAGE_TAG=$IMAGE_TAG"
}
