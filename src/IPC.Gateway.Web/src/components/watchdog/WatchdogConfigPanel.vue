<template>
  <a-card class="panel-card watchdog-config-panel" size="small">
    <template #title>
      <div class="card-header">
        <div class="detail-title">
          <span>{{ tx('看门狗配置') }}</span>
          <small>{{ tx('配置健康检查、自恢复、异常重启保护和监控对象') }}</small>
        </div>
        <div class="card-actions">
          <a-tag :color="tagColor(dirty ? 'warning' : 'success')">{{ dirty ? tx('未保存') : tx('已同步') }}</a-tag>
          <a-button :disabled="saving || !dirty" @click="resetDraft">{{ tx('重置') }}</a-button>
          <a-button type="primary" :loading="saving" :disabled="!canSave" @click="submit">
            {{ tx('保存配置') }}
          </a-button>
        </div>
      </div>
    </template>
    <a-alert
      type="info"
      show-icon
      :closable="false"
      class="watchdog-config-tip"
      :message="
        tx(
          '配置会写入当前环境的 appsettings 文件。检查间隔、阈值和监控开关可立即影响后续检查；启用状态和状态目录建议重启后确认完全生效。'
        )
      "
    />
    <a-form :model="draft" :disabled="!canSave" class="watchdog-config-form" layout="vertical">
      <section class="watchdog-config-section">
        <div class="watchdog-config-section__title">
          <h3>{{ tx('基础') }}</h3>
          <span>{{ tx('控制看门狗是否启用，以及保护状态文件保存位置。') }}</span>
        </div>
        <div class="watchdog-config-grid">
          <a-form-item :label="tx('启用看门狗')"><a-switch v-model:checked="draft.enabled" /></a-form-item>
          <a-form-item :label="tx('状态目录')" class="watchdog-config-grid__wide" required>
            <gateway-input v-model="draft.stateDirectory" placeholder="Data/Watchdog" />
          </a-form-item>
          <a-form-item :label="tx('检查周期 s')" required>
            <gateway-number-input v-model="draft.checkIntervalSeconds" :min="1" :max="3600" />
          </a-form-item>
          <a-form-item :label="tx('启动宽限 s')">
            <gateway-number-input v-model="draft.startupGraceSeconds" :min="0" :max="3600" />
          </a-form-item>
        </div>
      </section>
      <section class="watchdog-config-section">
        <div class="watchdog-config-section__title">
          <h3>{{ tx('监控对象') }}</h3>
          <span>{{ tx('选择纳入看门狗判断的运行组件。') }}</span>
        </div>
        <div class="watchdog-switch-grid">
          <a-checkbox v-model:checked="draft.monitorScheduler">{{ tx('采集调度器') }}</a-checkbox>
          <a-checkbox v-model:checked="draft.monitorMqtt">MQTT</a-checkbox>
          <a-checkbox v-model:checked="draft.monitorHistory">{{ tx('历史库') }}</a-checkbox>
          <a-checkbox v-model:checked="draft.monitorRuleEngine">{{ tx('规则引擎') }}</a-checkbox>
          <a-checkbox v-model:checked="draft.monitorOpcUa">OPC UA Server</a-checkbox>
        </div>
      </section>
      <section class="watchdog-config-section">
        <div class="watchdog-config-section__title">
          <h3>{{ tx('异常判定') }}</h3>
          <span>{{ tx('慢设备和外部连接异常达到阈值后触发降级或恢复建议。') }}</span>
        </div>
        <div class="watchdog-config-grid">
          <a-form-item :label="tx('采集无进展阈值 s')" required>
            <gateway-number-input v-model="draft.runtimeNoProgressSeconds" :min="30" :max="86400" />
          </a-form-item>
          <a-form-item :label="tx('MQTT 断连阈值 s')" required>
            <gateway-number-input
              v-model="draft.mqttDisconnectedSeconds"
              :disabled="!draft.monitorMqtt"
              :min="30"
              :max="86400"
            />
          </a-form-item>
        </div>
      </section>
      <section class="watchdog-config-section">
        <div class="watchdog-config-section__title">
          <h3>{{ tx('自恢复与重启保护') }}</h3>
          <span>{{ tx('限制恢复风暴，避免设备异常导致服务反复重启。') }}</span>
        </div>
        <div class="watchdog-config-grid">
          <a-form-item :label="tx('恢复冷却 s')" required>
            <gateway-number-input v-model="draft.recoveryCooldownSeconds" :min="1" :max="86400" />
          </a-form-item>
          <a-form-item :label="tx('恢复超时 s')" required>
            <gateway-number-input v-model="draft.recoveryTimeoutSeconds" :min="5" :max="3600" />
          </a-form-item>
          <a-form-item :label="tx('窗口内最大恢复次数')" required>
            <gateway-number-input v-model="draft.maxRecoveriesPerWindow" :min="1" :max="100" />
          </a-form-item>
          <a-form-item :label="tx('恢复保护窗口 min')" required>
            <gateway-number-input v-model="draft.recoveryWindowMinutes" :min="1" :max="1440" />
          </a-form-item>
          <a-form-item :label="tx('窗口内最大宿主重启请求')">
            <gateway-number-input v-model="draft.maxHostRestartRequestsPerWindow" :min="0" :max="100" />
          </a-form-item>
          <a-form-item :label="tx('宿主重启保护窗口 min')">
            <gateway-number-input v-model="draft.hostRestartProtectionWindowMinutes" :min="1" :max="1440" />
          </a-form-item>
          <a-form-item :label="tx('不可恢复时请求宿主停止')">
            <a-switch v-model:checked="draft.requestHostStopOnUnrecoverable" />
          </a-form-item>
        </div>
      </section>
    </a-form>
  </a-card>
</template>

<script setup lang="ts">
import { notify } from '../../utils/ui'
import { tx } from '../../i18n'
import { tagColor } from '../../utils/ui'
import { nextTick, reactive, ref, watch } from 'vue'

import type { GatewayWatchdogConfig } from '../../api'
import { createDefaultWatchdogConfig, normalizeWatchdogConfig, validateWatchdogConfig } from './watchdogModel'

const props = withDefaults(
  defineProps<{
    config: GatewayWatchdogConfig | null
    saving?: boolean
    canSave?: boolean
  }>(),
  {
    saving: false,
    canSave: true
  }
)

const emit = defineEmits<{
  'save-watchdog-config': [config: GatewayWatchdogConfig]
}>()

const draft = reactive(createDefaultWatchdogConfig())
const dirty = ref(false)
let syncing = false

watch(
  () => props.config,
  (value) => {
    if (!dirty.value) syncDraft(value)
  },
  { immediate: true, deep: true }
)

watch(
  draft,
  () => {
    if (!syncing) dirty.value = true
  },
  { deep: true }
)

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
    notify.warning('当前用户没有保存看门狗配置权限')
    return
  }
  const error = validateWatchdogConfig(draft)
  if (error) {
    notify.warning(error)
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
  color: var(--ui-text-color-primary);
  font-weight: 700;
}

.detail-title small {
  color: var(--ui-text-color-secondary);
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
  border: 1px solid var(--ui-border-color-light);
  border-radius: 8px;
  background: var(--ui-fill-color-blank);
}

.watchdog-config-section__title {
  display: grid;
  gap: 4px;
}

.watchdog-config-section__title h3 {
  margin: 0;
  font-size: calc(16px + 1pt);
}

.watchdog-config-section__title span {
  color: var(--ui-text-color-secondary);
  font-size: calc(13px + 1pt);
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

:deep(.ant-input-number) {
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
