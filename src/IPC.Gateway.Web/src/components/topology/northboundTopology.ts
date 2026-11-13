import type { GatewayStatus, MqttRuntimeStatus, OpcUaServerRuntimeStatus } from '../../api'
import { tx } from '../../i18n'
import { formatDateTime } from '../../utils/format'

export type NorthboundServiceId = 'mqtt' | 'opcua' | 'webapi'
export interface NorthboundService {
  id: NorthboundServiceId
  name: string
  mode: string
  state: string
  tone: 'good' | 'warn' | 'bad' | 'normal' | 'disabled'
  rows: { label: string; value: string | number }[]
}

function mqttState(mqtt: MqttRuntimeStatus): Pick<NorthboundService, 'state' | 'tone'> {
  if (!mqtt.enabled) return { state: tx('已停用'), tone: 'disabled' }
  if (!mqtt.isRunning) return { state: tx('未运行'), tone: mqtt.lastError ? 'bad' : 'warn' }
  const connection = tx(mqtt.isConnected ? 'Broker 已连接' : 'Broker 未连接')
  if (!mqtt.publishEnabled) return { state: connection + ' · ' + tx('发布关闭'), tone: 'warn' }
  if (mqtt.circuitBreaker?.isOpen) return { state: connection + ' · ' + tx('发布熔断'), tone: 'warn' }
  return { state: connection, tone: mqtt.isConnected ? 'good' : 'warn' }
}

function mqttService(mqtt?: MqttRuntimeStatus): NorthboundService {
  const service: NorthboundService = {
    id: 'mqtt', name: 'MQTT', mode: tx('消息发布'),
    state: tx('等待运行状态同步'), tone: 'normal', rows: []
  }
  if (!mqtt) return service
  Object.assign(service, mqttState(mqtt))
  service.rows = [
    { label: 'Broker', value: mqtt.broker || '—' },
    { label: tx('Broker 连接'), value: tx(mqtt.isConnected ? '已连接' : '未连接') },
    { label: tx('发布启用'), value: tx(mqtt.publishEnabled ? '已启用' : '未启用') },
    { label: tx('已发布消息数'), value: mqtt.publishedCount ?? '—' },
    { label: tx('普通消息待发送'), value: mqtt.outboxPendingCount ?? '—' },
    { label: tx('待发报警'), value: mqtt.alarmPendingCount ?? '—' },
    { label: tx('最近发布'), value: formatDateTime(mqtt.lastPublishTime) }
  ]
  if (mqtt.lastError) service.rows.push({ label: tx('最近错误'), value: tx(mqtt.lastError) })
  return service
}

function opcUaService(opc?: OpcUaServerRuntimeStatus): NorthboundService {
  const service: NorthboundService = {
    id: 'opcua', name: 'OPC UA Server', mode: tx('读取 / 订阅'),
    state: tx('等待运行状态同步'), tone: 'normal', rows: []
  }
  if (!opc) return service
  if (!opc.enabled) {
    service.state = tx('已停用')
    service.tone = 'disabled'
  } else if (opc.isRunning) {
    service.state = tx('监听中')
    service.tone = 'good'
  } else {
    service.state = tx('未运行')
    service.tone = opc.lastError ? 'bad' : 'warn'
  }
  service.rows = [
    { label: tx('端点'), value: opc.endpointUrl || '—' },
    { label: tx('标签节点数'), value: opc.tagNodeCount ?? '—' },
    { label: tx('最近值更新时间'), value: formatDateTime(opc.lastValueUpdateTime) }
  ]
  if (opc.lastError) service.rows.push({ label: tx('最近错误'), value: tx(opc.lastError) })
  return service
}

export function buildNorthboundServices(status: Pick<GatewayStatus, 'mqtt' | 'opcUa'> | null): NorthboundService[] {
  return [
    mqttService(status?.mqtt),
    opcUaService(status?.opcUa),
    {
      id: 'webapi', name: 'Web API', mode: tx('HTTP 查询'), state: tx('请求 / 响应'), tone: 'normal',
      rows: [
        { label: tx('实时点位接口'), value: 'GET /api/config/status/tags' },
        { label: tx('历史点位接口'), value: 'GET /api/config/history/values' }
      ]
    }
  ]
}
