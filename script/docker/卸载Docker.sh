#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} == --help || ${1:-} == -h ]]; then
  echo 'Usage: [ENVIRONMENT_FILE=...] [COMPOSE_FILE=...] [PROJECT_NAME=ipc-gateway] bash 卸载Docker.sh'
  echo 'Removes containers and network only; persistent volumes, images, configuration and database are retained.'
  exit 0
fi
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
COMPOSE_FILE="${COMPOSE_FILE:-$script_dir/docker-compose.external-db.yml}"
ENVIRONMENT_FILE="${ENVIRONMENT_FILE:-$script_dir/gateway.env}"
PROJECT_NAME="${PROJECT_NAME:-ipc-gateway}"
[[ $PROJECT_NAME =~ ^[a-z0-9][a-z0-9_-]*$ ]] || { echo 'Invalid PROJECT_NAME.' >&2; exit 1; }
[[ -f $ENVIRONMENT_FILE ]] || { echo 'Use the same environment file used for deployment.' >&2; exit 1; }
export IPC_GATEWAY_ENV_FILE
IPC_GATEWAY_ENV_FILE=$(cd -- "$(dirname -- "$ENVIRONMENT_FILE")" && pwd)/$(basename -- "$ENVIRONMENT_FILE")
docker compose -p "$PROJECT_NAME" --env-file "$IPC_GATEWAY_ENV_FILE" -f "$COMPOSE_FILE" down
echo 'Containers/network removed; volumes, images, environment file and database retained.'
