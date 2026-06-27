# IPC Gateway Deployment Notes

Use these examples as a production starting point for an industrial edge gateway deployment. Keep secrets outside source control and inject them through environment variables, a deployment secret store, or a host-specific configuration file.

## Required Production Overrides

- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://127.0.0.1:5184`
- `Gateway__Database__Host`
- `Gateway__Database__Database`
- `Gateway__Database__Username`
- `Gateway__Database__Password`
- `Gateway__Auth__Secret`
- `Gateway__Auth__BootstrapAdminPassword`
- `Gateway__ForwardedHeaders__Enabled=true` when TLS is terminated before Kestrel
- `Gateway__ForwardedHeaders__KnownProxies__0=127.0.0.1` for same-host Nginx, or `Gateway__ForwardedHeaders__KnownNetworks__0=<trusted-cidr>` for a proxy subnet

## Reverse Proxy

`deployment/nginx/ipc-gateway.conf.example` terminates HTTPS and forwards the headers required by the WebHost. The gateway only honors those headers when the proxy IP or CIDR is explicitly trusted.

## Deployment Modes

- Windows Service: `deployment/windows` contains install and remove scripts, service environment setup, and optional Service Control Manager recovery.
- Linux systemd: `deployment/linux` contains a hardened unit template and installer for `/opt/ipc-gateway/app`.
- Docker: `Dockerfile` and `deployment/docker/docker-compose.yml` build the WebHost with the Vue frontend and run it with PostgreSQL.

Build the frontend before `dotnet publish`; the WebHost project copies `IPC.Gateway.Web/dist` into the published `wwwroot` folder.
When publishing manually, publish `IPC.Gateway.LegacyProtocolPlugins` into the WebHost `Drivers` folder so migrated PLC protocol drivers are available after deployment.

## Install And Upgrade Packages

Use `deployment/package/build-ipc-gateway-package.ps1` to create an offline package:

```powershell
deployment/package/build-ipc-gateway-package.ps1 -PackageType Install -Version 1.0.0
deployment/package/build-ipc-gateway-package.ps1 -PackageType Upgrade -Version 1.0.1
deployment/package/build-ipc-gateway-package.ps1 -PackageType Upgrade -Version 1.0.2 -SigningPrivateKeyPath C:\secure\update-signing-private.pem -Signer "IPC Release"
```

Each package is a zip containing `ipc-gateway-package.json`, `payload/`, and deployment tools. The manifest lists every archive file with SHA256 and size; the WebHost rejects missing files, extra files, digest mismatches, invalid paths, and signature failures before staging. Upload upgrade packages from the Web console under `安装升级`. The WebHost validates and stages the package, creates a rollback point, and writes `Data/Updates/pending-action.json` plus `Data/Updates/apply-pending-update.ps1`.

For commercial delivery, keep the signing private key only in CI/release custody and deploy the public key to the gateway, for example:

```json
"Gateway": {
  "Maintenance": {
    "Updates": {
      "RequirePackageFileDigests": true,
      "RequirePackageSignature": true,
      "TrustedPackagePublicKeyPath": "security/update-signing-public.pem"
    }
  }
}
```

Run the generated script during a maintenance window, or use `deployment/windows/apply-ipc-gateway-offline-update.ps1 -PendingActionPath <Data/Updates/pending-action.json>`.

The package includes Windows Service and Linux systemd deployment tools under `tools/`.

## Release Checks

Run the full release candidate checklist in `deployment/RELEASE_CHECKLIST.md` before promoting a gateway package. At minimum, capture evidence for:

- `dotnet test IPC.Gateway.Tests/IPC.Gateway.Tests.csproj --no-restore`
- `dotnet build IPC.Gateway.slnx --no-restore`
- `dotnet list IPC.Gateway.slnx package --vulnerable --include-transitive`
- `npm run build` from `IPC.Gateway.Web`
- `dotnet publish IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj -c Release` with `wwwroot/index.html` present in the publish directory
- `deployment/smoke-test-webhost.ps1 -PublishDirectory <publish-dir>` for local liveness/readiness/root-page smoke checks against the published package
- `deployment/BROWSER_SMOKE_CHECKLIST.md` for browser login, readiness, audit, storage threshold, and navigation checks

