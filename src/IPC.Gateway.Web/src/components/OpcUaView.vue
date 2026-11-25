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
        <a-button v-if="canEditOpcUa" type="success" :loading="saving" @click="save">
          <template #icon>
            <span class="ui-icon"><component :is="Check" /></span>
          </template>
          {{ tx('保存') }}
        </a-button>
        <span v-if="status?.lastError" class="error-text" :title="tx(status.lastError)">{{ tx(status.lastError) }}</span>
      </div>
    </section>
    <a-card v-if="opcUa" class="settings-card" size="small">
      <a-tabs v-model:active-key="activeTab" class="mqtt-tabs">
        <a-tab-pane :tab="tx('连接')" key="connection">
          <a-form
            :model="form"
            :disabled="!canEditOpcUa"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '170px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <a-form-item :label="tx('启用')"><a-switch v-model:checked="form.enabled" /></a-form-item>
            <a-form-item :label="tx('应用名称')"><gateway-input v-model="form.applicationName" /></a-form-item>
            <a-form-item :label="tx('监听主机')">
              <gateway-input v-model="form.host" :placeholder="tx('0.0.0.0 或服务器 IP')" />
            </a-form-item>
            <a-form-item :label="tx('监听端口')">
              <gateway-number-input v-model="form.port" :min="1" :max="65535" />
            </a-form-item>
            <a-form-item :label="tx('端点路径')"><gateway-input v-model="form.endpointPath" /></a-form-item>
            <a-form-item :label="tx('本地监听地址')"><gateway-input :model-value="listenPreview" disabled /></a-form-item>
            <a-form-item :label="tx('对外公布端点')">
              <gateway-input v-model="form.advertisedEndpointUrl" placeholder="opc.tcp://192.168.164.128:4840/IPC.Gateway" />
            </a-form-item>
            <a-form-item :label="tx('客户端连接地址')"><gateway-input :model-value="endpointPreview" disabled /></a-form-item>
          </a-form>
          <a-alert class="opcua-entry-note" type="info" show-icon :message="tx('对外公布端点留空时使用本机地址；通过代理连接时填写统一入口，路径需与本地端点相同。修改公布地址后请重新信任更新的服务端证书。')" />
        </a-tab-pane>
        <a-tab-pane :tab="tx('命名空间')" key="namespace">
          <a-form
            :model="form"
            :disabled="!canEditOpcUa"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '170px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <a-form-item :label="tx('应用 URI')"><gateway-input v-model="form.applicationUri" /></a-form-item>
            <a-form-item :label="tx('产品 URI')"><gateway-input v-model="form.productUri" /></a-form-item>
            <a-form-item :label="tx('标签命名空间')"><gateway-input v-model="form.namespaceUri" /></a-form-item>
          </a-form>
        </a-tab-pane>
        <a-tab-pane :tab="tx('证书')" key="certificate">
          <a-form
            :model="form"
            :disabled="!canEditOpcUa"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '170px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <a-form-item :label="tx('证书目录')"><gateway-input v-model="form.certificateStorePath" /></a-form-item>
            <a-form-item :label="tx('证书附加 IP / DNS')">
              <a-textarea v-model:value="certificateNamesText" :auto-size="{minRows:2,maxRows:5}" :placeholder="tx('每行一个 IP 或 DNS 名称；统一入口的名称自动加入证书。')" />
            </a-form-item>
            <a-form-item :label="tx('自动信任客户端证书')">
              <a-switch v-model:checked="form.autoAcceptUntrustedCertificates" />
            </a-form-item>
          </a-form>
        </a-tab-pane>
        <a-tab-pane :tab="tx('安全')" key="security">
          <a-form
            :model="form"
            :disabled="!canEditOpcUa"
            class="settings-form settings-form--wide"
            :label-col="{ style: { width: '190px' } }"
            :wrapper-col="{ style: { flex: 1 } }"
            label-align="left"
          >
            <a-form-item :label="tx('允许匿名访问')">
              <a-switch v-model:checked="form.allowAnonymous" @change="handleAnonymousChange" />
            </a-form-item>
            <a-form-item :label="tx('用户名密码登录')">
              <a-switch v-model:checked="form.usernamePasswordEnabled" @change="handleUsernamePasswordChange" />
            </a-form-item>
            <a-form-item :label="tx('用户名')">
              <gateway-input
                v-model="form.username"
                :disabled="!canEditOpcUa || !form.usernamePasswordEnabled"
                autocomplete="username"
              />
            </a-form-item>
            <a-form-item :label="tx('密码')">
              <gateway-input
                v-model="form.password"
                type="password"
                show-password
                :placeholder="tx(passwordPlaceholder)"
                :disabled="!canEditOpcUa || !form.usernamePasswordEnabled"
                autocomplete="new-password"
              />
            </a-form-item>
            <a-form-item :label="tx('密码状态')">
              <a-tag :color="tagColor(form.passwordConfigured ? 'success' : 'info')">
                {{ form.passwordConfigured ? tx('已配置') : tx('未配置') }}
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
              :model="form"
              :disabled="!canEditOpcUa"
              class="settings-form settings-form--wide"
              :label-col="{ style: { width: '180px' } }"
              :wrapper-col="{ style: { flex: 1 } }"
              label-align="left"
            >
              <a-form-item :label="tx('最小采样间隔(ms)')">
                <gateway-number-input v-model="form.minimumSamplingIntervalMs" :min="100" :max="60000" />
              </a-form-item>
              <a-form-item :label="tx('发布诊断节点')">
                <a-switch v-model:checked="form.publishDiagnostics" />
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
import { useCommunicationConfigDraft } from '../utils/communicationConfigDraft'
import { tagColor } from '../utils/ui'
import { computed, ref } from 'vue'
import { CheckOutlined as Check, ApartmentOutlined as Connection } from '@ant-design/icons-vue'
import type { OpcUaServerConfig, OpcUaServerRuntimeStatus } from '../api'
import { formatDateTime } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

