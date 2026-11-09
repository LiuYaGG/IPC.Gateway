#!/usr/bin/env bash
# Install/update a published payload; preserve site configuration and data.
set -euo pipefail
if [[ ${1:-} == --help || ${1:-} == -h ]]; then
  echo 'Usage: sudo [INSTALL_DIR=...] [DATA_DIR=...] [START_SERVICE=1] bash 发布Linux服务.sh /path/to/payload'
  echo 'Optional: SERVICE_NAME, SERVICE_USER, SERVICE_GROUP, SERVICE_URL, ENVIRONMENT_FILE, DOTNET_PATH'
  exit 0
fi
fail() { echo "ERROR: $*" >&2; exit 1; }
SERVICE_NAME="${SERVICE_NAME:-ipc-gateway}"
SERVICE_USER="${SERVICE_USER:-ipc-gateway}"
SERVICE_GROUP="${SERVICE_GROUP:-ipc-gateway}"
INSTALL_DIR="${INSTALL_DIR:-/opt/ipc-gateway/app}"
DATA_DIR="${DATA_DIR:-$INSTALL_DIR/Data}"
ENVIRONMENT_FILE="${ENVIRONMENT_FILE:-/etc/ipc-gateway/$SERVICE_NAME.env}"
SERVICE_URL="${SERVICE_URL:-http://127.0.0.1:5184}"
START_SERVICE="${START_SERVICE:-0}"
PUBLISH_DIR="${1:-${PUBLISH_DIR:-}}"
[[ $EUID == 0 ]] || fail 'Run with sudo/root.'
for name in "$SERVICE_NAME" "$SERVICE_USER" "$SERVICE_GROUP"; do
  [[ $name =~ ^[a-zA-Z_][a-zA-Z0-9_-]*$ ]] || fail "Invalid service/user/group name: $name"
done
[[ $START_SERVICE == 0 || $START_SERVICE == 1 ]] || fail 'START_SERVICE must be 0 or 1.'
for tool in systemctl rsync realpath getent useradd groupadd usermod; do
  command -v "$tool" >/dev/null || fail "Missing prerequisite: $tool"
done
DOTNET_PATH="${DOTNET_PATH:-$(command -v dotnet || true)}"
[[ -x $DOTNET_PATH ]] || fail 'Install ASP.NET Core Runtime 10 first, or set DOTNET_PATH.'
"$DOTNET_PATH" --list-runtimes | grep -q '^Microsoft.AspNetCore.App 10\.' || fail 'ASP.NET Core Runtime 10 is required.'
[[ -n $PUBLISH_DIR ]] || fail 'Specify a published directory or an extracted ZIP payload directory.'
PUBLISH_DIR=$(realpath -e -- "$PUBLISH_DIR")
[[ -f $PUBLISH_DIR/IPC.Gateway.WebHost.dll && -f $PUBLISH_DIR/wwwroot/index.html ]] || fail 'Published backend/frontend files are missing.'
[[ -d $PUBLISH_DIR/Drivers ]] || fail 'Published Drivers directory is missing.'
INSTALL_DIR=$(realpath -m -- "$INSTALL_DIR")
DATA_DIR=$(realpath -m -- "$DATA_DIR")
ENVIRONMENT_FILE=$(realpath -m -- "$ENVIRONMENT_FILE")
# Restrict generated unit paths so whitespace/specifiers cannot alter directives.
for value in "$INSTALL_DIR" "$DATA_DIR" "$ENVIRONMENT_FILE" "$DOTNET_PATH"; do
  [[ $value =~ ^/[a-zA-Z0-9_./:@+-]+$ ]] || fail "Use absolute paths without spaces or systemd specifiers: $value"
