#!/usr/bin/env bash
set -euo pipefail

SERVICE_NAME="${SERVICE_NAME:-ipc-gateway}"
SERVICE_USER="${SERVICE_USER:-ipc-gateway}"
SERVICE_GROUP="${SERVICE_GROUP:-ipc-gateway}"
INSTALL_DIR="${INSTALL_DIR:-/opt/ipc-gateway/app}"
SERVICE_URL="${SERVICE_URL:-http://127.0.0.1:5184}"
START_SERVICE="${START_SERVICE:-0}"
PUBLISH_DIR="${1:-${PUBLISH_DIR:-}}"

if [ -z "$PUBLISH_DIR" ]; then
  echo "Usage: sudo PUBLISH_DIR=/path/to/publish $0"
  echo "   or: sudo $0 /path/to/publish"
  exit 2
fi

if [ "$(id -u)" -ne 0 ]; then
  echo "This installer must run as root because it writes /opt and systemd unit files."
  exit 1
fi

if [ ! -f "$PUBLISH_DIR/IPC.Gateway.WebHost.dll" ]; then
  echo "IPC.Gateway.WebHost.dll was not found in '$PUBLISH_DIR'. Publish IPC.Gateway.WebHost first."
  exit 1
fi

NOLOGIN_SHELL="/usr/sbin/nologin"
if [ ! -x "$NOLOGIN_SHELL" ]; then
  NOLOGIN_SHELL="/sbin/nologin"
fi
if [ ! -x "$NOLOGIN_SHELL" ]; then
  NOLOGIN_SHELL="/bin/false"
fi

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  useradd --system --home-dir /var/lib/ipc-gateway --create-home --shell "$NOLOGIN_SHELL" "$SERVICE_USER"
fi

if ! getent group "$SERVICE_GROUP" >/dev/null 2>&1; then
  groupadd --system "$SERVICE_GROUP"
fi

usermod -a -G "$SERVICE_GROUP" "$SERVICE_USER" >/dev/null 2>&1 || true

install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" "$INSTALL_DIR"
install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" "$INSTALL_DIR/Data"
install -d -o "$SERVICE_USER" -g "$SERVICE_GROUP" "$INSTALL_DIR/logs"

if command -v rsync >/dev/null 2>&1; then
  rsync -a --delete \
    --exclude "Data" \
    --exclude "logs" \
    --exclude "appsettings.Production.json" \
    "$PUBLISH_DIR"/ "$INSTALL_DIR"/
else
  find "$INSTALL_DIR" -mindepth 1 \
    ! -path "$INSTALL_DIR/Data" \
    ! -path "$INSTALL_DIR/Data/*" \
    ! -path "$INSTALL_DIR/logs" \
    ! -path "$INSTALL_DIR/logs/*" \
    ! -name "appsettings.Production.json" \
    -exec rm -rf {} +
  cp -a "$PUBLISH_DIR"/. "$INSTALL_DIR"/
fi

chown -R "$SERVICE_USER:$SERVICE_GROUP" "$INSTALL_DIR"

cat > "/etc/systemd/system/$SERVICE_NAME.service" <<UNIT
[Unit]
Description=IPC Gateway Edge Service
Wants=network-online.target
After=network-online.target

[Service]
Type=simple
WorkingDirectory=$INSTALL_DIR
ExecStart=/usr/bin/dotnet $INSTALL_DIR/IPC.Gateway.WebHost.dll
Restart=on-failure
RestartSec=30
StartLimitIntervalSec=1800
StartLimitBurst=3
KillSignal=SIGINT
TimeoutStopSec=30
User=$SERVICE_USER
Group=$SERVICE_GROUP
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=$SERVICE_URL
Environment=Gateway__Watchdog__RequestHostStopOnUnrecoverable=true
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ReadWritePaths=$INSTALL_DIR $INSTALL_DIR/Data $INSTALL_DIR/logs

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable "$SERVICE_NAME.service"

if [ "$START_SERVICE" = "1" ]; then
  systemctl restart "$SERVICE_NAME.service"
fi

echo "systemd service '$SERVICE_NAME' installed for $INSTALL_DIR"
echo "Start it with: sudo systemctl start $SERVICE_NAME"
echo "Check it with: sudo systemctl status $SERVICE_NAME"
