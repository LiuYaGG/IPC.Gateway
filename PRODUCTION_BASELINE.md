# IPC Gateway Production Baseline

This document tracks the commercial-readiness baseline for the gateway. Keep it current as each automation node lands.

## Current Verification

- Backend tests: `dotnet test IPC.Gateway.slnx --no-restore`
- Frontend build: `npm run build` from `IPC.Gateway.Web`
- Vulnerability scan: `dotnet list IPC.Gateway.slnx package --vulnerable --include-transitive`

## Security Baseline

- `Gateway:Auth:Secret` must be set to a non-default value of at least 32 characters outside `Development`.
- `Gateway:Auth:BootstrapAdminPassword` must be set before the first administrator account can be created.
- The production `appsettings.json` no longer contains a default database password or default administrator password.
- The login page does not prefill default credentials.
- Gateway user passwords are stored with random salts and PBKDF2-SHA256 hashes, and authentication responses never return password hash or salt material.
- Authentication cookies are `HttpOnly`, `SameSite=Strict`, and marked `Secure` when the request uses HTTPS.
- When TLS is terminated by a reverse proxy, `Gateway:ForwardedHeaders:Enabled` must be enabled and `KnownProxies` or `KnownNetworks` must list the trusted proxy boundary so the gateway can safely honor `X-Forwarded-Proto` and `X-Forwarded-For`.
- Forwarded headers startup configuration fails closed when enabled without an explicit trusted proxy IP or CIDR.
- Web responses set security headers to block MIME sniffing, iframe embedding, referrer leakage, and API response caching.
- Scheduler health is classified as `Healthy`, `Degraded`, or `Unhealthy` from queue pressure, rejected poll tasks, slow polls, and timeout counters.
- Device reconnect backoff is calculated with overflow-safe exponential growth, capped by device retry policy, spread with deterministic jitter, and exposed in runtime status for operations visibility.
- Device connection failures and recoveries are recorded in the runtime error timeline and persisted through the runtime state cache.
- MQTT outbox status exposes oldest pending age, invalid cache files, quarantined corrupt files, quarantine retention cleanup, consecutive publish failures, and next retry time for outage recovery visibility.
- Readiness checks include MQTT outbox and local history storage watermarks, with configurable `Gateway:StorageHealth` thresholds. Low free space is reported as `Degraded`; critically low free space is reported as `Unhealthy`.
- Readiness checks include configurable `Gateway:Reliability` CPU, memory, and thread-pool thresholds so site resource pressure can degrade or fail traffic admission before runtime work stalls.
- The operations dashboard consumes `/api/health/ready` and shows component-level readiness for gateway runtime, configuration, MQTT, storage, history, rule engine, scheduler, and system resources.
- Maintenance users can generate a compact support snapshot from `/api/maintenance/support/snapshot`, covering runtime summary, component health, recent runtime errors, update state, watchdog state, recommendations, and permission-gated audit detail.
- The WebHost registers OpenTelemetry-compatible .NET metrics under the `IPC.Gateway` Meter and exposes a Prometheus scrape endpoint at `/metrics` for runtime, device, tag, MQTT, scheduler, system-resource, history, rule-engine, and OPC UA status.
- Commercial operations expose device templates, tag CSV import/export, license status, protocol driver trust state, project backup/restore, and a version compatibility matrix under `/api/commercial`.
- Protocol driver manifests carry assembly SHA256 and RSA-SHA256 signature metadata; production deployments can require trusted signatures through `Gateway:ProtocolDrivers`.
- Commercial license validation supports signed offline license payloads through `Gateway:License`, with production examples requiring a valid trusted license.
- Storage health thresholds are editable from the operations dashboard through `/api/config/storage-health` and are stored as versioned gateway configuration.
- Published WebHost packages include the built frontend under `wwwroot`, while development runs can still serve `IPC.Gateway.Web/dist` from the source tree.
- Release WebHost publish packages exclude `appsettings.Development.json` so development-only passwords and auth secrets are not shipped with plant-floor packages.
- Windows Service deployment scripts install the WebHost with production environment variables and a local Kestrel binding suitable for reverse proxy termination.
- The packaged WebHost smoke script starts a published package with disposable SQLite storage and generated secrets, then verifies liveness, API readiness JSON, root HTML, frontend assets, and absence of development config.
- The browser smoke checklist covers published-package login, dashboard readiness, storage threshold writes, audit evidence, main navigation, and browser console errors.
- The release candidate checklist verifies backend tests, zero-warning solution builds, frontend assets, WebHost publish contents, local health smoke checks, promotion gates, and rollback evidence before plant-floor deployment.
- Tag value snapshots, tag read/write API contracts, PLC connection options, and the lightweight MQTT client now model default/null lifecycle state explicitly instead of relying on implicit nulls.
- Modbus TCP connection state, value formatting, tag scaling, and virtual PLC read defaults now preserve null semantics explicitly and avoid implicit null writes into runtime snapshots.
- Virtual PLC string and boolean conversions now return deterministic non-null values for empty reads, string writes, boolean aliases, and blank boolean inputs.
- Runtime tag-change events, rule runtime events, local history handlers, and MQTT publishing handlers now treat event senders as optional while keeping event snapshots non-null.
- Built-in DLT645-2007 and CJ/T188-2004 metering clients now model TCP and stream lifecycle state explicitly and clean up failed connection attempts before exposing connected state.
- PLC driver plugin registration and load-context paths now model missing drivers, duplicate sessions, reflection load gaps, and unloadable contexts explicitly instead of relying on implicit nulls.
- PLC driver plugin manifest version parsing and core log exception-chain rendering now handle null values explicitly, keeping Core builds free of nullable warnings.
- RuntimeEngine now models stopped-state configuration, timer, client, snapshot lookup, and poll error lifecycle explicitly so scheduler and write paths do not rely on implicit nulls.
- Project configuration cloning and JSON persistence now model missing child objects, deserialization gaps, and directory resolution explicitly while normalizing loaded gateway projects into runnable defaults.
- Flow rule compilation now treats missing flow definitions, null graph nodes, null graph edges, optional source nodes, and optional transform/action nodes explicitly before producing runtime edge-rule projections.
- Edge rule webhook actions use reusable `HttpClient` requests with per-call timeout cancellation, templated headers and body content, and explicit HTTP 4xx/5xx action failure reporting.
- Edge rule runtime status and action dispatch now ignore null rule/action entries while keeping rule state lookup and status aggregation non-null.
- MQTT outbox parsing, publish results, and gateway MQTT option persistence now normalize missing fields, reject malformed cached payloads without throwing, and keep retry/outbox state non-null across reloads.
- Gateway core configuration commands and runtime status aggregation now model missing devices, groups, tags, rules, flow rules, deserialization gaps, and absent runtime snapshots explicitly before mutating project state.
- Readiness contract tests verify that unhealthy or failed readiness checks still return structured JSON with HTTP 503 for operations dashboards and probes.