done
case "$INSTALL_DIR" in /|/opt|/usr|/usr/local|/etc|/var|/var/lib|/home|/root|/bin|/sbin|/lib|/lib64|/tmp|/srv) fail 'Refusing an unsafe installation root.';; esac
[[ $SERVICE_URL != *[$'\r\n\t %"']* && $SERVICE_URL == http*://* ]] || fail 'Invalid SERVICE_URL.'
[[ $PUBLISH_DIR != "$INSTALL_DIR" && $PUBLISH_DIR != "$INSTALL_DIR/"* && $INSTALL_DIR != "$PUBLISH_DIR/"* ]] || fail 'Source and destination must be separate, non-nested directories.'
if [[ $DATA_DIR == "$INSTALL_DIR" || ( $DATA_DIR == "$INSTALL_DIR/"* && $DATA_DIR != "$INSTALL_DIR/Data" ) || $INSTALL_DIR == "$DATA_DIR/"* ]]; then
  fail 'DATA_DIR must be INSTALL_DIR/Data or a separate non-parent directory.'
fi
unit="/etc/systemd/system/$SERVICE_NAME.service"
if [[ -e $unit ]] && ! grep -q 'IPC Gateway Edge Service' "$unit"; then fail 'An unrelated systemd unit already uses this name.'; fi
loaded_state=$(systemctl show -p LoadState --value "$SERVICE_NAME.service" 2>/dev/null || true)
if [[ -n $loaded_state && $loaded_state != not-found ]]; then
  existing_description=$(systemctl show -p Description --value "$SERVICE_NAME.service" 2>/dev/null || true)
  [[ $existing_description == 'IPC Gateway Edge Service' ]] || fail 'An unrelated loaded service already uses this name.'
fi
was_active=0
if systemctl is-active --quiet "$SERVICE_NAME.service"; then was_active=1; fi
if [[ $was_active == 1 ]]; then systemctl stop "$SERVICE_NAME.service"; fi
if ! getent group "$SERVICE_GROUP" >/dev/null; then groupadd --system "$SERVICE_GROUP"; fi
if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  useradd --system --gid "$SERVICE_GROUP" --home-dir "$DATA_DIR" --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi
usermod -a -G "$SERVICE_GROUP" "$SERVICE_USER"
install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" "$INSTALL_DIR" "$DATA_DIR" "$INSTALL_DIR/logs"
rsync -a --chown="$SERVICE_USER:$SERVICE_GROUP" --delete --exclude='/Data' --exclude='/logs' --exclude='/appsettings.Production.json' -- "$PUBLISH_DIR/" "$INSTALL_DIR/"
if [[ ! -e $INSTALL_DIR/appsettings.Production.json && -f $PUBLISH_DIR/appsettings.Production.json ]]; then
  install -m 640 -o "$SERVICE_USER" -g "$SERVICE_GROUP" "$PUBLISH_DIR/appsettings.Production.json" "$INSTALL_DIR/appsettings.Production.json"
fi
install -d -m 750 "$(dirname "$ENVIRONMENT_FILE")"
if [[ ! -e $ENVIRONMENT_FILE ]]; then
  (umask 077; printf '# Site overrides; see gateway.env.example. Existing appsettings.Production.json remains effective.\n' > "$ENVIRONMENT_FILE")
fi
cat > "$unit" <<UNIT
[Unit]
Description=IPC Gateway Edge Service
Wants=network-online.target
After=network-online.target
StartLimitIntervalSec=1800
StartLimitBurst=3

[Service]
Type=simple
WorkingDirectory=$INSTALL_DIR
ExecStart=$DOTNET_PATH $INSTALL_DIR/IPC.Gateway.WebHost.dll
Restart=on-failure
RestartSec=30
KillSignal=SIGINT
TimeoutStopSec=60
User=$SERVICE_USER
Group=$SERVICE_GROUP
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=$SERVICE_URL
Environment=Gateway__History__Directory=$DATA_DIR/History
Environment=Gateway__Mqtt__OutboxDirectory=$DATA_DIR/MqttOutbox
Environment=Gateway__Maintenance__Updates__UpdateDirectory=$DATA_DIR/Updates
Environment=Gateway__OpcUa__CertificateStorePath=$DATA_DIR/OpcUa/pki
Environment=Gateway__Watchdog__StateDirectory=$DATA_DIR/Watchdog
Environment=Gateway__Inference__ModelDirectory=$DATA_DIR/Models
Environment=Gateway__Scripting__ConfigurationFile=$DATA_DIR/Scripting/config.json
Environment=Gateway__Watchdog__RequestHostStopOnUnrecoverable=true
EnvironmentFile=$ENVIRONMENT_FILE
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ReadWritePaths=$INSTALL_DIR $DATA_DIR

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable "$SERVICE_NAME.service"
if [[ $START_SERVICE == 1 || $was_active == 1 ]]; then
  systemctl restart "$SERVICE_NAME.service"
  systemctl is-active --quiet "$SERVICE_NAME.service" || fail 'Service did not start. Check journalctl.'
fi
echo "Installed $SERVICE_NAME at $INSTALL_DIR; persistent data: $DATA_DIR"
echo "Configure database/credentials in $ENVIRONMENT_FILE before first start. Existing configuration was retained."
echo "Start: sudo systemctl start $SERVICE_NAME; logs: sudo journalctl -u $SERVICE_NAME -f"
