#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} == --help || ${1:-} == -h ]]; then
  echo 'Usage: [REPOSITORY_ROOT=...] [ENVIRONMENT_FILE=...] [NO_BUILD=1] bash 发布Docker.sh'
  echo 'Optional: COMPOSE_FILE, PROJECT_NAME, IMAGE. Requires Docker Compose v2.'
  exit 0
fi
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
REPOSITORY_ROOT="${REPOSITORY_ROOT:-$script_dir/../..}"
COMPOSE_FILE="${COMPOSE_FILE:-$script_dir/docker-compose.external-db.yml}"
ENVIRONMENT_FILE="${ENVIRONMENT_FILE:-$script_dir/gateway.env}"
PROJECT_NAME="${PROJECT_NAME:-ipc-gateway}"
NO_BUILD="${NO_BUILD:-0}"
[[ $PROJECT_NAME =~ ^[a-z0-9][a-z0-9_-]*$ ]] || { echo 'Invalid PROJECT_NAME.' >&2; exit 1; }
[[ $NO_BUILD == 0 || $NO_BUILD == 1 ]] || { echo 'NO_BUILD must be 0 or 1.' >&2; exit 1; }
[[ -f $ENVIRONMENT_FILE ]] || { echo 'Copy gateway.env.example to gateway.env and configure it first.' >&2; exit 1; }
[[ $NO_BUILD == 1 || -f $REPOSITORY_ROOT/script/docker/Dockerfile ]] || { echo 'Source checkout missing. Set REPOSITORY_ROOT or load a release image and set NO_BUILD=1.' >&2; exit 1; }
export IPC_GATEWAY_SOURCE_DIRECTORY
IPC_GATEWAY_SOURCE_DIRECTORY=$(cd -- "$REPOSITORY_ROOT" && pwd)
export IPC_GATEWAY_ENV_FILE
IPC_GATEWAY_ENV_FILE=$(cd -- "$(dirname -- "$ENVIRONMENT_FILE")" && pwd)/$(basename -- "$ENVIRONMENT_FILE")
export IPC_GATEWAY_IMAGE="${IMAGE:-ipc-gateway:latest}"
docker compose version
args=(compose -p "$PROJECT_NAME" --env-file "$IPC_GATEWAY_ENV_FILE" -f "$COMPOSE_FILE")
docker "${args[@]}" config --quiet
if [[ $NO_BUILD == 1 ]]; then docker "${args[@]}" up -d --no-build; else docker "${args[@]}" up -d --build; fi
echo 'Containers started. Check logs and /health/ready before putting the gateway into service.'
