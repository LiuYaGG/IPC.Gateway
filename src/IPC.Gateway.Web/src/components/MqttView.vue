<template>
  <section class="view-stack mqtt-view">
    <section class="mqtt-status-bar">
      <div class="mqtt-status-main">
        <div>
          <span>{{ tx('运行状态') }}</span>
          <a-tag :color="tagColor(status?.isConnected ? 'success' : status?.isRunning ? 'warning' : 'info')">
            {{ status?.isConnected ? tx('已连接') : status?.isRunning ? tx('重连中') : tx('未运行') }}
          </a-tag>
          <a-tag :color="tagColor(isSparkplug ? 'primary' : 'info')">
            {{ isSparkplug ? 'Sparkplug B' : tx('普通 MQTT') }}
          </a-tag>
        </div>
        <strong>{{ status?.broker || brokerPreview }}</strong>
      </div>
      <div class="mqtt-status-grid">
        <div>
          <span>ClientId</span>
          <strong>{{ mqtt?.clientId || '-' }}</strong>
        </div>
        <div>
          <span>{{ tx('最近连接') }}</span>
          <strong>{{ formatDateTime(status?.lastConnectedTime) }}</strong>
        </div>
        <div>
          <span>{{ tx('最近发布') }}</span>
          <strong>{{ formatDateTime(status?.lastPublishTime) }}</strong>
        </div>
        <div>
          <span>{{ tx('待发送') }}</span>
          <strong>{{ status?.outboxPendingCount ?? 0 }}</strong>
        </div>
        <div>
          <span>Birth</span>
          <strong>{{ status?.sparkplugBirthCount ?? 0 }}</strong>
        </div>
        <div>
          <span>Death</span>
          <strong>{{ status?.sparkplugDeathCount ?? 0 }}</strong>
        </div>
      </div>
      <div class="mqtt-status-actions">
        <span class="error-text" :title="tx(status?.lastError || status?.lastPublishResult || '')">{{ tx(status?.lastError || status?.lastPublishResult || '') }}</span>
        <a-button v-if="canEditMqtt" type="success" @click="emit('persist-mqtt')">
          <template #icon>
            <span class="ui-icon"><component :is="Check" /></span>
          </template>
          {{ tx('保存') }}
        </a-button>
      </div>
    </section>
    <a-card class="panel-card" size="small">
      <a-tabs v-if="mqtt" v-model:active-key="activeTab" class="mqtt-tabs">
        <a-tab-pane :tab="tx('连接')" key="connection">
          <a-form
            :model="mqtt"
            :disabled="!canEditMqtt"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '150px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <div class="settings-grid">
              <a-form-item :label="tx('启用')"><a-switch v-model:checked="mqtt.enabled" /></a-form-item>
              <a-form-item :label="tx('发布启用')"><a-switch v-model:checked="mqtt.publishEnabled" /></a-form-item>
              <a-form-item :label="tx('发布模式')">
                <gateway-choice
                  v-model="mqtt.publishMode"
                  :options="publishModeOptions"
                  @change="ensureSparkplugDefaults"
                />
              </a-form-item>
              <a-form-item :label="tx('网关 ID')"><gateway-input v-model="mqtt.gatewayId" /></a-form-item>
              <a-form-item :label="tx('网关名称')"><gateway-input v-model="mqtt.gatewayName" /></a-form-item>
              <a-form-item :label="tx('站点')"><gateway-input v-model="mqtt.siteName" /></a-form-item>
              <a-form-item :label="tx('协议版本')"><gateway-input v-model="mqtt.cloudProtocolVersion" /></a-form-item>
              <a-form-item :label="tx('配置版本')">
                <gateway-number-input v-model="mqtt.configVersion" :min="1" :max="999999" />
              </a-form-item>
              <a-form-item label="ClientId"><gateway-input v-model="mqtt.clientId" /></a-form-item>
              <a-form-item :label="tx('主机')"><gateway-input v-model="mqtt.host" /></a-form-item>
              <a-form-item :label="tx('端口')">
                <gateway-number-input v-model="mqtt.port" :min="1" :max="65535" />
              </a-form-item>
              <a-form-item :label="tx('用户名')">
                <gateway-input v-model="mqtt.username" autocomplete="username" />
              </a-form-item>
              <a-form-item :label="tx('密码')">
                <gateway-input v-model="mqtt.password" type="password" autocomplete="current-password" show-password />
              </a-form-item>
              <a-form-item :label="tx('启用 TLS')"><a-switch v-model:checked="mqtt.useTls" /></a-form-item>
              <a-form-item :label="tx('允许不受信证书')">
                <a-switch v-model:checked="mqtt.allowUntrustedCertificates" :disabled="!mqtt.useTls" />
              </a-form-item>
              <a-form-item :label="tx('客户端证书')">
                <gateway-input
                  v-model="mqtt.clientCertificatePath"
                  :disabled="!mqtt.useTls"
                  placeholder="Data/Certificates/mqtt-client.pfx"
                />
              </a-form-item>
              <a-form-item :label="tx('证书密码')">
                <gateway-input
                  v-model="mqtt.clientCertificatePassword"
                  :disabled="!mqtt.useTls"
                  type="password"
                  show-password
                />
              </a-form-item>
              <a-form-item :label="tx('客户端证书指纹')">
                <gateway-input v-model="mqtt.clientCertificateThumbprint" :disabled="!mqtt.useTls" />
              </a-form-item>
              <a-form-item :label="tx('Broker证书指纹')">
                <gateway-input v-model="mqtt.serverCertificateThumbprint" :disabled="!mqtt.useTls" />
              </a-form-item>
              <a-form-item :label="tx('CA证书路径')">
                <gateway-input
                  v-model="mqtt.caCertificatePath"
                  :disabled="!mqtt.useTls"
                  placeholder="Data/Certificates/ca.crt"
                />
              </a-form-item>
              <a-form-item label="KeepAlive(s)">
                <gateway-number-input v-model="mqtt.keepAliveSeconds" :min="5" :max="3600" />
              </a-form-item>
              <a-form-item :label="tx('重连间隔(s)')">
                <gateway-number-input v-model="mqtt.reconnectSeconds" :min="1" :max="3600" />
              </a-form-item>
            </div>
          </a-form>
        </a-tab-pane>
        <a-tab-pane tab="Sparkplug B" key="sparkplug">
          <a-form
            :model="mqtt"
            :disabled="!canEditMqtt || !isSparkplug"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '180px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <div class="settings-grid">
              <a-form-item :label="tx('命名空间')"><gateway-input v-model="mqtt.sparkplugNamespace" /></a-form-item>
              <a-form-item label="Group ID"><gateway-input v-model="mqtt.sparkplugGroupId" /></a-form-item>
              <a-form-item label="Edge Node ID"><gateway-input v-model="mqtt.sparkplugEdgeNodeId" /></a-form-item>
              <a-form-item :label="tx('设备 ID 来源')">
                <gateway-select v-model="mqtt.sparkplugDeviceIdSource">
                  <a-select-option :label="tx('设备名称')" value="DeviceName">{{ tx('设备名称') }}</a-select-option>
                  <a-select-option :label="tx('设备 ID')" value="DeviceId">{{ tx('设备 ID') }}</a-select-option>
                </gateway-select>
              </a-form-item>
              <a-form-item :label="tx('接收 NCMD / DCMD')">
                <a-switch v-model:checked="mqtt.sparkplugEnableCommands" />
              </a-form-item>
              <a-form-item label="Primary Host ID">
                <gateway-input
                  v-model="mqtt.sparkplugPrimaryHostId"
                  :placeholder="tx('留空表示不启用 Primary Host 仲裁')"
                />
              </a-form-item>
              <a-form-item :label="tx('Metric 名称模板')">
                <gateway-input v-model="mqtt.sparkplugMetricNameTemplate" />
              </a-form-item>
              <a-form-item label="Birth QoS">
                <gateway-number-input v-model="mqtt.sparkplugBirthQos" :min="0" :max="2" />
              </a-form-item>
              <a-form-item label="Death QoS">
                <gateway-number-input v-model="mqtt.sparkplugDeathQos" :min="0" :max="2" />
              </a-form-item>
              <a-form-item label="Node Birth">
                <a-switch v-model:checked="mqtt.sparkplugPublishNodeBirth" />
              </a-form-item>
              <a-form-item label="Device Birth">
                <a-switch v-model:checked="mqtt.sparkplugPublishDeviceBirth" />
              </a-form-item>
              <a-form-item label="Device Death">
                <a-switch v-model:checked="mqtt.sparkplugPublishDeviceDeath" />
              </a-form-item>
              <a-form-item :label="tx('工业上下文属性')">
                <a-switch v-model:checked="mqtt.sparkplugIncludeProperties" />
              </a-form-item>
              <a-form-item label="Metric Alias"><a-switch v-model:checked="mqtt.sparkplugUseAliases" /></a-form-item>
            </div>
          </a-form>
          <div class="stat-pairs sparkplug-topic-preview">
            <div>
              <span>NBIRTH</span>
              <strong>{{ status?.sparkplugNodeBirthTopic || nodeBirthPreview }}</strong>
            </div>
            <div>
              <span>NDEATH</span>
              <strong>{{ status?.sparkplugNodeDeathTopic || nodeDeathPreview }}</strong>
            </div>
            <div>
              <span>NCMD</span>
              <strong>{{ nodeCommandPreview }}</strong>
            </div>
            <div>
              <span>DCMD</span>
              <strong>{{ deviceCommandPreview }}</strong>
            </div>
            <div>
              <span>Primary Host STATE</span>
              <strong>{{ primaryHostStatePreview }}</strong>
            </div>
            <div>
              <span>{{ tx('最近 Birth') }}</span>
              <strong>{{ formatDateTime(status?.lastSparkplugBirthTime) }}</strong>
            </div>
            <div>
              <span>{{ tx('最近 Death') }}</span>
              <strong>{{ formatDateTime(status?.lastSparkplugDeathTime) }}</strong>
            </div>
            <div>
              <span>{{ tx('DDATA 数量') }}</span>
              <strong>{{ status?.sparkplugDataCount ?? 0 }}</strong>
            </div>
            <div>
              <span>Namespace</span>
              <strong>{{ status?.sparkplugNamespace || mqtt.sparkplugNamespace || 'spBv1.0' }}</strong>
            </div>
          </div>
        </a-tab-pane>
        <a-tab-pane :tab="tx('主题')" key="topics">
          <a-form
            :model="mqtt"
            :disabled="!canEditMqtt"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '170px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <a-form-item :label="tx('订阅主题')"><gateway-input v-model="mqtt.subscribeTopic" /></a-form-item>
            <a-form-item :label="tx('发布主题模板')">
              <gateway-input v-model="mqtt.publishTopicTemplate" :disabled="isSparkplug || !canEditMqtt" />
            </a-form-item>
            <a-form-item :label="tx('心跳启用')"><a-switch v-model:checked="mqtt.heartbeatEnabled" /></a-form-item>
            <div class="settings-grid">
              <a-form-item :label="tx('心跳间隔(s)')">
                <gateway-number-input v-model="mqtt.heartbeatIntervalSeconds" :min="5" :max="86400" />
              </a-form-item>
              <a-form-item :label="tx('心跳 QoS')">
                <gateway-number-input v-model="mqtt.heartbeatQos" :min="0" :max="2" />
              </a-form-item>
            </div>
            <a-form-item :label="tx('心跳主题')">
              <gateway-input v-model="mqtt.heartbeatTopic" :disabled="isSparkplug || !canEditMqtt" />
            </a-form-item>
            <a-form-item :label="tx('状态主题')">
              <gateway-input v-model="mqtt.statusTopic" :disabled="isSparkplug || !canEditMqtt" />
            </a-form-item>
            <a-form-item :label="tx('命令回复主题模板')">
              <gateway-input v-model="mqtt.commandReplyTopicTemplate" />
            </a-form-item>
          </a-form>
        </a-tab-pane>
        <a-tab-pane :tab="tx('发布策略')" key="publish">
          <a-form
            :model="mqtt"
            :disabled="!canEditMqtt"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '180px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <div class="settings-grid">
              <a-form-item :label="tx('只发布选中标签')">
                <a-switch v-model:checked="mqtt.publishSelectedTagsOnly" />
              </a-form-item>
              <a-form-item :label="tx('仅变化时发布')">
                <a-switch v-model:checked="mqtt.publishChangedOnly" />
              </a-form-item>
              <a-form-item :label="tx('发布 QoS')">
                <gateway-number-input v-model="mqtt.publishQos" :min="0" :max="2" />
              </a-form-item>
              <a-form-item :label="tx('ACK 超时(ms)')">
                <gateway-number-input v-model="mqtt.publishAckTimeoutMilliseconds" :min="1000" :max="60000" />
              </a-form-item>
              <a-form-item :label="tx('未变化心跳(s)')">
                <gateway-number-input v-model="mqtt.publishUnchangedHeartbeatSeconds" :min="0" :max="86400" />
              </a-form-item>
              <a-form-item :label="tx('批量发送数量')">
                <gateway-number-input v-model="mqtt.publishFlushBatchSize" :min="1" :max="10000" />
              </a-form-item>
              <a-form-item :label="tx('重试最小间隔(s)')">
                <gateway-number-input v-model="mqtt.publishRetryMinSeconds" :min="1" :max="86400" />
              </a-form-item>
              <a-form-item :label="tx('重试最大间隔(s)')">
                <gateway-number-input v-model="mqtt.publishRetryMaxSeconds" :min="1" :max="86400" />
              </a-form-item>
            </div>
          </a-form>
        </a-tab-pane>
        <a-tab-pane :tab="tx('离线缓存')" key="outbox">
          <div class="runtime-grid mqtt-outbox-grid">
            <a-form
              :model="mqtt"
              :disabled="!canEditMqtt"
              class="settings-form settings-form--wide"
              :label-col="{ style: { width: '160px' } }"
              :wrapper-col="{ style: { flex: 1 } }"
              label-align="left"
            >
              <a-form-item :label="tx('缓存目录')"><gateway-input v-model="mqtt.outboxDirectory" /></a-form-item>
              <div class="settings-grid">
                <a-form-item :label="tx('最大消息数')">
                  <gateway-number-input v-model="mqtt.outboxMaxMessages" :min="100" :max="1000000" />
                </a-form-item>
                <a-form-item :label="tx('最大容量(MB)')">
                  <gateway-number-input v-model="mqtt.outboxMaxMegabytes" :min="1" :max="102400" />
                </a-form-item>
                <a-form-item :label="tx('保留小时数')">
                  <gateway-number-input v-model="mqtt.outboxRetentionHours" :min="1" :max="87600" />
                </a-form-item>
                <a-form-item :label="tx('隔离保留(h)')">
                  <gateway-number-input v-model="mqtt.outboxQuarantineRetentionHours" :min="1" :max="87600" />
                </a-form-item>
              </div>
            </a-form>
            <div class="stat-pairs">
              <div>
                <span>{{ tx('已隔离') }}</span>
                <strong>{{ status?.outboxQuarantinedMessageCount ?? 0 }}</strong>
              </div>
              <div>
                <span>{{ tx('隔离中') }}</span>
                <strong>{{ status?.outboxQuarantineCount ?? 0 }}</strong>
              </div>
              <div>
                <span>{{ tx('隔离大小') }}</span>
                <strong>{{ formatBytes(status?.outboxQuarantineBytes ?? 0) }}</strong>
              </div>
              <div>
                <span>{{ tx('隔离清理') }}</span>
                <strong>{{ status?.outboxQuarantineExpiredDeletedCount ?? 0 }}</strong>
              </div>
              <div>
                <span>{{ tx('最早隔离') }}</span>
                <strong>{{ formatDateTime(status?.outboxOldestQuarantineTime) }}</strong>
              </div>
              <div>
                <span>{{ tx('隔离目录') }}</span>
                <strong>{{ status?.outboxQuarantineDirectory || '-' }}</strong>
              </div>
              <div>
                <span>{{ tx('最老积压') }}</span>
                <strong>{{ formatDurationSeconds(status?.outboxOldestPendingAgeSeconds ?? 0) }}</strong>
              </div>
              <div>
                <span>{{ tx('下一次重试') }}</span>
                <strong>{{ formatDateTime(status?.nextPublishRetryTime) }}</strong>
              </div>
              <div>
                <span>{{ tx('连续失败') }}</span>
                <strong>{{ status?.publishConsecutiveFailureCount ?? 0 }}</strong>
              </div>
              <div>
                <span>{{ tx('无效文件') }}</span>
                <strong>{{ status?.outboxInvalidMessageCount ?? 0 }}</strong>
              </div>
              <div>
                <span>{{ tx('待发送') }}</span>
                <strong>{{ status?.outboxPendingCount ?? 0 }}</strong>
              </div>
              <div>
                <span>{{ tx('已入队') }}</span>
                <strong>{{ status?.outboxEnqueuedCount ?? 0 }}</strong>
              </div>
              <div>
                <span>{{ tx('缓存大小') }}</span>
                <strong>{{ formatBytes(status?.outboxBytes ?? 0) }}</strong>
              </div>
              <div>
                <span>{{ tx('过期删除') }}</span>
                <strong>{{ status?.outboxExpiredDeletedCount ?? 0 }}</strong>
              </div>
              <div>
                <span>{{ tx('溢出删除') }}</span>
                <strong>{{ status?.outboxOverflowDeletedCount ?? 0 }}</strong>
              </div>
              <div>
                <span>{{ tx('重试退避(s)') }}</span>
                <strong>{{ status?.publishRetryBackoffSeconds ?? 0 }}</strong>
              </div>
            </div>
          </div>
        </a-tab-pane>
      </a-tabs>
      <a-empty v-else :description="tx('暂无 MQTT 配置')" />
    </a-card>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../i18n'
