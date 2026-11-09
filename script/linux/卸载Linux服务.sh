#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} == --help || ${1:-} == -h ]]; then
  echo 'Usage: sudo [SERVICE_NAME=ipc-gateway] bash 卸载Linux服务.sh'
  echo 'Removes only the systemd service. Program, configuration, user and data are retained.'
  exit 0
fi
SERVICE_NAME="${SERVICE_NAME:-ipc-gateway}"
[[ $EUID == 0 ]] || { echo 'Run with sudo/root.' >&2; exit 1; }
[[ $SERVICE_NAME =~ ^[a-zA-Z_][a-zA-Z0-9_-]*$ ]] || { echo 'Invalid service name.' >&2; exit 1; }
unit="/etc/systemd/system/$SERVICE_NAME.service"
if [[ ! -f $unit ]]; then echo "$SERVICE_NAME is not installed by this installer."; exit 0; fi
grep -q 'IPC Gateway Edge Service' "$unit" || { echo 'Refusing to remove an unrelated service.' >&2; exit 1; }
systemctl disable --now "$SERVICE_NAME.service"
rm -- "$unit"
systemctl daemon-reload
systemctl reset-failed "$SERVICE_NAME.service" 2>/dev/null || true
echo "Removed $SERVICE_NAME. Program, configuration, database, data and service user were retained."
