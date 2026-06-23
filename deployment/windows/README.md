# Windows Service Deployment

These scripts install the WebHost as a Windows Service for plant-floor gateway machines.

## Publish

Build the frontend before publishing the WebHost so `IPC.Gateway.Web/dist` is copied into the published `wwwroot` folder.

```powershell
Push-Location ..\..\IPC.Gateway.Web
npm ci
npm run build
Pop-Location

dotnet publish ..\..\IPC.Gateway.WebHost\IPC.Gateway.WebHost.csproj -c Release -o C:\IPC.Gateway\app
dotnet publish ..\..\IPC.Gateway.LegacyProtocolPlugins\IPC.Gateway.LegacyProtocolPlugins.csproj -c Release -o C:\IPC.Gateway\app\Drivers /p:UseAppHost=false
```

## Install

Run PowerShell as Administrator:

```powershell
.\install-ipc-gateway-service.ps1 `
  -PublishDirectory C:\IPC.Gateway\app `
  -Url http://127.0.0.1:5184 `
  -DataDirectory C:\IPC.Gateway\data `
  -EnableServiceRecovery `
  -Start
```

The installer sets:

- `ASPNETCORE_ENVIRONMENT=Production`
- `DOTNET_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://127.0.0.1:5184`
- data directories under `C:\IPC.Gateway\data` for history, MQTT outbox, updates, OPC UA certificates, and watchdog state when `-DataDirectory` is provided
- `Gateway__Watchdog__RequestHostStopOnUnrecoverable=true` when `-EnableServiceRecovery` is provided

Keep database credentials, auth secrets, bootstrap password, and forwarded-header trust boundaries in environment variables or a machine-local `appsettings.Production.json`.

Append machine-specific settings with `-Environment`:

```powershell
.\install-ipc-gateway-service.ps1 `
  -PublishDirectory C:\IPC.Gateway\app `
  -Environment "Gateway__Database__Host=127.0.0.1","Gateway__Database__Password=<secret>"
```

## Recovery

Use `-EnableServiceRecovery` on production gateways. It configures Windows Service Control Manager to restart the WebHost after failure and lets the gateway watchdog stop the host when in-process recovery is exhausted. The defaults are:

- restart delay: 30 seconds
- reset failure count: 1 day

Override them with `-RestartDelaySeconds` and `-ResetFailureCountDays`.

## Health Checks

After start:

```powershell
Invoke-RestMethod http://127.0.0.1:5184/health/live
Invoke-RestMethod http://127.0.0.1:5184/health/ready
```

## Remove

Run PowerShell as Administrator:

```powershell
.\remove-ipc-gateway-service.ps1
```
