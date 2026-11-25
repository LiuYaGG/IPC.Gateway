#!/usr/bin/env bash
set -euo pipefail
usage() {
  echo 'Build: bash 发布仲裁客户端.sh build /absolute/empty-version-dir [linux-x64|linux-arm64]'
  echo 'Business gateway with embedded client: use build-client with the same arguments.'
  echo 'Install: sudo bash 发布仲裁客户端.sh install /absolute/published-dir /absolute/member.json'
  echo 'Optional: DATA_DIR=/var/lib/ipc-gateway-arbiter START_SERVICE=1'
}
case "${1:---help}" in
  --help|-h) usage; exit 0 ;;
  build|build-client)
    dir=${2:?Specify an empty publish directory}
    rid=${3:-}
    if [[ -z $rid ]]; then case "$(uname -m)" in x86_64) rid=linux-x64;; aarch64|arm64) rid=linux-arm64;; *) echo 'Specify a supported runtime' >&2; exit 1;; esac; fi
    [[ $rid == linux-x64 || $rid == linux-arm64 ]]
    [[ ! -e $dir || ( -d $dir && -z "$(ls -A -- "$dir")" ) ]] || { echo 'Publish directory must be empty' >&2; exit 1; }
    root=$(cd -- "$(dirname -- "$0")/../.." && pwd)
    if [[ $1 == build-client ]]; then
      (cd "$root/src/IPC.Gateway.Web" && npm ci && npm run build)
      dotnet publish "$root/src/IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj" -c Release -r "$rid" --self-contained false -o "$dir"
      dotnet publish "$root/src/IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj" -c Release -r "$rid" --self-contained false -o "$dir/Drivers"
      exit 0
    fi
    exec dotnet publish "$root/src/IPC.Gateway.Arbiter/IPC.Gateway.Arbiter.csproj" -c Release -r "$rid" --self-contained false -o "$dir"
    ;;
  install) ;;
  *) usage; exit 2 ;;
esac
[[ $EUID == 0 ]] || { echo 'Install requires root' >&2; exit 1; }
app=$(realpath -e -- "${2:?Specify the published directory}")
config=$(realpath -e -- "${3:?Specify the provisioned member JSON}")
config_dir=$(dirname -- "$config")
case "$config_dir" in /|/etc|/opt|/usr|/usr/local|/var|/var/lib|/home) echo 'Use a directory dedicated to this member configuration.' >&2; exit 1 ;; esac
python3 -c 'import json,sys; d=json.load(open(sys.argv[1],encoding="utf-8-sig"),object_hook=lambda o:{k.lower():v for k,v in o.items()}); sys.exit(d.get("arbiter",{}).get("nodeid") != "C")' "$config" || { echo 'Standalone member installation is for C. Install the integrated gateway on A or B.' >&2; exit 1; }
data=$(realpath -m -- "${DATA_DIR:-/var/lib/ipc-gateway-arbiter}")
dotnet=$(command -v dotnet)
[[ -f $app/IPC.Gateway.Arbiter.dll && -f $config && $data != "$app" && $data != "$app/"* ]]
for path in "$app" "$config" "$data" "$dotnet"; do [[ $path =~ ^/[a-zA-Z0-9_./-]+$ ]] || { echo 'Use simple absolute paths in the systemd service' >&2; exit 1; }; done
service=${ARBITER_SERVICE:-ipc-gateway-arbiter}
[[ $service =~ ^ipc-gateway-arbiter(-c)?$ ]]
unit=/etc/systemd/system/$service.service
if [[ -f $unit ]] && ! grep -Fxq 'Description=IPC Gateway Arbiter' "$unit"; then echo 'Unit name is already used' >&2; exit 1; fi
if systemctl is-active --quiet "$service"; then echo 'Drain and stop this member after checking the other two voters' >&2; exit 1; fi
getent group ipc-arbiter >/dev/null || groupadd --system ipc-arbiter
id ipc-arbiter >/dev/null 2>&1 || useradd --system --gid ipc-arbiter --home-dir "$data" --no-create-home --shell /usr/sbin/nologin ipc-arbiter
if [[ ! -d $data ]]; then install -d -m 750 -o ipc-arbiter -g ipc-arbiter "$data"; fi
runuser -u ipc-arbiter -- test -w "$data" || { echo "Grant ipc-arbiter write access to $data" >&2; exit 1; }
runuser -u ipc-arbiter -- test -r "$config" || { echo "Grant ipc-arbiter read access to $config and its TLS files" >&2; exit 1; }
if ! runuser -u ipc-arbiter -- test -w "$config_dir"; then
  command -v setfacl >/dev/null || { echo "Grant ipc-arbiter write access to the dedicated identity directory $config_dir (install acl/setfacl)." >&2; exit 1; }
  setfacl -m u:ipc-arbiter:rwx "$config_dir"
fi
runuser -u ipc-arbiter -- test -w "$config_dir" || { echo "Grant ipc-arbiter write access to $config_dir for signed network changes." >&2; exit 1; }
cat > "$unit" <<UNIT
[Unit]
Description=IPC Gateway Arbiter
After=network-online.target
Wants=network-online.target
[Service]
User=ipc-arbiter
Group=ipc-arbiter
WorkingDirectory=$app
ExecStart=$dotnet $app/IPC.Gateway.Arbiter.dll
Environment=IPC_ARBITER_CONFIG=$config
Environment=Arbiter__StateDirectory=$data
EnvironmentFile=-/etc/ipc-arbiter/arbiter.env
Restart=on-failure
RestartSec=3
TimeoutStopSec=60
ProtectSystem=strict
ReadWritePaths=$data $config_dir
NoNewPrivileges=true
PrivateTmp=true
[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable "$service"
if [[ ${START_SERVICE:-0} == 1 ]]; then systemctl start "$service"; fi
