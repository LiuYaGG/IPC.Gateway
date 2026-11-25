#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} == --deploy-gateway ]]; then
  if [[ $# != 7 || ! $2 =~ ^[AB]$ ]]; then echo 'Usage: --deploy-gateway A|B config-dir local-data-dir peer-hot-dir peer-cold-dir registry/gateway:version' >&2; exit 2; fi
  export IPC_HA_NODE=$2 IPC_HA_GATEWAY_IMAGE=$7
  export IPC_HA_GATEWAY_CONFIG_DIR IPC_HA_GATEWAY_DATA IPC_HA_PEER_HISTORY IPC_HA_PEER_COLD
  IPC_HA_GATEWAY_CONFIG_DIR=$(cd -- "$3" && pwd -P)
  IPC_HA_GATEWAY_DATA=$(cd -- "$4" && pwd -P)
  IPC_HA_PEER_HISTORY=$(cd -- "$5" && pwd -P)
  IPC_HA_PEER_COLD=$(cd -- "$6" && pwd -P)
  [[ -f "$IPC_HA_GATEWAY_CONFIG_DIR/appsettings.Production.json" ]] || { echo 'Missing appsettings.Production.json in the node configuration directory.' >&2; exit 1; }
  for path in "$IPC_HA_GATEWAY_CONFIG_DIR" "$IPC_HA_PEER_HISTORY" "$IPC_HA_PEER_COLD"; do
    [[ "$path/" != "$IPC_HA_GATEWAY_DATA/"* && "$IPC_HA_GATEWAY_DATA/" != "$path/"* ]] || { echo 'Keep configuration, local data and remote history on separate paths.' >&2; exit 1; }
  done
  root=$(cd -- "$(dirname -- "$0")" && pwd)
  args=(-f "$root/docker-compose.ha-gateway.yml" -p "ipc-ha-gateway-${IPC_HA_NODE,,}")
  legacy=$(docker ps -aq --filter "label=com.docker.compose.project=ipc-arbiter-${IPC_HA_NODE,,}")
  [[ -z $legacy ]] || { echo 'Remove the old standalone arbiter container and retain its volume before installing.' >&2; exit 1; }
  docker compose "${args[@]}" config --quiet
  running=$(docker compose "${args[@]}" ps --status running -q gateway)
  [[ -z $running ]] || { echo 'Transfer the workload and stop this business node before updating.' >&2; exit 1; }
  exec docker compose "${args[@]}" up -d --no-build gateway
fi
if [[ ${1:-} == --gateway-client ]]; then
  [[ $# == 2 ]] || { echo 'Usage: --gateway-client registry/gateway:version' >&2; exit 2; }
  root=$(cd -- "$(dirname -- "$0")/../.." && pwd)
  exec docker build -f "$root/script/docker/Dockerfile" -t "$2" "$root"
fi
if [[ ${1:-} == --help || $# -lt 3 ]]; then
  echo 'Usage: bash 发布仲裁客户端.sh NODE_ID /path/to/config-dir registry/image:version [--no-build]'
  echo 'Build the business gateway image with its embedded HA client: --gateway-client registry/gateway:version'
  echo 'Deploy that image on A or B: --deploy-gateway A|B config-dir local-data-dir peer-hot-dir peer-cold-dir registry/gateway:version'
  [[ ${1:-} == --help ]] && exit 0 || exit 2
fi
[[ ${1^^} == C && $# -le 4 ]] || { echo 'Standalone member deployment is for C. Install the integrated gateway on A or B.' >&2; exit 2; }
export IPC_ARBITER_NODE="${1^^}"
export IPC_ARBITER_CONFIG_DIR="$(cd -- "$2" && pwd)"
[[ -f "$IPC_ARBITER_CONFIG_DIR/member.json" ]]
export IPC_ARBITER_IMAGE="$3"
export IPC_ARBITER_VOLUME="ipc-arbiter-${1,,}-state"
root=$(cd -- "$(dirname -- "$0")" && pwd)
args=(-f "$root/docker-compose.arbiter.yml" -p "ipc-arbiter-${1,,}")
build=--build
if [[ $# == 4 ]]; then [[ $4 == --no-build ]]; build=--no-build; fi
running=$(docker compose "${args[@]}" ps --status running -q arbiter)
if [[ -n $running ]]; then
  echo 'Verify the other two voters, then stop this member before updating.' >&2; exit 1
fi
docker compose "${args[@]}" up -d "$build" arbiter
