<template>
  <section class="big-panel slow-panel">
    <header>
      <span>{{ tx('慢设备与异常采集') }}</span>
      <small>top {{ rows.length }}</small>
    </header>
    <div class="slow-list">
      <article v-for="device in rows" :key="device.deviceId" class="slow-row">
        <div>
          <strong>{{ device.deviceName }}</strong>
          <span>{{ device.protocol || '-' }}</span>
        </div>
        <div>
          <strong>{{ Number(device.lastTaskDurationMs || 0).toFixed(0) }}ms</strong>
          <span>{{ tx('成功率') }} {{ Number(device.successRate || 0).toFixed(1) }}%</span>
        </div>
        <div>
          <strong>{{ device.timeoutCount || 0 }}</strong>
          <span>{{ tx('超时') }}</span>
        </div>
        <small :title="tx(device.lastError)">
          {{ tx(statusText(device.lastError || device.lastTaskStatus || device.status || '-')) }}
        </small>
      </article>
      <a-empty v-if="rows.length === 0" :description="tx('暂无设备数据')" />
    </div>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
import { statusText } from '../../utils/display'
import { computed } from 'vue'
import type { DeviceRuntimeStatus } from '../../api'
import { topSlowDevices } from './bigScreenModel'

const props = defineProps<{
  devices: DeviceRuntimeStatus[]
}>()

const rows = computed(() => topSlowDevices(props.devices, 6))
</script>

<style scoped>
.big-panel {
  display: grid;
  gap: 12px;
  padding: 16px;
  border: 1px solid #e0e9f2;
  border-radius: 6px;
  background: #ffffff;
}

.big-panel header {
  display: flex;
  justify-content: space-between;
  color: #000000;
}

.big-panel small,
.slow-row span {
  color: #7a8a9e;
}

.slow-list {
  display: grid;
  gap: 10px;
}

.slow-row {
  display: grid;
  grid-template-columns: minmax(120px, 1fr) 120px 70px minmax(120px, 1.2fr);
  gap: 12px;
  align-items: center;
  padding: 11px 12px;
  border: 1px solid #e0e9f2;
  border-radius: 6px;
  background: #f5f8fc;
}

.slow-row div {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.slow-row strong,
.slow-row small {
  overflow: hidden;
  color: #000000;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
