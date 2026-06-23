<template>
  <section class="big-screen-view">
    <BigScreenHero :status="status" :health="health" />
    <BigScreenKpiStrip :status="status" />

    <div class="big-screen-grid">
      <BigScreenTrendPanel :trend="trend" />
      <BigScreenDistributionPanel :devices="status?.devices ?? []" />
      <BigScreenResourcePanel :status="status" />
      <BigScreenModulePanel :status="status" />
      <BigScreenSlowDevices :devices="status?.devices ?? []" />
      <BigScreenAlertsPanel :errors="status?.recentErrors ?? []" @select-error="emit('select-error', $event)" />
    </div>
  </section>
</template>

<script setup lang="ts">
import type { GatewayHealthResponse, GatewayStatus, RuntimeErrorDetail } from '../../api'
import type { TrendSample } from '../DashboardView.vue'
import BigScreenAlertsPanel from './BigScreenAlertsPanel.vue'
import BigScreenDistributionPanel from './BigScreenDistributionPanel.vue'
import BigScreenHero from './BigScreenHero.vue'
import BigScreenKpiStrip from './BigScreenKpiStrip.vue'
import BigScreenModulePanel from './BigScreenModulePanel.vue'
import BigScreenResourcePanel from './BigScreenResourcePanel.vue'
import BigScreenSlowDevices from './BigScreenSlowDevices.vue'
import BigScreenTrendPanel from './BigScreenTrendPanel.vue'

defineProps<{
  status: GatewayStatus | null
  health: GatewayHealthResponse | null
  trend: TrendSample[]
}>()

const emit = defineEmits<{
  'select-error': [error: RuntimeErrorDetail]
}>()
</script>

<style scoped>
.big-screen-view {
  display: grid;
  gap: 14px;
  min-height: 100%;
  padding: 2px;
  color: #e2e8f0;
}

.big-screen-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.35fr) minmax(360px, 0.9fr);
  gap: 14px;
  align-items: stretch;
}

.big-screen-grid > :first-child {
  grid-column: 1 / -1;
}

@media (max-width: 1280px) {
  .big-screen-grid {
    grid-template-columns: 1fr;
  }
}
</style>
