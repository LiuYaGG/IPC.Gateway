// Only presentation text is localized. Parameters such as topics, endpoints and
// counts are copied verbatim; backend diagnostics and stored events are untouched.
export const runtimeMessages: ReadonlyArray<readonly [string, string]> = [
  ['FINS 数据格式必须为 ABCD、BADC、CDAB 或 DCBA。', 'FINS data format must be ABCD, BADC, CDAB, or DCBA.'],
  ['连续无变化超时的数值处理节点需要数值输入。', 'Numeric processing before the unchanged timeout requires numeric input.'],
  ['连续无变化超时参数无效：超时和数据最大间隔必须大于 0，变化死区必须为非负有限数。', 'Invalid unchanged timeout settings: timeout and sample gap must be positive, and deadband must be finite and non-negative.'],
  ['Modbus 数据格式必须为 ABCD、BADC、CDAB 或 DCBA。', 'Modbus data format must be ABCD, BADC, CDAB, or DCBA.'],
  ['Modbus UDP 响应超时（{0} ms）。', 'Modbus UDP response timed out after {0} ms.'],
  ['Modbus UDP 异常：功能码 0x{0}，异常码 0x{1}。', 'Modbus UDP exception: function 0x{0}, code 0x{1}.'],
  ['Modbus UDP 报文头或数据报长度无效。', 'Invalid Modbus UDP MBAP header or datagram length.'],
  ['Modbus UDP 异常响应长度无效。', 'Invalid Modbus UDP exception response length.'],
  ['Modbus UDP 响应功能码与请求不匹配。', 'Modbus UDP response function does not match the request.'],
  ['Modbus UDP 读取响应字节数与请求不匹配。', 'Modbus UDP read response byte count does not match the request.'],
  ['Modbus UDP 写入响应的地址或值/数量与请求不匹配。', 'Modbus UDP write response does not echo the requested address and value/count.'],
  ['Protobuf 项目文件或格式版本不受支持。', 'Unsupported Protobuf project file or format version.'],
  ['Protobuf 项目备份或格式版本不受支持。', 'Unsupported Protobuf project backup or format version.'],
  ['Protobuf 项目备份内容不完整。', 'The Protobuf project backup is incomplete.'],
  ['Protobuf 项目数据为空。', 'The Protobuf project payload is empty.'],
  ['Protobuf 项目数据格式无效。', 'Invalid Protobuf project payload.'],
  ['缺少 Protobuf 项目配置。', 'Protobuf project configuration is missing.'],
  ['MQTT 熔断器已开启，已降级为仅缓存待发送消息模式。', 'MQTT circuit breaker is open; degraded to outbox-only mode.'],
  ['历史库熔断器已开启，已跳过写入。', 'History circuit breaker is open; write skipped.'],
  ['协议驱动熔断器已开启，设备采集已降级并跳过。', 'Protocol driver circuit breaker is open; device poll is degraded and skipped.'],
  ['调度器状态不可用。', 'Scheduler status is unavailable.'],
  ['调度队列已满，且没有可用的采集工作线程。', 'Scheduler queue is full and no polling workers are available.'],
  ['采集调度器正在正常接收采集任务。', 'Scheduler is accepting polling work.'],
  ['网关运行时正常。', 'Gateway runtime is healthy.'],
  ['网关运行时未运行。', 'Gateway runtime is not running.'],
  ['采集调度正常。', 'Polling scheduler is healthy.'],
  ['MQTT 未启用。', 'MQTT is disabled.'],
  ['MQTT 连接正常。', 'MQTT connection is healthy.'],
  ['MQTT 当前未连接。', 'MQTT is currently disconnected.'],
  ['历史库未启用。', 'History storage is disabled.'],
  ['历史库运行正常。', 'History storage is running normally.'],
  ['历史库已启用但未运行。', 'History storage is enabled but not running.'],
  ['规则引擎未启用。', 'Rule engine is disabled.'],
  ['规则引擎运行正常。', 'Rule engine is running normally.'],
  ['规则引擎已启用但未运行。', 'Rule engine is enabled but not running.'],
  ['OPC UA Server 未启用。', 'OPC UA Server is disabled.'],
  ['OPC UA Server 运行正常。', 'OPC UA Server is running normally.'],
  ['OPC UA Server 已启用但未运行。', 'OPC UA Server is enabled but not running.'],
  ['看门狗未启用。', 'Watchdog is disabled.'],
  ['上一次恢复仍在执行，本次恢复已跳过。', 'The previous recovery is still running; this recovery was skipped.'],
  ['上一次恢复仍在执行，已阻止重叠恢复。', 'The previous recovery is still running; overlapping recovery was blocked.'],
  ['恢复动作被冷却时间或恢复风暴保护拦截。', 'Recovery was blocked by the cooldown or recovery storm protection.'],
  ['恢复等待已超时，但原恢复任务仍在执行；完成前不会启动新的恢复。', 'Recovery wait timed out, but the original recovery is still running; no new recovery will start until it finishes.'],
  ['异常重启保护已拦截宿主重启请求。', 'Restart protection blocked the host restart request.'],
  ['已请求宿主停止，交给外部服务管理器重启。', 'Host shutdown was requested; the external service manager will restart it.'],
  ['采集调度疑似卡住，{0} 秒没有完成任务。', 'Polling scheduler may be stalled; no task has completed for {0} seconds.'],
  ['采集队列正在执行背压控制（{0}/{1}）', 'polling queue backpressure is active ({0}/{1})'],
  ['采集队列接近容量上限（{0}/{1}）', 'polling queue is near capacity ({0}/{1})'],
  ['所有采集工作线程均忙碌，尚有 {0} 个任务等待处理', 'all polling workers are busy with {0} task(s) pending'],
  ['最近 {1} 分钟发生 {0} 次采集超时', '{0} poll timeout(s) occurred in the last {1} minute(s)'],
  ['最近 {1} 秒发生 {0} 次采集超时', '{0} poll timeout(s) occurred in the last {1} second(s)'],
  ['最近 {1} 分钟发生 {0} 次标签读取超时', '{0} tag read timeout(s) occurred in the last {1} minute(s)'],
  ['最近 {1} 秒发生 {0} 次标签读取超时', '{0} tag read timeout(s) occurred in the last {1} second(s)'],
  ['连接被拒绝。', 'Connection refused.'],
  ['连接超时。', 'Connection timed out.'],
  ['操作已取消。', 'The operation was canceled.'],
  ['任务已取消。', 'A task was canceled.'],
  ['操作已超时。', 'The operation has timed out.'],
  ['连接已关闭。', 'Connection is closed.'],
  ['客户端未连接。', 'Client is not connected.'],
  ['驱动缺少签名，已被强制验签策略禁用。', 'Driver signature is required but missing.'],
  ['驱动存在签名，但尚未配置可信公钥。', 'Driver signature is present but no trusted public key is configured.'],
  ['驱动签名验证失败。', 'Driver signature verification failed.'],
  ['驱动文件摘要与清单不一致。', 'Driver assembly SHA256 does not match the manifest.'],
  ['MQTT Broker 连接超时。', 'MQTT Broker connection timed out.'],
  ['规则熔断器已开启，已跳过规则计算。', 'Rule circuit breaker is Open; evaluation skipped.'],
  ['规则熔断器处于半开状态，已跳过规则计算。', 'Rule circuit breaker is HalfOpen; evaluation skipped.'],
]

