#!/usr/bin/env bash
set -euo pipefail
if [[ $# -lt 2 || ! $1 =~ ^[ABC]$ || $# -gt 3 || (${3:-} != '' && ${3:-} != '--uninstall') ]]; then
  echo 'Usage: bash 安装热备服务.sh A|B|C /path/to/role.env [--uninstall]' >&2; exit 2
fi
role=$1
dir=$(cd -- "$(dirname -- "$0")" && pwd -P)
envfile=$(realpath -e -- "$2")
export IPC_ARBITER_NODE=$role IPC_HA_NODE=$role
svc=gateway; [[ $role != C ]] || svc=arbiter
compose() {
  local file project
  if [[ $1 == arbiter ]]; then file=docker-compose.arbiter.yml; project=ipc-arbiter-${role,,}
  else file=docker-compose.ha-gateway.yml; project=ipc-ha-gateway-${role,,}; fi
  shift
  docker compose -f "$dir/$file" --env-file "$envfile" -p "$project" "$@"
}
compose "$svc" config --quiet
if [[ ${3:-} == --uninstall ]]; then
  compose "$svc" down
else
  if [[ $role != C ]]; then
    legacy=$(docker ps -aq --filter "label=com.docker.compose.project=ipc-arbiter-${role,,}")
    [[ -z $legacy ]] || { echo "Remove the old standalone arbiter $role container and retain its volume before installing." >&2; exit 1; }
  fi
  running=$(compose "$svc" ps --status running -q)
  [[ -z $running ]] || { echo "Stop the existing $role $svc container before updating." >&2; exit 1; }
  compose "$svc" up -d --no-build
fi
