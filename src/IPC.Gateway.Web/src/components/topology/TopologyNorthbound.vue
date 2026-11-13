<template>
  <div class="topology-north-stack">
    <a-button
      v-for="service in services"
      :key="service.id"
      class="topology-north-node"
      :data-service="service.id"
      :aria-pressed="selectedId === service.id"
      :title="[service.name, service.mode, service.state].join(' · ')"
      @click="emit('select', service.id)"
    >
      <span class="north-icon"><component :is="icons[service.id]" /></span>
      <span class="north-copy">
        <strong>{{ service.name }}</strong>
        <small>{{ service.mode }}</small>
        <small class="north-state" :data-tone="service.tone">{{ service.state }}</small>
      </span>
    </a-button>
  </div>
</template>

<script setup lang="ts">
import { ApiOutlined, CloudUploadOutlined, ClusterOutlined } from '@ant-design/icons-vue'
import type { NorthboundService, NorthboundServiceId } from './northboundTopology'

defineProps<{ services: NorthboundService[]; selectedId?: NorthboundServiceId | null }>()
const emit = defineEmits<{ select: [id: NorthboundServiceId] }>()
const icons = { mqtt: CloudUploadOutlined, opcua: ClusterOutlined, webapi: ApiOutlined }
</script>

<style scoped>
.topology-north-stack { display: grid; min-width: 0; gap: 8px; }
.topology-north-node.ant-btn {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: flex-start;
  width: 100%;
  min-width: 0;
  height: 68px;
  padding: 4px 10px;
  gap: 8px;
  text-align: start;
  white-space: normal;
  color: #111;
  background: #fff;
  border: 1px solid #cfdee9;
  border-radius: 6px;
}
.topology-north-node:hover, .topology-north-node[aria-pressed="true"] {
  border-color: #6c98b4;
  background: #eef6fd;
}
.north-icon {
  display: grid;
  place-items: center;
  flex: 0 0 28px;
  height: 28px;
  border-radius: 5px;
  background: #e9f1f7;
  color: #3c6785;
}
.topology-north-node .north-copy {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  min-width: 0;
  line-height: 1.25;
}
.north-copy strong { font-size: calc(13px + 1pt); }
.north-copy small { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #4c6071; font-size: 12px; }
.north-state::before {
  content: '';
  display: inline-block;
  width: 6px;
  height: 6px;
  margin-inline-end: 5px;
  vertical-align: 0.08em;
  background: #6e8292;
  border-radius: 50%;
}
.north-state[data-tone="good"]::before { background: #1c885c; }
.north-state[data-tone="warn"]::before { background: #b9761c; }
.north-state[data-tone="bad"]::before { background: #c4473c; }
.north-state[data-tone="disabled"]::before { background: #90a0ac; }
</style>
