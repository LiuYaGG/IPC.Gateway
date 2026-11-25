#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} == --help || ${1:-} == -h ]]; then
  echo 'Usage: sudo bash 卸载Linux服务.sh [--gateway|--arbiter|--all]'
  echo 'Removes only the systemd service. Program, configuration, user and data are retained.'
  exit 0
fi
case "${1:---gateway}" in
  --gateway) units=("${SERVICE_NAME:-ipc-gateway}");;
  --arbiter) units=(ipc-gateway-arbiter ipc-gateway-arbiter-a ipc-gateway-arbiter-b ipc-gateway-arbiter-c);;
  --all) units=("${SERVICE_NAME:-ipc-gateway}" ipc-gateway-arbiter ipc-gateway-arbiter-a ipc-gateway-arbiter-b ipc-gateway-arbiter-c);;
  *) echo 'Unknown option' >&2; exit 1;;
esac
[[ $EUID == 0 ]] || { echo 'Run with sudo/root.' >&2; exit 1; }
for SERVICE_NAME in "${units[@]}"; do
[[ $SERVICE_NAME =~ ^[a-zA-Z_][a-zA-Z0-9_-]*$ ]] || { echo 'Invalid service name.' >&2; exit 1; }
unit="/etc/systemd/system/$SERVICE_NAME.service"
if [[ ! -f $unit ]]; then echo "$SERVICE_NAME is not installed by this installer."; continue; fi
case "$SERVICE_NAME" in ipc-gateway-arbiter|ipc-gateway-arbiter-[abc]) description='IPC Gateway Arbiter';; *) description='IPC Gateway Edge Service';; esac
grep -Fxq "Description=$description" "$unit" || { echo 'Refusing to remove an unrelated service.' >&2; exit 1; }
systemctl disable --now "$SERVICE_NAME.service"
for helper in "$SERVICE_NAME-update.path" "$SERVICE_NAME-update.service"; do
  helper_unit="/etc/systemd/system/$helper"
  if [[ -f $helper_unit ]] && grep -Eq '^Description=IPC Gateway (Update Request Watcher|Restricted Update Worker)$' "$helper_unit"; then
    systemctl disable --now "$helper" 2>/dev/null || true
    rm -- "$helper_unit"
  fi
done
rm -- "$unit"
drop="$unit.d/90-ipc-ha.conf"
if [[ -f $drop ]] && grep -Fxq '# IPC Gateway HA installer' "$drop"; then rm -- "$drop"; fi
systemctl daemon-reload
systemctl reset-failed "$SERVICE_NAME.service" 2>/dev/null || true
echo "Removed $SERVICE_NAME. Program, configuration, database, data and service user were retained."
done
