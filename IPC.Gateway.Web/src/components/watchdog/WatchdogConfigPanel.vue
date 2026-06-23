<template>
  <el-card shadow="never" class="panel-card watchdog-config-panel">
    <template #header>
      <div class="card-header">
        <div class="detail-title">
          <span>看门狗配置</span>
          <small>配置健康检查、自恢复、异常重启保护和监控对象</small>
        </div>
        <div class="card-actions">
          <el-tag :type="dirty ? 'warning' : 'success'" effect="plain">
            {{ dirty ? '未保存' : '已同步' }}
          </el-tag>
          <el-button :disabled="saving || !dirty" @click="resetDraft">重置</el-button>
          <el-button type="primary" :loading="saving" :disabled="!canSave" @click="submit">保存配置</el-button>
        </div>
      </div>
    </template>

    <el-alert
      type="info"
      show-icon
      :closable="false"
      class="watchdog-config-tip"
      title="配置会写入当前环境的 appsettings 文件。检查间隔、阈值和监控开关可立即影响后续检查；启用状态和状态目录建议重启后确认完全生效。"
    />

    <el-form :model="draft" :disabled="!canSave" label-position="top" class="watchdog-config-form">
      <section class="watchdog-config-section">
        <div class="watchdog-config-section__title">
          <h3>基础</h3>
          <span>控制看门狗是否启用，以及保护状态文件保存位置。</span>
        </div>
        <div class="watchdog-config-grid">
          <el-form-item label="启用看门狗">
            <el-switch v-model="draft.enabled" />
          </el-form-item>
          <el-form-item label="状态目录" class="watchdog-config-grid__wide" required>
            <el-input v-model="draft.stateDirectory" placeholder="Data/Watchdog" />
          </el-form-item>
          <el-form-item label="检查周期 s" required>
            <el-input-number v-model="draft.checkIntervalSeconds" :min="1" :max="3600" controls-position="right" />
          </el-form-item>
          <el-form-item label="启动宽限 s">
            <el-input-number v-model="draft.startupGraceSeconds" :min="0" :max="3600" controls-position="right" />
          </el-form-item>
        </div>
      </section>

      <section class="watchdog-config-section">
        <div class="watchdog-config-section__title">
          <h3>监控对象</h3>
          <span>选择纳入看门狗判断的运行组件。</span>
        </div>
        <div class="watchdog-switch-grid">
          <el-checkbox v-model="draft.monitorScheduler">采集调度器</el-checkbox>
          <el-checkbox v-model="draft.monitorMqtt">MQTT</el-checkbox>
          <el-checkbox v-model="draft.monitorHistory">历史库</el-checkbox>
          <el-checkbox v-model="draft.monitorRuleEngine">规则引擎</el-checkbox>
          <el-checkbox v-model="draft.monitorOpcUa">OPC UA Server</el-checkbox>
        </div>
      </section>

      <section class="watchdog-config-section">
        <div class="watchdog-config-section__title">
          <h3>异常判定</h3>
          <span>慢设备和外部连接异常达到阈值后触发降级或恢复建议。</span>
        </div>
        <div class="watchdog-config-grid">
          <el-form-item label="采集无进展阈值 s" required>
            <el-input-number v-model="draft.runtimeNoProgressSeconds" :min="30" :max="86400" controls-position="right" />
          </el-form-item>
          <el-form-item label="MQTT 断连阈值 s" required>
            <el-input-number
              v-model="draft.mqttDisconnectedSeconds"
              :disabled="!draft.monitorMqtt"
              :min="30"
              :max="86400"
              controls-position="right"
            />
          </el-form-item>
        </div>
      </section>

      <section class="watchdog-config-section">
        <div class="watchdog-config-section__title">
          <h3>自恢复与重启保护</h3>
          <span>限制恢复风暴，避免设备异常导致服务反复重启。</span>
        </div>
        <div class="watchdog-config-grid">
          <el-form-item label="恢复冷却 s" required>
            <el-input-number v-model="draft.recoveryCooldownSeconds" :min="1" :max="86400" controls-position="right" />
          </el-form-item>
          <el-form-item label="恢复超时 s" required>
            <el-input-number v-model="draft.recoveryTimeoutSeconds" :min="5" :max="3600" controls-position="right" />
          </el-form-item>
          <el-form-item label="窗口内最大恢复次数" required>
            <el-input-number v-model="draft.maxRecoveriesPerWindow" :min="1" :max="100" controls-position="right" />
          </el-form-item>
          <el-form-item label="恢复保护窗口 min" required>
            <el-input-number v-model="draft.recoveryWindowMinutes" :min="1" :max="1440" controls-position="right" />
          </el-form-item>
          <el-form-item label="窗口内最大宿主重启请求">
            <el-input-number v-model="draft.maxHostRestartRequestsPerWindow" :min="0" :max="100" controls-position="right" />
          </el-form-item>
          <el-form-item label="宿主重启保护窗口 min">
            <el-input-number v-model="draft.hostRestartProtectionWindowMinutes" :min="1" :max="1440" controls-position="right" />
          </el-form-item>
          <el-form-item label="不可恢复时请求宿主停止">
            <el-switch v-model="draft.requestHostStopOnUnrecoverable" />
          </el-form-item>
        </div>
      </section>
    </el-form>
  </el-card>