import { tagColor } from '../utils/ui'
import { computed, ref } from 'vue'
import { CheckOutlined as Check } from '@ant-design/icons-vue'
import type { MqttRuntimeStatus } from '../api'
import { formatBytes, formatDateTime, formatDurationSeconds } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

const props = defineProps<{
  mqtt: Record<string, any> | null
  status: MqttRuntimeStatus | null | undefined
}>()

const emit = defineEmits<{
  'persist-mqtt': []
}>()

const { hasPermission } = usePermissions()
const activeTab = ref('connection')
const canEditMqtt = computed(() => hasPermission(PERMISSIONS.mqttEdit))
const publishModeOptions = [
  { label: '普通 MQTT', value: 'Classic' },
  { label: 'Sparkplug B', value: 'SparkplugB' }
]

const isSparkplug = computed(() => props.mqtt?.publishMode === 'SparkplugB' || props.status?.sparkplugEnabled)

const brokerPreview = computed(() => {
  if (!props.mqtt) return '-'
  const host = props.mqtt.host || '-'
  const port = props.mqtt.port || 1883
  return `${host}:${port}`
})

const nodeBirthPreview = computed(() => buildSparkplugTopic('NBIRTH'))
const nodeDeathPreview = computed(() => buildSparkplugTopic('NDEATH'))
const nodeCommandPreview = computed(() => buildSparkplugTopic('NCMD'))
const deviceCommandPreview = computed(() => `${buildSparkplugTopic('DCMD')}/+`)
const primaryHostStatePreview = computed(() => {
  if (!props.mqtt?.sparkplugPrimaryHostId) return '未启用'
  const namespaceName = sanitizeTopic(props.mqtt.sparkplugNamespace || 'spBv1.0')
  return `${namespaceName}/STATE/${sanitizeTopic(props.mqtt.sparkplugPrimaryHostId)}`
})

