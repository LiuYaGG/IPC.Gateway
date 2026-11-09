<template>
  <section class="big-panel alerts-panel">
    <header>
      <span>{{ tx('最近告警') }}</span>
      <small>{{ errors.length }} {{ tx('条事件') }}</small>
    </header>
    <div class="alert-list">
      <button
        v-for="error in visibleErrors"
        :key="errorKey(error)"
        type="button"
        class="alert-row"
        @click="emit('select-error', error)"
      >
        <strong>{{ error.deviceName || error.category || tx('系统') }}</strong>
        <span>{{ error.message || '-' }}</span>
        <small>{{ formatDateTime(error.timestamp) }}</small>
      </button>
      <a-empty v-if="visibleErrors.length === 0" :description="tx('暂无告警')" />
    </div>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
import { computed } from 'vue'
import type { RuntimeErrorDetail } from '../../api'
import { formatDateTime } from '../../utils/format'

const props = defineProps<{
  errors: RuntimeErrorDetail[]
}>()

const emit = defineEmits<{
  'select-error': [error: RuntimeErrorDetail]
}>()

const visibleErrors = computed(() => props.errors.slice(0, 8))

function errorKey(error: RuntimeErrorDetail) {
  return `${error.timestamp}-${error.category}-${error.channelId}-${error.deviceId}-${error.groupId}-${error.tagId}-${error.message}`
}
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

.big-panel small {
  color: #7a8a9e;
}

.alert-list {
  display: grid;
  gap: 9px;
}

.alert-row {
  display: grid;
  grid-template-columns: 110px minmax(0, 1fr) 122px;
  gap: 10px;
  align-items: center;
  width: 100%;
  padding: 10px 12px;
  border: 1px solid #f0dcdc;
  border-radius: 6px;
  color: inherit;
  text-align: left;
  background: #fcf2f2;
  cursor: pointer;
}

.alert-row strong,
.alert-row span,
.alert-row small {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.alert-row strong {
  color: #b75959;
}

.alert-row span {
  color: #000000;
}
</style>
