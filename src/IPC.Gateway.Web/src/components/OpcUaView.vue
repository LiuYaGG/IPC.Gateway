<template>
  <section class="view-stack opcua-view">
    <section class="mqtt-status-bar">
      <div class="mqtt-status-main">
        <span class="ui-icon"><Connection /></span>
        <div>
          <span>OPC UA Server</span>
          <strong>{{ status?.isRunning ? tx('运行中') : opcUa?.enabled ? tx('未运行') : tx('未启用') }}</strong>
        </div>
        <a-tag :color="tagColor(status?.isRunning ? 'success' : opcUa?.enabled ? 'warning' : 'info')">
          {{ status?.isRunning ? tx('已启动') : opcUa?.enabled ? tx('异常') : tx('停用') }}
        </a-tag>
      </div>
      <div class="mqtt-status-grid">
        <div>
          <span>{{ tx('端点') }}</span>
          <strong>{{ endpointPreview }}</strong>
        </div>
        <div>
          <span>{{ tx('通道 / 标签节点') }}</span>
          <strong>{{ status?.channelNodeCount ?? 0 }} / {{ status?.tagNodeCount ?? 0 }}</strong>
        </div>
        <div>
          <span>{{ tx('值更新') }}</span>
          <strong>{{ status?.valueUpdateCount ?? 0 }}</strong>
        </div>
        <div>
          <span>{{ tx('最近更新') }}</span>
          <strong>{{ formatDateTime(status?.lastValueUpdateTime || '') || '-' }}</strong>
        </div>
      </div>
      <div class="mqtt-status-actions">
        <a-button v-if="canEditOpcUa" type="success" @click="emit('persist-opcua')">
          <template #icon>
            <span class="ui-icon"><component :is="Check" /></span>
          </template>
          {{ tx('保存') }}
        </a-button>
        <span v-if="status?.lastError" class="error-text" :title="tx(status.lastError)">{{ status.lastError }}</span>
      </div>
    </section>
    <a-card v-if="opcUa" class="settings-card" size="small">
      <a-tabs v-model:active-key="activeTab" class="mqtt-tabs">
        <a-tab-pane :tab="tx('连接')" key="connection">
          <a-form
            :model="opcUa"
            :disabled="!canEditOpcUa"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '170px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <a-form-item :label="tx('启用')"><a-switch v-model:checked="opcUa.enabled" /></a-form-item>
            <a-form-item :label="tx('应用名称')"><gateway-input v-model="opcUa.applicationName" /></a-form-item>
            <a-form-item :label="tx('监听主机')">
              <gateway-input v-model="opcUa.host" :placeholder="tx('0.0.0.0 或服务器 IP')" />
            </a-form-item>
            <a-form-item :label="tx('监听端口')">
              <gateway-number-input v-model="opcUa.port" :min="1" :max="65535" />
            </a-form-item>
            <a-form-item :label="tx('端点路径')"><gateway-input v-model="opcUa.endpointPath" /></a-form-item>
            <a-form-item :label="tx('端点预览')"><gateway-input :model-value="endpointPreview" disabled /></a-form-item>
          </a-form>
        </a-tab-pane>
        <a-tab-pane :tab="tx('命名空间')" key="namespace">
          <a-form
            :model="opcUa"
            :disabled="!canEditOpcUa"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '170px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <a-form-item :label="tx('应用 URI')"><gateway-input v-model="opcUa.applicationUri" /></a-form-item>
            <a-form-item :label="tx('产品 URI')"><gateway-input v-model="opcUa.productUri" /></a-form-item>
            <a-form-item :label="tx('标签命名空间')"><gateway-input v-model="opcUa.namespaceUri" /></a-form-item>
          </a-form>
        </a-tab-pane>
        <a-tab-pane :tab="tx('证书')" key="certificate">
          <a-form
            :model="opcUa"
            :disabled="!canEditOpcUa"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '170px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <a-form-item :label="tx('证书目录')"><gateway-input v-model="opcUa.certificateStorePath" /></a-form-item>
            <a-form-item :label="tx('自动信任客户端证书')">
              <a-switch v-model:checked="opcUa.autoAcceptUntrustedCertificates" />
            </a-form-item>
          </a-form>
        </a-tab-pane>
        <a-tab-pane :tab="tx('安全')" key="security">
          <a-form
            :model="opcUa"
            :disabled="!canEditOpcUa"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '190px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <a-form-item :label="tx('允许匿名访问')">
              <a-switch v-model:checked="opcUa.allowAnonymous" @change="handleAnonymousChange" />
            </a-form-item>
            <a-form-item :label="tx('用户名密码登录')">
              <a-switch v-model:checked="opcUa.usernamePasswordEnabled" @change="handleUsernamePasswordChange" />
            </a-form-item>
            <a-form-item :label="tx('用户名')">
              <gateway-input
                v-model="opcUa.username"
                :disabled="!canEditOpcUa || !opcUa.usernamePasswordEnabled"
                autocomplete="username"
              />
            </a-form-item>
            <a-form-item :label="tx('密码')">
              <gateway-input
                v-model="opcUa.password"
                type="password"
                show-password
                :placeholder="tx(passwordPlaceholder)"
                :disabled="!canEditOpcUa || !opcUa.usernamePasswordEnabled"
                autocomplete="new-password"
              />
            </a-form-item>
            <a-form-item :label="tx('密码状态')">
              <a-tag :color="tagColor(opcUa.passwordConfigured ? 'success' : 'info')">
                {{ opcUa.passwordConfigured ? tx('已配置') : tx('未配置') }}
              </a-tag>
            </a-form-item>
            <a-form-item :label="tx('消息安全模式')">
              <gateway-select v-model="securityModeValue">
                <a-select-option v-for="mode in securityModeOptions" :key="mode.value" :value="mode.value" :label="tx(mode.label)">
                  {{ tx(mode.label) }}
                </a-select-option>
              </gateway-select>
            </a-form-item>
            <a-form-item :label="tx('安全策略')">
              <gateway-select v-model="securityPolicyValue" :disabled="!canEditOpcUa || securityModeValue === 'None'">
                <a-select-option
                  v-for="policy in securityPolicyOptions"
                  :key="policy.value"
                  :label="tx(policy.label)"
                  :value="policy.value"
                >
                  {{ tx(policy.label) }}
                </a-select-option>
              </gateway-select>
            </a-form-item>
          </a-form>
        </a-tab-pane>
        <a-tab-pane :tab="tx('诊断')" key="diagnostics">
          <div class="runtime-grid mqtt-outbox-grid">
            <a-form
              :model="opcUa"
              :disabled="!canEditOpcUa"
              class="settings-form settings-form--wide"
              :label-col="{ style: { width: '180px' } }"
              :wrapper-col="{ style: { flex: 1 } }"
              label-align="left"
            >
              <a-form-item :label="tx('最小采样间隔(ms)')">
                <gateway-number-input v-model="opcUa.minimumSamplingIntervalMs" :min="100" :max="60000" />
              </a-form-item>
              <a-form-item :label="tx('发布诊断节点')">
                <a-switch v-model:checked="opcUa.publishDiagnostics" />
              </a-form-item>
            </a-form>
            <div class="runtime-panel">
              <h3>{{ tx('节点统计') }}</h3>
              <div class="status-list">
                <span>{{ tx('通道节点') }}</span>
                <strong>{{ status?.channelNodeCount ?? 0 }}</strong>
                <span>{{ tx('设备节点') }}</span>
                <strong>{{ status?.deviceNodeCount ?? 0 }}</strong>
                <span>{{ tx('分组节点') }}</span>
                <strong>{{ status?.groupNodeCount ?? 0 }}</strong>
                <span>{{ tx('标签节点') }}</span>
                <strong>{{ status?.tagNodeCount ?? 0 }}</strong>
                <span>{{ tx('最近消息') }}</span>
                <strong>{{ status?.lastMessage || '-' }}</strong>
              </div>
            </div>
          </div>
        </a-tab-pane>
      </a-tabs>
    </a-card>
    <a-empty v-else :description="tx('OPC UA Server 配置未加载')" />
  </section>