function ensureSparkplugDefaults() {
  if (!props.mqtt) return
  props.mqtt.publishMode ||= 'Classic'
  props.mqtt.sparkplugNamespace ||= 'spBv1.0'
  props.mqtt.sparkplugGroupId ||= props.mqtt.gatewayId || 'IPC-Gateway'
  props.mqtt.sparkplugEdgeNodeId ||= props.mqtt.clientId || props.mqtt.gatewayId || 'EdgeNode'
  props.mqtt.sparkplugDeviceIdSource ||= 'DeviceId'
  props.mqtt.sparkplugMetricNameTemplate ||= '{channel}/{group}/{tag}'
  props.mqtt.sparkplugPublishNodeBirth ??= true
  props.mqtt.sparkplugPublishDeviceBirth ??= true
  props.mqtt.sparkplugPublishDeviceDeath ??= true
  props.mqtt.sparkplugIncludeProperties ??= true
  props.mqtt.sparkplugUseAliases ??= false
  props.mqtt.sparkplugBirthQos ??= 0
  props.mqtt.sparkplugDeathQos ??= 0
  props.mqtt.sparkplugEnableCommands ??= true
  props.mqtt.sparkplugPrimaryHostId ??= ''
}

function buildSparkplugTopic(messageType: string) {
  if (!props.mqtt) return '-'
  const namespaceName = sanitizeTopic(props.mqtt.sparkplugNamespace || 'spBv1.0')
  const groupId = sanitizeTopic(props.mqtt.sparkplugGroupId || props.mqtt.gatewayId || 'IPC-Gateway')
  const edgeNodeId = sanitizeTopic(props.mqtt.sparkplugEdgeNodeId || props.mqtt.clientId || 'EdgeNode')
  return `${namespaceName}/${groupId}/${messageType}/${edgeNodeId}`
}

function sanitizeTopic(value: string) {
  const text = String(value || '_')
    .replaceAll('\\', '/')
    .replaceAll('+', '_')
    .replaceAll('#', '_')
    .replace(/^\/+|\/+$/g, '')
  return text || '_'
}
</script>
