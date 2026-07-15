<template>
  <section class="view-stack mqtt-view">
    <section class="mqtt-status-bar">
      <div class="mqtt-status-main">
        <div>
          <span>运行状态</span>
          <el-tag :type="status?.isConnected ? 'success' : status?.isRunning ? 'warning' : 'info'">
            {{ status?.isConnected ? '已连接' : status?.isRunning ? '重连中' : '未运行' }}
          </el-tag>
          <el-tag :type="isSparkplug ? 'primary' : 'info'" effect="plain">
            {{ isSparkplug ? 'Sparkplug B' : '普通 MQTT' }}
          </el-tag>
        </div>
        <strong>{{ status?.broker || brokerPreview }}</strong>
      </div>

      <div class="mqtt-status-grid">
        <div>
          <span>ClientId</span>
          <strong>{{ mqtt?.clientId || '-' }}</strong>
        </div>
        <div>
          <span>最近连接</span>
          <strong>{{ formatDateTime(status?.lastConnectedTime) }}</strong>
        </div>
        <div>
          <span>最近发布</span>
          <strong>{{ formatDateTime(status?.lastPublishTime) }}</strong>
        </div>
        <div>
          <span>待发送</span>
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
        <span class="error-text">{{ status?.lastError || status?.lastPublishResult || '' }}</span>
        <el-button v-if="canEditMqtt" type="success" :icon="Check" @click="emit('persist-mqtt')">保存</el-button>
      </div>
    </section>

    <el-card shadow="never" class="panel-card">
      <el-tabs v-if="mqtt" v-model="activeTab" class="mqtt-tabs">
        <el-tab-pane label="连接" name="connection">
          <el-form :model="mqtt" :disabled="!canEditMqtt" label-width="150px" class="settings-form settings-form--wide">
            <div class="settings-grid">
              <el-form-item label="启用">
                <el-switch v-model="mqtt.enabled" />
              </el-form-item>
              <el-form-item label="发布启用">
                <el-switch v-model="mqtt.publishEnabled" />
              </el-form-item>
              <el-form-item label="发布模式">
                <el-segmented v-model="mqtt.publishMode" :options="publishModeOptions" @change="ensureSparkplugDefaults" />
              </el-form-item>
              <el-form-item label="网关 ID">
                <el-input v-model="mqtt.gatewayId" />
              </el-form-item>
              <el-form-item label="网关名称">
                <el-input v-model="mqtt.gatewayName" />
              </el-form-item>
              <el-form-item label="站点">
                <el-input v-model="mqtt.siteName" />
              </el-form-item>
              <el-form-item label="协议版本">
                <el-input v-model="mqtt.cloudProtocolVersion" />
              </el-form-item>
              <el-form-item label="配置版本">
                <el-input-number v-model="mqtt.configVersion" :min="1" :max="999999" />
              </el-form-item>
              <el-form-item label="ClientId">
                <el-input v-model="mqtt.clientId" />
              </el-form-item>
              <el-form-item label="主机">
                <el-input v-model="mqtt.host" />
              </el-form-item>
              <el-form-item label="端口">
                <el-input-number v-model="mqtt.port" :min="1" :max="65535" />
              </el-form-item>
              <el-form-item label="用户名">
                <el-input v-model="mqtt.username" autocomplete="username" />
              </el-form-item>
              <el-form-item label="密码">
                <el-input v-model="mqtt.password" type="password" autocomplete="current-password" show-password />
              </el-form-item>
              <el-form-item label="启用 TLS">
                <el-switch v-model="mqtt.useTls" />
              </el-form-item>
              <el-form-item label="允许不受信证书">
                <el-switch v-model="mqtt.allowUntrustedCertificates" :disabled="!mqtt.useTls" />
              </el-form-item>
              <el-form-item label="客户端证书">
                <el-input v-model="mqtt.clientCertificatePath" :disabled="!mqtt.useTls" placeholder="Data/Certificates/mqtt-client.pfx" />
              </el-form-item>
              <el-form-item label="证书密码">
                <el-input v-model="mqtt.clientCertificatePassword" :disabled="!mqtt.useTls" type="password" show-password />
              </el-form-item>
              <el-form-item label="客户端证书指纹">
                <el-input v-model="mqtt.clientCertificateThumbprint" :disabled="!mqtt.useTls" />
              </el-form-item>
              <el-form-item label="Broker证书指纹">
                <el-input v-model="mqtt.serverCertificateThumbprint" :disabled="!mqtt.useTls" />
              </el-form-item>
              <el-form-item label="CA证书路径">
                <el-input v-model="mqtt.caCertificatePath" :disabled="!mqtt.useTls" placeholder="Data/Certificates/ca.crt" />
              </el-form-item>
              <el-form-item label="KeepAlive(s)">
                <el-input-number v-model="mqtt.keepAliveSeconds" :min="5" :max="3600" />
              </el-form-item>
              <el-form-item label="重连间隔(s)">
                <el-input-number v-model="mqtt.reconnectSeconds" :min="1" :max="3600" />
              </el-form-item>
            </div>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="Sparkplug B" name="sparkplug">
          <el-form :model="mqtt" :disabled="!canEditMqtt || !isSparkplug" label-width="180px" class="settings-form settings-form--wide">
            <div class="settings-grid">
              <el-form-item label="命名空间">
                <el-input v-model="mqtt.sparkplugNamespace" />
              </el-form-item>
              <el-form-item label="Group ID">
                <el-input v-model="mqtt.sparkplugGroupId" />
              </el-form-item>
              <el-form-item label="Edge Node ID">
                <el-input v-model="mqtt.sparkplugEdgeNodeId" />
              </el-form-item>
              <el-form-item label="设备 ID 来源">
                <el-select v-model="mqtt.sparkplugDeviceIdSource">
                  <el-option label="设备名称" value="DeviceName" />
                  <el-option label="设备 ID" value="DeviceId" />
                </el-select>
              </el-form-item>
              <el-form-item label="接收 NCMD / DCMD">
                <el-switch v-model="mqtt.sparkplugEnableCommands" />
              </el-form-item>
              <el-form-item label="Primary Host ID">
                <el-input v-model="mqtt.sparkplugPrimaryHostId" placeholder="留空表示不启用 Primary Host 仲裁" />
              </el-form-item>
              <el-form-item label="Metric 名称模板">
                <el-input v-model="mqtt.sparkplugMetricNameTemplate" />
              </el-form-item>
              <el-form-item label="Birth QoS">
                <el-input-number v-model="mqtt.sparkplugBirthQos" :min="0" :max="2" />
              </el-form-item>
              <el-form-item label="Death QoS">
                <el-input-number v-model="mqtt.sparkplugDeathQos" :min="0" :max="2" />
              </el-form-item>
              <el-form-item label="Node Birth">
                <el-switch v-model="mqtt.sparkplugPublishNodeBirth" />
              </el-form-item>
              <el-form-item label="Device Birth">
                <el-switch v-model="mqtt.sparkplugPublishDeviceBirth" />
              </el-form-item>
              <el-form-item label="Device Death">
                <el-switch v-model="mqtt.sparkplugPublishDeviceDeath" />
              </el-form-item>
              <el-form-item label="工业上下文属性">
                <el-switch v-model="mqtt.sparkplugIncludeProperties" />
              </el-form-item>
              <el-form-item label="Metric Alias">
                <el-switch v-model="mqtt.sparkplugUseAliases" />
              </el-form-item>
            </div>
          </el-form>

          <div class="stat-pairs sparkplug-topic-preview">
            <div><span>NBIRTH</span><strong>{{ status?.sparkplugNodeBirthTopic || nodeBirthPreview }}</strong></div>
            <div><span>NDEATH</span><strong>{{ status?.sparkplugNodeDeathTopic || nodeDeathPreview }}</strong></div>
            <div><span>NCMD</span><strong>{{ nodeCommandPreview }}</strong></div>
            <div><span>DCMD</span><strong>{{ deviceCommandPreview }}</strong></div>
            <div><span>Primary Host STATE</span><strong>{{ primaryHostStatePreview }}</strong></div>
            <div><span>最近 Birth</span><strong>{{ formatDateTime(status?.lastSparkplugBirthTime) }}</strong></div>
            <div><span>最近 Death</span><strong>{{ formatDateTime(status?.lastSparkplugDeathTime) }}</strong></div>
            <div><span>DDATA 数量</span><strong>{{ status?.sparkplugDataCount ?? 0 }}</strong></div>
            <div><span>Namespace</span><strong>{{ status?.sparkplugNamespace || mqtt.sparkplugNamespace || 'spBv1.0' }}</strong></div>
          </div>
        </el-tab-pane>

        <el-tab-pane label="主题" name="topics">
          <el-form :model="mqtt" :disabled="!canEditMqtt" label-width="170px" class="settings-form settings-form--wide">
            <el-form-item label="订阅主题">
              <el-input v-model="mqtt.subscribeTopic" />
            </el-form-item>
            <el-form-item label="发布主题模板">
              <el-input v-model="mqtt.publishTopicTemplate" :disabled="isSparkplug || !canEditMqtt" />
            </el-form-item>
            <el-form-item label="心跳启用">
              <el-switch v-model="mqtt.heartbeatEnabled" />
            </el-form-item>
            <div class="settings-grid">
              <el-form-item label="心跳间隔(s)">
                <el-input-number v-model="mqtt.heartbeatIntervalSeconds" :min="5" :max="86400" />
              </el-form-item>
              <el-form-item label="心跳 QoS">
                <el-input-number v-model="mqtt.heartbeatQos" :min="0" :max="2" />
              </el-form-item>
            </div>
            <el-form-item label="心跳主题">
              <el-input v-model="mqtt.heartbeatTopic" :disabled="isSparkplug || !canEditMqtt" />
            </el-form-item>
            <el-form-item label="状态主题">
              <el-input v-model="mqtt.statusTopic" :disabled="isSparkplug || !canEditMqtt" />
            </el-form-item>
            <el-form-item label="命令回复主题模板">
              <el-input v-model="mqtt.commandReplyTopicTemplate" />
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="发布策略" name="publish">
          <el-form :model="mqtt" :disabled="!canEditMqtt" label-width="180px" class="settings-form settings-form--wide">
            <div class="settings-grid">
              <el-form-item label="只发布选中标签">
                <el-switch v-model="mqtt.publishSelectedTagsOnly" />
              </el-form-item>
              <el-form-item label="仅变化时发布">
                <el-switch v-model="mqtt.publishChangedOnly" />
              </el-form-item>
              <el-form-item label="发布 QoS">
                <el-input-number v-model="mqtt.publishQos" :min="0" :max="2" />
              </el-form-item>
              <el-form-item label="ACK 超时(ms)">
                <el-input-number v-model="mqtt.publishAckTimeoutMilliseconds" :min="1000" :max="60000" />
              </el-form-item>
              <el-form-item label="未变化心跳(s)">
                <el-input-number v-model="mqtt.publishUnchangedHeartbeatSeconds" :min="0" :max="86400" />
              </el-form-item>
              <el-form-item label="批量发送数量">
                <el-input-number v-model="mqtt.publishFlushBatchSize" :min="1" :max="10000" />
              </el-form-item>
              <el-form-item label="重试最小间隔(s)">
                <el-input-number v-model="mqtt.publishRetryMinSeconds" :min="1" :max="86400" />
              </el-form-item>
              <el-form-item label="重试最大间隔(s)">
                <el-input-number v-model="mqtt.publishRetryMaxSeconds" :min="1" :max="86400" />
              </el-form-item>
            </div>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="离线缓存" name="outbox">
          <div class="runtime-grid mqtt-outbox-grid">
            <el-form :model="mqtt" :disabled="!canEditMqtt" label-width="160px" class="settings-form settings-form--wide">
              <el-form-item label="缓存目录">
                <el-input v-model="mqtt.outboxDirectory" />
              </el-form-item>
              <div class="settings-grid">
                <el-form-item label="最大消息数">
                  <el-input-number v-model="mqtt.outboxMaxMessages" :min="100" :max="1000000" />
                </el-form-item>
                <el-form-item label="最大容量(MB)">
                  <el-input-number v-model="mqtt.outboxMaxMegabytes" :min="1" :max="102400" />
                </el-form-item>
                <el-form-item label="保留小时数">
                  <el-input-number v-model="mqtt.outboxRetentionHours" :min="1" :max="87600" />
                </el-form-item>
                <el-form-item label="隔离保留(h)">
                  <el-input-number v-model="mqtt.outboxQuarantineRetentionHours" :min="1" :max="87600" />
                </el-form-item>
              </div>
            </el-form>

            <div class="stat-pairs">
              <div><span>已隔离</span><strong>{{ status?.outboxQuarantinedMessageCount ?? 0 }}</strong></div>
              <div><span>隔离中</span><strong>{{ status?.outboxQuarantineCount ?? 0 }}</strong></div>
              <div><span>隔离大小</span><strong>{{ formatBytes(status?.outboxQuarantineBytes ?? 0) }}</strong></div>
              <div><span>隔离清理</span><strong>{{ status?.outboxQuarantineExpiredDeletedCount ?? 0 }}</strong></div>
              <div><span>最早隔离</span><strong>{{ formatDateTime(status?.outboxOldestQuarantineTime) }}</strong></div>
              <div><span>隔离目录</span><strong>{{ status?.outboxQuarantineDirectory || '-' }}</strong></div>
              <div><span>最老积压</span><strong>{{ formatDurationSeconds(status?.outboxOldestPendingAgeSeconds ?? 0) }}</strong></div>
              <div><span>下一次重试</span><strong>{{ formatDateTime(status?.nextPublishRetryTime) }}</strong></div>
              <div><span>连续失败</span><strong>{{ status?.publishConsecutiveFailureCount ?? 0 }}</strong></div>
              <div><span>无效文件</span><strong>{{ status?.outboxInvalidMessageCount ?? 0 }}</strong></div>
              <div><span>待发送</span><strong>{{ status?.outboxPendingCount ?? 0 }}</strong></div>
              <div><span>已入队</span><strong>{{ status?.outboxEnqueuedCount ?? 0 }}</strong></div>
              <div><span>缓存大小</span><strong>{{ formatBytes(status?.outboxBytes ?? 0) }}</strong></div>
              <div><span>过期删除</span><strong>{{ status?.outboxExpiredDeletedCount ?? 0 }}</strong></div>
              <div><span>溢出删除</span><strong>{{ status?.outboxOverflowDeletedCount ?? 0 }}</strong></div>
              <div><span>重试退避(s)</span><strong>{{ status?.publishRetryBackoffSeconds ?? 0 }}</strong></div>
            </div>
          </div>
        </el-tab-pane>
      </el-tabs>
      <el-empty v-else description="暂无 MQTT 配置" />
    </el-card>
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { Check } from '@element-plus/icons-vue'
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
