# IPC Gateway Browser Smoke Checklist

Use this checklist after `deployment/smoke-test-webhost.ps1` has started a published WebHost package with `-KeepRunning` and a disposable bootstrap administrator password.

Example:

```powershell
.\deployment\smoke-test-webhost.ps1 `
  -PublishDirectory $publishDir `
  -BootstrapAdminPassword "replace-with-smoke-only-password" `
  -KeepRunning
```

Record the script output URL and process id in the release evidence. Stop the process after the browser smoke is complete.

## Login

- Open the smoke URL.
- Sign in as the bootstrap administrator, usually `admin`.
- Pass when the app leaves the login screen and shows the left navigation menu.

## Dashboard Readiness

- Open `运行总览`.
- Pass when readiness is visible and is not `Unhealthy` for the smoke configuration.
- Pass when component cards render for gateway, configuration, MQTT, history, history storage, rule engine, and scheduler.

## Storage Threshold Write

- On `运行总览`, change `降级可用空间(MB)` to a disposable value.
- Save the threshold.
- Pass when the save action completes and the history storage component shows the updated threshold.

## Audit Evidence

- Open `审计日志`.
- Pass when a successful `config:storage-health` audit entry is visible for the bootstrap administrator.

## Main Navigation

Open each page and verify the heading renders without browser console errors:

- `设备管理`
- `流程规则`
- `规则引擎`
- `MQTT`
- `项目配置`
- `运行总览`

## Evidence

Capture:

- Smoke URL and process id.
- Browser console error count.
- Readiness status.
- Storage threshold save result.
- Audit row for `config:storage-health`.
- Any screenshots required by the release process.