type Compiled = { zh: string; en: string; patterns: { regex: RegExp; ids: number[] }[] }
function compile(template: string) {
  const ids: number[] = []
  const escaped = template.split(/(\{\d+\})/).map(part => {
    const parameter = /^\{(\d+)\}$/.exec(part)
    if (parameter) { ids.push(Number(parameter[1])); return '([\\s\\S]+?)' }
    return part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  }).join('')
  return { regex: new RegExp(`^${escaped}$`), ids }
}
const messages: Compiled[] = runtimeMessages.map(([zh, en]) => ({ zh, en, patterns: [compile(zh), compile(en)] }))
function render(template: string, values: string[]) { return template.replace(/\{(\d+)\}/g, (_, n) => values[Number(n)] ?? '') }

const runtimeCache = new Map<string, string | undefined>()
export function translateRuntimeMessage(text: string, language: 'zh-CN' | 'en-US', depth = 0): string | undefined {
  if (depth > 0) return translateRuntimeMessageInternal(text, language, depth)
  const key = `${language}\n${text}`
  if (runtimeCache.has(key)) return runtimeCache.get(key)
  const result = translateRuntimeMessageInternal(text, language, depth)
  if (runtimeCache.size >= 2000) runtimeCache.delete(runtimeCache.keys().next().value!)
  runtimeCache.set(key, result)
  return result
}

function translateRuntimeMessageInternal(text: string, language: 'zh-CN' | 'en-US', depth: number): string | undefined {
  if (depth > 4) return undefined
  const input = text.trim()
  for (const item of messages) for (const pattern of item.patterns) {
    const match = pattern.regex.exec(input)
    if (match) {
      const values: string[] = []
      pattern.ids.forEach((id, index) => { values[id] = match[index + 1] })
      return render(language === 'zh-CN' ? item.zh : item.en, values)
    }
  }
  // Structured prefixes contain diagnostics or topics, never arbitrary UI prose.
  const prefixes: [string, string, boolean][] = [
    ['看门狗读取运行状态失败：', 'Watchdog failed to read runtime status: ', true],
    ['MQTT Broker 连接失败：', 'MQTT Broker connection failed: ', true],
    ['失败：', 'Failed: ', true], ['已排队：', 'Queued: ', false], ['发布成功：', 'OK: ', false],
  ]
  for (const [zh, en, nested] of prefixes) for (const prefix of [zh, en]) {
    if (input.startsWith(prefix)) {
      const detail = input.slice(prefix.length)
      return (language === 'zh-CN' ? zh : en) + (nested ? translateRuntimeMessage(detail, language, depth + 1) ?? detail : detail)
    }
  }
  const breaker = /^(Protocol driver circuit breaker is open; device poll is degraded and skipped\.|协议驱动熔断器已开启，设备采集已降级并跳过。)(?: Last error: | 最近错误：)([\s\S]+)$/.exec(input)
  if (breaker) return (translateRuntimeMessage(breaker[1], language, depth + 1) ?? breaker[1]) +
    (language === 'zh-CN' ? ' 最近错误：' : ' Last error: ') + (translateRuntimeMessage(breaker[2], language, depth + 1) ?? breaker[2])
  // Scheduler health can contain multiple reasons separated by semicolons.
  const body = input.replace(/[.。]$/, '')
  const parts = body.split(/; |；/)
  if (parts.length > 1 || body !== input) {
    const translated = parts.map(part => translateRuntimeMessage(part, language, depth + 1))
    if (translated.some(part => part !== undefined)) return translated.map((part, i) => part ?? parts[i]).join(language === 'zh-CN' ? '；' : '; ') +
      (body !== input ? language === 'zh-CN' ? '。' : '.' : '')
  }
  return undefined
}

export function watchdogCheckLabel(name: string) {
  const labels: Record<string, string> = { runtime: '网关运行时', scheduler: '采集调度器', mqtt: 'MQTT', history: '历史库', ruleEngine: '规则引擎', opcUa: 'OPC UA Server', watchdog: '看门狗' }
  return labels[name] ?? name
}