</template>

<script setup lang="ts">
import { tx } from '../i18n'
import { tagColor } from '../utils/ui'
import { computed, ref } from 'vue'
import { CheckOutlined as Check, ApartmentOutlined as Connection } from '@ant-design/icons-vue'
import type { OpcUaServerConfig, OpcUaServerRuntimeStatus } from '../api'
import { formatDateTime } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

const props = defineProps<{
  opcUa: OpcUaServerConfig | null
  status?: OpcUaServerRuntimeStatus
}>()

const emit = defineEmits<{
  'persist-opcua': []
}>()

const { hasPermission } = usePermissions()
const activeTab = ref('connection')
const canEditOpcUa = computed(() => hasPermission(PERMISSIONS.opcUaEdit))
const allSecurityPolicies = [
  { label: 'None', value: 'None' },
  { label: 'Basic128Rsa15', value: 'Basic128Rsa15' },
  { label: 'Basic256', value: 'Basic256' },
  { label: 'Basic256Sha256', value: 'Basic256Sha256' },
  { label: 'Aes128_Sha256_RsaOaep', value: 'Aes128_Sha256_RsaOaep' },
  { label: 'Aes256_Sha256_RsaPss', value: 'Aes256_Sha256_RsaPss' }
]
const securityModeOptions = [
  { label: 'None', value: 'None' },
  { label: '签名（Sign）', value: 'Sign' },
  { label: '签名与加密（SignAndEncrypt）', value: 'SignAndEncrypt' }
]
const securityModeValue = computed({
  get(): string {
    return props.opcUa?.securityMode || (securityPolicyValue.value === 'None' ? 'None' : 'SignAndEncrypt')
  },
  set(value: string) {
    if (!props.opcUa) return
    props.opcUa.securityMode = value as OpcUaServerConfig['securityMode']
    setSecurityPolicy(value === 'None' ? 'None' : securityPolicyValue.value === 'None' ? 'Basic256Sha256' : securityPolicyValue.value)
  }
})
const securityPolicyOptions = computed(() => allSecurityPolicies.filter(policy =>
  securityModeValue.value === 'None' ? policy.value === 'None' : policy.value !== 'None'
))

