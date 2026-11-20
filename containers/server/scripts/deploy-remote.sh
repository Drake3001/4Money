#!/usr/bin/env bash
set -Eeuo pipefail

if [[ $# != 1 || ! "$1" =~ ^sha-[0-9a-f]{40}$ ]]; then
  echo "Usage: bash deploy-remote.sh sha-<full lowercase commit SHA>" >&2
  exit 1
fi
image_tag="$1"
: "${SERVER_SSH_KEY_PATH:?Set SERVER_SSH_KEY_PATH to the private key path on the runner}"
: "${SERVER_KNOWN_HOSTS_PATH:?Set SERVER_KNOWN_HOSTS_PATH to the verified known_hosts path on the runner}"
[[ -r "$SERVER_SSH_KEY_PATH" && -s "$SERVER_KNOWN_HOSTS_PATH" ]]
for tool in ssh tar; do command -v "$tool" > /dev/null; done

repository=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)
destination=/home/debian/4money
ssh_options=(-F /dev/null -i "$SERVER_SSH_KEY_PATH" -o IdentitiesOnly=yes -o BatchMode=yes
  -o StrictHostKeyChecking=yes -o "UserKnownHostsFile=$SERVER_KNOWN_HOSTS_PATH" -o ConnectTimeout=10)

deploy_vm() {
  local target="$1" host="$2"
  local files=("containers/server/$target/docker-compose.yml"
    containers/server/scripts/deploy-common.sh "containers/server/scripts/deploy-$target.sh")
  if [[ "$target" == app ]]; then
    files+=(containers/server/app/nginx/default.conf.template database/init)
  fi
  echo "Deploying $target to $host with $image_tag"
  # Take the same VM lock before replacing live configuration and keep it through deployment.
  tar -C "$repository" -cf - "${files[@]}" | ssh "${ssh_options[@]}" "debian@$host" \
    "set -eu
     mkdir -p '$destination/containers/server/$target'
     exec 9>'$destination/containers/server/$target/.deploy.lock'
     flock -n 9 || { echo 'Another deployment is running.' >&2; exit 1; }
     test -f '$destination/containers/server/$target/.env'
     tar -xf - -C '$destination'
     DEPLOY_LOCK_HELD=true bash '$destination/containers/server/scripts/deploy-$target.sh' '$image_tag'"
}

deploy_vm ocr 192.168.0.221
deploy_vm app 192.168.0.220
