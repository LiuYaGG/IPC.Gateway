<template>
  <section class="big-panel slow-panel">
    <header>
      <span>慢设备与异常采集</span>
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
          <span>成功率 {{ Number(device.successRate || 0).toFixed(1) }}%</span>
        </div>
        <div>
          <strong>{{ device.timeoutCount || 0 }}</strong>
          <span>超时</span>
        </div>
        <small :title="device.lastError">{{ device.lastError || device.lastTaskStatus || device.status || '-' }}</small>
      </article>
      <el-empty v-if="rows.length === 0" description="暂无设备数据" />
    </div>
  </section>
</template>

<script setup lang="ts">
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
  border: 1px solid rgba(148, 163, 184, 0.18);
  border-radius: 8px;
  background: #0f1d2e;
}

.big-panel header {
  display: flex;
  justify-content: space-between;
  color: #f8fafc;
}

.big-panel small,
.slow-row span {
  color: #94a3b8;
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
  border: 1px solid rgba(51, 65, 85, 0.78);
  border-radius: 8px;
  background: #132235;
}

.slow-row div {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.slow-row strong,
.slow-row small {
  overflow: hidden;
  color: #f8fafc;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
