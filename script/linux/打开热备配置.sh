#!/usr/bin/env bash
set -euo pipefail
root=$(cd -- "$(dirname -- "$0")/.." && pwd)
exec "${DOTNET_PATH:-dotnet}" "$root/components/IPC.Gateway.HaSetup/IPC.Gateway.HaSetup.dll" --configRoot "${1:-/etc/ipc-gateway/ha}"
