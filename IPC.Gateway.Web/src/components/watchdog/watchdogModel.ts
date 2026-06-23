import type { GatewayWatchdogConfig } from '../../api'

export function createDefaultWatchdogConfig(): GatewayWatchdogConfig {
  return {
    enabled: true,
    checkIntervalSeconds: 10,
    startupGraceSeconds: 30,
    runtimeNoProgressSeconds: 180,
    recoveryCooldownSeconds: 60,
    recoveryTimeoutSeconds: 30,
    maxRecoveriesPerWindow: 3,
    recoveryWindowMinutes: 10,
    maxHostRestartRequestsPerWindow: 2,
    hostRestartProtectionWindowMinutes: 30,
    requestHostStopOnUnrecoverable: false,
    stateDirectory: 'Data/Watchdog',
    monitorMqtt: true,
    mqttDisconnectedSeconds: 300,
    monitorHistory: true,
    monitorRuleEngine: true,
    monitorOpcUa: true,
    monitorScheduler: true
  }
}

export function normalizeWatchdogConfig(input?: Partial<GatewayWatchdogConfig> | null): GatewayWatchdogConfig {
  const fallback = createDefaultWatchdogConfig()
  return {
    enabled: Boolean(input?.enabled ?? fallback.enabled),
    checkIntervalSeconds: numberOr(input?.checkIntervalSeconds, fallback.checkIntervalSeconds),
    startupGraceSeconds: numberOr(input?.startupGraceSeconds, fallback.startupGraceSeconds),
    runtimeNoProgressSeconds: numberOr(input?.runtimeNoProgressSeconds, fallback.runtimeNoProgressSeconds),
    recoveryCooldownSeconds: numberOr(input?.recoveryCooldownSeconds, fallback.recoveryCooldownSeconds),
    recoveryTimeoutSeconds: numberOr(input?.recoveryTimeoutSeconds, fallback.recoveryTimeoutSeconds),
    maxRecoveriesPerWindow: numberOr(input?.maxRecoveriesPerWindow, fallback.maxRecoveriesPerWindow),
    recoveryWindowMinutes: numberOr(input?.recoveryWindowMinutes, fallback.recoveryWindowMinutes),
    maxHostRestartRequestsPerWindow: numberOr(input?.maxHostRestartRequestsPerWindow, fallback.maxHostRestartRequestsPerWindow),
    hostRestartProtectionWindowMinutes: numberOr(input?.hostRestartProtectionWindowMinutes, fallback.hostRestartProtectionWindowMinutes),
    requestHostStopOnUnrecoverable: Boolean(input?.requestHostStopOnUnrecoverable ?? fallback.requestHostStopOnUnrecoverable),
    stateDirectory: String(input?.stateDirectory || fallback.stateDirectory),
    monitorMqtt: Boolean(input?.monitorMqtt ?? fallback.monitorMqtt),
    mqttDisconnectedSeconds: numberOr(input?.mqttDisconnectedSeconds, fallback.mqttDisconnectedSeconds),
    monitorHistory: Boolean(input?.monitorHistory ?? fallback.monitorHistory),
    monitorRuleEngine: Boolean(input?.monitorRuleEngine ?? fallback.monitorRuleEngine),
    monitorOpcUa: Boolean(input?.monitorOpcUa ?? fallback.monitorOpcUa),
    monitorScheduler: Boolean(input?.monitorScheduler ?? fallback.monitorScheduler)
  }
}

export function validateWatchdogConfig(config: GatewayWatchdogConfig) {
  if (!config.stateDirectory.trim()) return '请填写看门狗状态目录'
  if (config.checkIntervalSeconds < 1 || config.checkIntervalSeconds > 3600) return '检查周期必须在 1 到 3600 秒之间'
  if (config.startupGraceSeconds < 0 || config.startupGraceSeconds > 3600) return '启动宽限必须在 0 到 3600 秒之间'
  if (config.runtimeNoProgressSeconds < 30 || config.runtimeNoProgressSeconds > 86400) return '无进展阈值必须在 30 到 86400 秒之间'
  if (config.recoveryCooldownSeconds < 1 || config.recoveryCooldownSeconds > 86400) return '恢复冷却必须在 1 到 86400 秒之间'
  if (config.recoveryTimeoutSeconds < 5 || config.recoveryTimeoutSeconds > 3600) return '恢复超时必须在 5 到 3600 秒之间'
  if (config.maxRecoveriesPerWindow < 1 || config.maxRecoveriesPerWindow > 100) return '恢复窗口内最大次数必须在 1 到 100 之间'
  if (config.recoveryWindowMinutes < 1 || config.recoveryWindowMinutes > 1440) return '恢复保护窗口必须在 1 到 1440 分钟之间'
  if (config.maxHostRestartRequestsPerWindow < 0 || config.maxHostRestartRequestsPerWindow > 100) return '宿主重启窗口内最大次数必须在 0 到 100 之间'
  if (config.hostRestartProtectionWindowMinutes < 1 || config.hostRestartProtectionWindowMinutes > 1440) return '宿主重启保护窗口必须在 1 到 1440 分钟之间'
  if (config.mqttDisconnectedSeconds < 30 || config.mqttDisconnectedSeconds > 86400) return 'MQTT 断连阈值必须在 30 到 86400 秒之间'
  return ''
}

function numberOr(value: unknown, fallback: number) {
  const next = Number(value)
  return Number.isFinite(next) ? next : fallback
}