## Mitigated Supply Chain Risk

- `SqlSugarCore` still pulls `Microsoft.Data.Sqlite`, but the gateway pins `SQLitePCLRaw.bundle_e_sqlite3 3.0.3` in `IPC.Gateway.Core`.
- NuGet now resolves SQLite native support through `SQLitePCLRaw.core 3.0.3`, `SQLitePCLRaw.provider.e_sqlite3 3.0.3`, and `SourceGear.sqlite3 3.50.4.5`.
- `dotnet list IPC.Gateway.slnx package --vulnerable --include-transitive` reports no vulnerable packages in the current sources.

## Commercial Readiness Checklist

- Security: no production default secrets, no default administrator password, role checks on configuration writes, and baseline response security headers.
- Operations: health endpoints expose live and ready states, Prometheus/OpenTelemetry metrics expose machine-scrapable runtime signals, the dashboard surfaces component-level runtime, scheduler, MQTT outbox, storage, and resource-pressure status, and maintenance support snapshots provide first-response evidence for after-sales support.
- Reliability: scheduler timeout, queue backpressure, device reconnect backoff, MQTT outbox, local history, CPU pressure, memory pressure, and thread-pool pressure are visible in runtime/readiness status.
- Readiness: `/health/ready` degrades or fails when scheduler pressure, runtime state, storage watermarks, system resource pressure, or health collection failures indicate the gateway is no longer ready.
- Commercial capability: device templates, tag CSV import/export, signed protocol drivers, signed license authorization, project backup/restore, and compatibility matrix checks must be smoke-tested before delivery.
- Deployment: production configuration must be supplied via environment variables or deployment-specific config, including storage watermarks sized for the target gateway disk and trusted forwarded-header proxy settings when deployed behind TLS termination. `IPC.Gateway.WebHost/appsettings.Production.example.json`, `deployment/nginx/ipc-gateway.conf.example`, and `deployment/windows` provide the current production starting point.
- Verification: backend tests, frontend build, and dependency audit must run before each release candidate.
