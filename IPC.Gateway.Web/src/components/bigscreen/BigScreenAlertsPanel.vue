<template>
  <section class="big-panel alerts-panel">
    <header>
      <span>最近告警</span>
      <small>{{ errors.length }} events</small>
    </header>
    <div class="alert-list">
      <button v-for="error in visibleErrors" :key="errorKey(error)" type="button" class="alert-row" @click="emit('select-error', error)">
        <strong>{{ error.deviceName || error.category || '系统' }}</strong>
        <span>{{ error.message || '-' }}</span>
        <small>{{ formatDateTime(error.timestamp) }}</small>
      </button>
      <el-empty v-if="visibleErrors.length === 0" description="暂无告警" />
    </div>
  </section>
</template>

<script setup lang="ts">
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
  return `${error.timestamp}-${error.category}-${error.deviceName}-${error.tagName}-${error.message}`
}
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

.big-panel small {
  color: #94a3b8;
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
  border: 1px solid rgba(127, 29, 29, 0.35);
  border-radius: 8px;
  color: inherit;
  text-align: left;
  background: rgba(127, 29, 29, 0.18);
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
  color: #fecdd3;
}

.alert-row span {
  color: #f8fafc;
}
</style>
