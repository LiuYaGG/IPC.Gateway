# IPC Gateway Release Candidate Checklist

Use this checklist before promoting a WebHost package to a plant-floor gateway. Store command output, publish path, package hash, operator, and timestamp in the release evidence record.

## 1. Source Verification

Run from the repository root:

```powershell
dotnet test .\IPC.Gateway.Tests\IPC.Gateway.Tests.csproj --no-restore -p:UseSharedCompilation=false
dotnet build .\IPC.Gateway.slnx --no-restore -t:Rebuild -p:UseSharedCompilation=false
dotnet list .\IPC.Gateway.slnx package --vulnerable --include-transitive
```

Pass criteria:

- All tests pass.
- Solution build reports `0 warnings` and `0 errors`.
- Vulnerability scan reports no vulnerable direct or transitive packages.

## 2. Frontend Build

Run from `IPC.Gateway.Web`:

```powershell
npm run build
```

Pass criteria:

- Type checking and Vite build complete successfully.
- `IPC.Gateway.Web\dist\index.html` exists.
- The generated assets do not include source maps or secrets unless a release policy explicitly allows them.

## 3. WebHost Publish Package

Run from the repository root after the frontend build:

```powershell
$publishDir = Join-Path $env:TEMP ("ipc-gateway-webhost-" + (Get-Date -Format "yyyyMMddHHmmss"))
dotnet publish .\IPC.Gateway.WebHost\IPC.Gateway.WebHost.csproj -c Release -o $publishDir --no-restore -p:UseSharedCompilation=false
Test-Path (Join-Path $publishDir "IPC.Gateway.WebHost.exe")
Test-Path (Join-Path $publishDir "wwwroot\index.html")
Get-ChildItem (Join-Path $publishDir "wwwroot\assets") -File
```

Pass criteria:

- Publish exits successfully.
- `IPC.Gateway.WebHost.exe` exists.
- `wwwroot\index.html` and bundled frontend assets exist in the publish directory.
- The package does not contain machine-local secrets or development-only config files.

## 4. Local Smoke Run

Run the packaged smoke script from the repository root:

```powershell
.\deployment\smoke-test-webhost.ps1 -PublishDirectory $publishDir
```

Pass criteria:

- Liveness returns HTTP 200.
- API readiness returns structured JSON and the expected health status for the smoke configuration.
- The root page returns HTML from the published `wwwroot`.
- The smoke output reports `DevelopmentConfigPresent=false` and at least one frontend asset.
- When metrics are enabled, an authorized API Token with `runtime.view` can scrape `/metrics` and receives Prometheus text containing `ipc_gateway_runtime_up`.
- Browser smoke follows `deployment/BROWSER_SMOKE_CHECKLIST.md` and verifies login, dashboard readiness, audit view, storage health settings, and the device/rule navigation paths.
- Commercial smoke verifies device template apply, tag CSV export/import on a test device, project backup download, project restore dry-run evidence, license status display, protocol driver signature status, and compatibility matrix rendering.

## 5. Production Promotion Gates

Do not promote when any of these are true:

- Build, tests, publish, vulnerability scan, or smoke run failed.
- `Gateway:Auth:Secret` or `Gateway:Auth:BootstrapAdminPassword` is empty, default, or stored in source control.
- Forwarded headers are enabled without trusted proxy IP or CIDR boundaries.
- Storage health thresholds are not sized for the target gateway disk.
- Reliability thresholds are not sized for the target gateway CPU, memory, and thread-pool baseline.
- Readiness is `Unhealthy` for runtime, configuration, scheduler, storage, system resources, history, MQTT outbox, or rule engine.
- Prometheus/OpenTelemetry metrics are enabled but `/metrics` scraping has not been verified with a production API Token.
- Maintenance support snapshot generation fails for an authorized maintenance user.
- Commercial license public key, signed license file, protocol driver signature policy, and trusted driver public key are missing or still set to development defaults.
- Project backup/restore and tag CSV import/export have not been tested against the target version compatibility matrix.
- Rollback package, database backup, and service recovery steps are not available.

## 6. Rollback Evidence

Before starting the upgrade, record:

- Previous package path and checksum.
- New package path and checksum.
- Database backup location.
- Gateway project backup location and compatibility matrix output.
- Machine-local production configuration location.
- Windows Service name and startup account.
- Operator responsible for rollback approval.
