<template>
  <section class="view-stack opcua-view">
    <section class="mqtt-status-bar">
      <div class="mqtt-status-main">
        <el-icon><Connection /></el-icon>
        <div>
          <span>OPC UA Server</span>
          <strong>{{ status?.isRunning ? '运行中' : opcUa?.enabled ? '未运行' : '未启用' }}</strong>
        </div>
        <el-tag size="small" :type="status?.isRunning ? 'success' : opcUa?.enabled ? 'warning' : 'info'">
          {{ status?.isRunning ? '已启动' : opcUa?.enabled ? '异常' : '停用' }}
        </el-tag>
      </div>
      <div class="mqtt-status-grid">
        <div>
          <span>端点</span>
          <strong>{{ endpointPreview }}</strong>
        </div>
        <div>
          <span>标签节点</span>
          <strong>{{ status?.tagNodeCount ?? 0 }}</strong>
        </div>
        <div>
          <span>值更新</span>
          <strong>{{ status?.valueUpdateCount ?? 0 }}</strong>
        </div>
        <div>
          <span>最近更新</span>
          <strong>{{ formatDateTime(status?.lastValueUpdateTime || '') || '-' }}</strong>
        </div>
      </div>
      <div class="mqtt-status-actions">
        <el-button v-if="canEditOpcUa" type="success" :icon="Check" @click="emit('persist-opcua')">保存</el-button>
        <span v-if="status?.lastError" class="error-text">{{ status.lastError }}</span>
      </div>
    </section>

    <el-card v-if="opcUa" shadow="never" class="settings-card">
      <el-tabs v-model="activeTab" class="mqtt-tabs">
        <el-tab-pane label="连接" name="connection">
          <el-form :model="opcUa" :disabled="!canEditOpcUa" label-width="170px" class="settings-form settings-form--wide">
            <el-form-item label="启用">
              <el-switch v-model="opcUa.enabled" />
            </el-form-item>
            <el-form-item label="应用名称">
              <el-input v-model="opcUa.applicationName" />
            </el-form-item>
            <el-form-item label="监听主机">
              <el-input v-model="opcUa.host" placeholder="0.0.0.0 或服务器 IP" />
            </el-form-item>
            <el-form-item label="监听端口">
              <el-input-number v-model="opcUa.port" :min="1" :max="65535" />
            </el-form-item>
            <el-form-item label="端点路径">
              <el-input v-model="opcUa.endpointPath" />
            </el-form-item>
            <el-form-item label="端点预览">
              <el-input :model-value="endpointPreview" disabled />
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="命名空间" name="namespace">
          <el-form :model="opcUa" :disabled="!canEditOpcUa" label-width="170px" class="settings-form settings-form--wide">
            <el-form-item label="应用 URI">
              <el-input v-model="opcUa.applicationUri" />
            </el-form-item>
            <el-form-item label="产品 URI">
              <el-input v-model="opcUa.productUri" />
            </el-form-item>
            <el-form-item label="标签命名空间">
              <el-input v-model="opcUa.namespaceUri" />
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="证书" name="certificate">
          <el-form :model="opcUa" :disabled="!canEditOpcUa" label-width="170px" class="settings-form settings-form--wide">
            <el-form-item label="证书目录">
              <el-input v-model="opcUa.certificateStorePath" />
            </el-form-item>
            <el-form-item label="自动信任客户端证书">
              <el-switch v-model="opcUa.autoAcceptUntrustedCertificates" />
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="安全" name="security">
          <el-form :model="opcUa" :disabled="!canEditOpcUa" label-width="190px" class="settings-form settings-form--wide">
            <el-form-item label="允许匿名访问">
              <el-switch v-model="opcUa.allowAnonymous" @change="handleAnonymousChange" />
            </el-form-item>
            <el-form-item label="用户名密码登录">
              <el-switch v-model="opcUa.usernamePasswordEnabled" @change="handleUsernamePasswordChange" />
            </el-form-item>
            <el-form-item label="用户名">
              <el-input v-model="opcUa.username" :disabled="!canEditOpcUa || !opcUa.usernamePasswordEnabled" autocomplete="username" />
            </el-form-item>
            <el-form-item label="密码">
              <el-input
                v-model="opcUa.password"
                type="password"
                show-password
                :placeholder="passwordPlaceholder"
                :disabled="!canEditOpcUa || !opcUa.usernamePasswordEnabled"
                autocomplete="new-password"
              />
            </el-form-item>
            <el-form-item label="密码状态">
              <el-tag size="small" :type="opcUa.passwordConfigured ? 'success' : 'info'">
                {{ opcUa.passwordConfigured ? '已配置' : '未配置' }}
              </el-tag>
            </el-form-item>
            <el-form-item label="安全策略">
              <el-select v-model="securityPolicyValue" @change="handleSecurityPolicyChange">
                <el-option
                  v-for="policy in securityPolicyOptions"
                  :key="policy.value"
                  :label="policy.label"
                  :value="policy.value"
                />
              </el-select>
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="诊断" name="diagnostics">
          <div class="runtime-grid mqtt-outbox-grid">
            <el-form :model="opcUa" :disabled="!canEditOpcUa" label-width="180px" class="settings-form settings-form--wide">
              <el-form-item label="最小采样间隔(ms)">
                <el-input-number v-model="opcUa.minimumSamplingIntervalMs" :min="100" :max="60000" />
              </el-form-item>
              <el-form-item label="发布诊断节点">
                <el-switch v-model="opcUa.publishDiagnostics" />
              </el-form-item>
            </el-form>
            <div class="runtime-panel">
              <h3>节点统计</h3>
              <div class="status-list">
                <span>设备节点</span>
                <strong>{{ status?.deviceNodeCount ?? 0 }}</strong>
                <span>分组节点</span>
                <strong>{{ status?.groupNodeCount ?? 0 }}</strong>
                <span>标签节点</span>
                <strong>{{ status?.tagNodeCount ?? 0 }}</strong>
                <span>最近消息</span>
                <strong>{{ status?.lastMessage || '-' }}</strong>
              </div>
            </div>
          </div>
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <el-empty v-else description="OPC UA Server 配置未加载" />
  </section>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { Check, Connection } from '@element-plus/icons-vue'
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
const securityPolicyOptions = [
  { label: 'None', value: 'None' },
  { label: 'Basic256', value: 'Basic256' },
  { label: 'Basic256Sha256', value: 'Basic256Sha256' }
]

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

function handleSecurityPolicyChange(value: string | number | boolean) {
  setSecurityPolicy(String(value))
}

function setSecurityPolicy(value: string) {
  if (!props.opcUa) return
  const policy = normalizeSecurityPolicy(value) || 'None'
  props.opcUa.securityPolicy = policy
  props.opcUa.allowSecurityPolicyNone = policy === 'None'
  props.opcUa.enableBasic256SignAndEncrypt = policy === 'Basic256'
  props.opcUa.enableBasic256Sha256SignAndEncrypt = policy === 'Basic256Sha256'
}

function normalizeSecurityPolicy(value: string | undefined) {
  return securityPolicyOptions.some(policy => policy.value === value) ? value || '' : ''
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