</template>

<script setup lang="ts">
import { nextTick, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import type { GatewayWatchdogConfig } from '../../api'
import { createDefaultWatchdogConfig, normalizeWatchdogConfig, validateWatchdogConfig } from './watchdogModel'

const props = withDefaults(defineProps<{
  config: GatewayWatchdogConfig | null
  saving?: boolean
  canSave?: boolean
}>(), {
  saving: false,
  canSave: true
})

const emit = defineEmits<{
  'save-watchdog-config': [config: GatewayWatchdogConfig]
}>()

const draft = reactive(createDefaultWatchdogConfig())
const dirty = ref(false)
let syncing = false

watch(() => props.config, value => {
  if (!dirty.value) syncDraft(value)
}, { immediate: true, deep: true })

watch(draft, () => {
  if (!syncing) dirty.value = true
}, { deep: true })

function syncDraft(value: GatewayWatchdogConfig | null) {
  syncing = true
  Object.assign(draft, normalizeWatchdogConfig(value))
  dirty.value = false
  nextTick(() => {
    syncing = false
  })
}

function resetDraft() {
  syncDraft(props.config)
}

function submit() {
  if (!props.canSave) {
    ElMessage.warning('当前用户没有保存看门狗配置权限')
    return
  }
  const error = validateWatchdogConfig(draft)
  if (error) {
    ElMessage.warning(error)
    return
  }
  dirty.value = false
  emit('save-watchdog-config', normalizeWatchdogConfig(draft))
}
</script>

<style scoped>
.card-header,
.card-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.detail-title {
  display: grid;
  gap: 4px;
}

.detail-title span {
  color: var(--el-text-color-primary);
  font-weight: 700;
}

.detail-title small {
  color: var(--el-text-color-secondary);
}

.watchdog-config-tip {
  margin-bottom: 16px;
}

.watchdog-config-form,
.watchdog-config-section {
  display: grid;
  gap: 16px;
}

.watchdog-config-section {
  padding: 16px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  background: var(--el-fill-color-blank);
}

.watchdog-config-section__title {
  display: grid;
  gap: 4px;
}

.watchdog-config-section__title h3 {
  margin: 0;
  font-size: 16px;
}

.watchdog-config-section__title span {
  color: var(--el-text-color-secondary);
  font-size: 13px;
}

.watchdog-config-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px 18px;
}

.watchdog-config-grid__wide {
  grid-column: 1 / -1;
}

.watchdog-switch-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 8px 14px;
}

:deep(.el-input-number) {
  width: 100%;
}

@media (max-width: 760px) {
  .card-header {
    align-items: stretch;
    flex-direction: column;
  }

  .card-actions {
    justify-content: flex-end;
  }
}
</style>