## Readiness Probes

- Liveness: `GET /health/live`
- Readiness: `GET /health/ready`
- API readiness: `GET /api/health/ready`

Use readiness, not liveness, for traffic admission. Readiness includes runtime, scheduler, MQTT outbox, storage, history, rule engine, system resources, and configuration health.

Site reliability thresholds are configured under `Gateway:Reliability`. For production deployments, tune these values with environment variables such as `Gateway__Reliability__DegradedCpuUsagePercent`, `Gateway__Reliability__UnhealthyCpuUsagePercent`, `Gateway__Reliability__DegradedMemoryUsagePercent`, `Gateway__Reliability__UnhealthyMemoryUsagePercent`, `Gateway__Reliability__DegradedAvailableThreadPoolWorkers`, and `Gateway__Reliability__UnhealthyAvailableThreadPoolWorkers`.

## Metrics

The WebHost registers OpenTelemetry-compatible .NET metrics through the `IPC.Gateway` Meter and exposes Prometheus text at `GET /metrics` when `Gateway:Observability:MetricsEnabled=true`.

Prometheus should scrape `/metrics` with an API Token that has `runtime.view` permission, using either `Authorization: Bearer <token>` or the configured `Gateway:Security:ApiTokens:HeaderName`. A starting scrape config is available at `deployment/prometheus/ipc-gateway-prometheus.yml`.

The exported metrics include runtime up state, device/tag quality counts, MQTT connection and outbox state, scheduler queue and timeout counters, CPU and memory pressure, history storage size, rule-engine state, and OPC UA server state.

## Commercial Operations

The WebHost exposes commercial plant-floor operations under `/api/commercial`:

- Device templates: list built-in PLC templates and apply them to create a device, group, and starter tags.
- Tag bulk operations: export and import device tags as CSV for commissioning worksheets.
- License status: report signed offline license validity, expiry, feature flags, and capacity limits.
- Protocol drivers: report loaded drivers, manifest compatibility, assembly SHA256, signer, and signature trust status.
- Project backup/restore: export a versioned JSON backup and restore project, MQTT, OPC UA, history, and storage-health settings.
- Compatibility matrix: report gateway, WebHost, backup schema, project schema, plugin manifest, metrics, and driver compatibility.

For commercial production, configure signed license and protocol driver trust settings outside source control:

```json
"Gateway": {
  "License": {
    "ProductId": "IPC.Gateway",
    "LicenseFile": "Data/License/ipc-gateway-license.json",
    "TrustedPublicKeyPem": "-----BEGIN PUBLIC KEY-----...-----END PUBLIC KEY-----",
    "RequireValidLicense": true
  },
  "ProtocolDrivers": {
    "RequireSignature": true,
    "TrustedPublicKeyPem": "-----BEGIN PUBLIC KEY-----...-----END PUBLIC KEY-----"
  }
}
```

Protocol driver manifests can be signed during release or partner certification:

```powershell
.\scripts\sign-protocol-driver.ps1 -AssemblyPath .\Drivers\Partner.Driver.dll -SigningPrivateKeyPath C:\secure\driver-signing-private.pem -Signer "IPC Partner"
```

## Watchdog

The WebHost starts `IPC.Gateway.Watchdog` as a separate hosted service. It monitors runtime liveness, scheduler progress, MQTT, history, rule engine, and OPC UA Server status. Runtime stalls trigger an in-process gateway restart first. Repeated recovery attempts are throttled by `Gateway:Watchdog` restart-protection settings and persisted under `Data/Watchdog`.

For production Windows Service, systemd, and Docker deployments, keep `Gateway:Watchdog:RequestHostStopOnUnrecoverable=true` only when the external supervisor is configured to restart the service and has its own restart limits.

## Support Snapshots

Use `GET /api/maintenance/support/snapshot` or the Maintenance page support snapshot button when opening an after-sales support case. The snapshot is a compact JSON summary of runtime health, component status, recent runtime errors, update state, watchdog state, and recommended next actions. It requires `maintenance.view`; recent audit details are included only when the caller also has `audit.view`.