const passwordPlaceholder = computed(() => {
  return props.opcUa?.passwordConfigured ? '已配置，留空不修改' : '请输入密码'
})

const securityPolicyValue = computed({
  get() {
    if (!props.opcUa) return 'None'
    return normalizeSecurityPolicy(props.opcUa.securityPolicy) || deriveSecurityPolicy()
  },
  set(value: string) {
    setSecurityPolicy(value)
  }
})

const endpointPreview = computed(() => {
  if (!props.opcUa) return '-'
  if (props.opcUa.endpointUrl) return props.opcUa.endpointUrl
  const host = !props.opcUa.host || props.opcUa.host === '0.0.0.0' ? 'localhost' : props.opcUa.host
  const path = (props.opcUa.endpointPath || 'IPC.Gateway').replace(/^\/+|\/+$/g, '')
  return `opc.tcp://${host}:${props.opcUa.port || 4840}/${path}`
})

function handleAnonymousChange(value: string | number | boolean) {
  if (!props.opcUa) return
  if (!Boolean(value) && !props.opcUa.usernamePasswordEnabled) {
    props.opcUa.allowAnonymous = true
  }
}

function handleUsernamePasswordChange(value: string | number | boolean) {
  if (!props.opcUa) return
  if (!Boolean(value) && !props.opcUa.allowAnonymous) {
    props.opcUa.allowAnonymous = true
  }
}

function setSecurityPolicy(value: string) {
  if (!props.opcUa) return
  const policy = normalizeSecurityPolicy(value) || 'None'
  props.opcUa.securityPolicy = policy
  props.opcUa.allowSecurityPolicyNone = policy === 'None'
  props.opcUa.enableBasic256SignAndEncrypt = securityModeValue.value === 'SignAndEncrypt' && policy === 'Basic256'
  props.opcUa.enableBasic256Sha256SignAndEncrypt = securityModeValue.value === 'SignAndEncrypt' && policy === 'Basic256Sha256'
}

function normalizeSecurityPolicy(value: string | undefined) {
  return allSecurityPolicies.some((policy) => policy.value === value) ? value || '' : ''
}

function deriveSecurityPolicy() {
  if (!props.opcUa) return 'None'
  if (props.opcUa.usernamePasswordEnabled && props.opcUa.enableBasic256SignAndEncrypt) return 'Basic256'
  if (props.opcUa.usernamePasswordEnabled && props.opcUa.enableBasic256Sha256SignAndEncrypt) return 'Basic256Sha256'
  if (props.opcUa.allowSecurityPolicyNone) return 'None'
  if (props.opcUa.enableBasic256SignAndEncrypt) return 'Basic256'
  if (props.opcUa.enableBasic256Sha256SignAndEncrypt) return 'Basic256Sha256'
  return 'None'
}
</script>