const props = defineProps<{
  opcUa: OpcUaServerConfig | null
  resetRevision?: number
  saving?: boolean
  status?: OpcUaServerRuntimeStatus
}>()

const emit = defineEmits<{
  'persist-opcua': [config: OpcUaServerConfig, acceptSaved: (saved: OpcUaServerConfig) => void]
}>()

const { draft: opcUa, save } = useCommunicationConfigDraft(
  () => props.opcUa, () => props.resetRevision,
  (config, acceptSaved) => emit('persist-opcua', config, acceptSaved)
)
// The form is rendered only inside the opcUa guard; preserve its non-null type in Ant form slots.
const form = computed(() => opcUa.value!)
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
    return opcUa.value?.securityMode || (securityPolicyValue.value === 'None' ? 'None' : 'SignAndEncrypt')
  },
  set(value: string) {
    if (!opcUa.value) return
    opcUa.value.securityMode = value as OpcUaServerConfig['securityMode']
    setSecurityPolicy(value === 'None' ? 'None' : securityPolicyValue.value === 'None' ? 'Basic256Sha256' : securityPolicyValue.value)
  }
})
const securityPolicyOptions = computed(() => allSecurityPolicies.filter(policy =>
  securityModeValue.value === 'None' ? policy.value === 'None' : policy.value !== 'None'
))

const passwordPlaceholder = computed(() => {
  return opcUa.value?.passwordConfigured ? '已配置，留空不修改' : '请输入密码'
})

const securityPolicyValue = computed({
  get() {
    if (!opcUa.value) return 'None'
    return normalizeSecurityPolicy(opcUa.value.securityPolicy) || deriveSecurityPolicy()
  },
  set(value: string) {
    setSecurityPolicy(value)
  }
})

const endpointPreview = computed(() => {
  if (!opcUa.value) return '-'
  if (opcUa.value.advertisedEndpointUrl?.trim()) return opcUa.value.advertisedEndpointUrl.trim()
  const saved=props.opcUa
  if(saved?.endpointUrl && !saved.advertisedEndpointUrl && saved.host===opcUa.value.host &&
    saved.port===opcUa.value.port && saved.endpointPath===opcUa.value.endpointPath)return saved.endpointUrl
  const host = !opcUa.value.host || ['0.0.0.0','::','[::]'].includes(opcUa.value.host) ? window.location.hostname : opcUa.value.host
  const path = (opcUa.value.endpointPath || 'IPC.Gateway').replace(/^\/+|\/+$/g, '')
  return `opc.tcp://${host.includes(':') && !host.startsWith('[') ? '['+host+']' : host}:${opcUa.value.port || 4840}/${path}`
})
const listenPreview = computed(() => {
  const host=opcUa.value?.host || '0.0.0.0'
  return `opc.tcp://${host.includes(':') && !host.startsWith('[') ? '['+host+']' : host}:${opcUa.value?.port || 4840}/${(opcUa.value?.endpointPath || 'IPC.Gateway').replace(/^\/+|\/+$/g,'')}`
})
const certificateNamesText = computed({
  get:()=> (opcUa.value?.certificateAlternativeNames || []).join('\n'),
  set:(value:string)=>{ if(opcUa.value)opcUa.value.certificateAlternativeNames=value.split(/[\r\n,;]+/).map(v=>v.trim()).filter(Boolean) }
})

function handleAnonymousChange(value: string | number | boolean) {
  if (!opcUa.value) return
  if (!Boolean(value) && !opcUa.value.usernamePasswordEnabled) {
    opcUa.value.allowAnonymous = true
  }
}

function handleUsernamePasswordChange(value: string | number | boolean) {
  if (!opcUa.value) return
  if (!Boolean(value) && !opcUa.value.allowAnonymous) {
    opcUa.value.allowAnonymous = true
  }
}

function setSecurityPolicy(value: string) {
  if (!opcUa.value) return
  const policy = normalizeSecurityPolicy(value) || 'None'
  opcUa.value.securityPolicy = policy
  opcUa.value.allowSecurityPolicyNone = policy === 'None'
  opcUa.value.enableBasic256SignAndEncrypt = securityModeValue.value === 'SignAndEncrypt' && policy === 'Basic256'
  opcUa.value.enableBasic256Sha256SignAndEncrypt = securityModeValue.value === 'SignAndEncrypt' && policy === 'Basic256Sha256'
}

function normalizeSecurityPolicy(value: string | undefined) {
  return allSecurityPolicies.some((policy) => policy.value === value) ? value || '' : ''
}

function deriveSecurityPolicy() {
  if (!opcUa.value) return 'None'
  if (opcUa.value.usernamePasswordEnabled && opcUa.value.enableBasic256SignAndEncrypt) return 'Basic256'
  if (opcUa.value.usernamePasswordEnabled && opcUa.value.enableBasic256Sha256SignAndEncrypt) return 'Basic256Sha256'
  if (opcUa.value.allowSecurityPolicyNone) return 'None'
  if (opcUa.value.enableBasic256SignAndEncrypt) return 'Basic256'
  if (opcUa.value.enableBasic256Sha256SignAndEncrypt) return 'Basic256Sha256'
  return 'None'
}
</script>
<style scoped>
.opcua-entry-note{margin:4px 0 12px;overflow-wrap:anywhere}
</style>
