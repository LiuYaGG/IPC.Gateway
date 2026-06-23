# Linux systemd Deployment

Use systemd when the gateway runs directly on an industrial Linux host.

## Publish

Build the frontend first, then publish the WebHost:

```bash
cd IPC.Gateway.Web
npm ci
npm run build
cd ..
dotnet publish IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj -c Release -o ./artifacts/publish/ipc-gateway
dotnet publish IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj -c Release -o ./artifacts/publish/ipc-gateway/Drivers /p:UseAppHost=false
```

## Install

```bash
sudo PUBLISH_DIR=/absolute/path/to/artifacts/publish/ipc-gateway \
  SERVICE_URL=http://127.0.0.1:5184 \
  START_SERVICE=1 \
  deployment/linux/install-ipc-gateway-systemd.sh
```

The installer creates or updates:

- `/opt/ipc-gateway/app`
- `/etc/systemd/system/ipc-gateway.service`
- Linux service user `ipc-gateway`

## Operations

```bash
sudo systemctl status ipc-gateway
sudo journalctl -u ipc-gateway -f
sudo systemctl restart ipc-gateway
sudo systemctl stop ipc-gateway
```

Use `GET /health/ready` for readiness checks. The watchdog can request host shutdown when repeated self-recovery fails; systemd then restarts the service with its own restart limits.
