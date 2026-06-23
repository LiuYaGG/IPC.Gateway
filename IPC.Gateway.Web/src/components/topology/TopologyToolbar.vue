<template>
  <section class="topology-toolbar">
    <el-input
      :model-value="search"
      clearable
      placeholder="搜索设备、协议、分组、标签"
      @update:model-value="emit('update:search', String($event))"
    />
    <el-switch
      :model-value="showTagNodes"
      active-text="显示标签节点"
      inactive-text="折叠标签"
      @update:model-value="emit('update:showTagNodes', Boolean($event))"
    />
    <el-button :icon="Aim" @click="emit('fit')">适配画布</el-button>
  </section>
</template>

<script setup lang="ts">
import { Aim } from '@element-plus/icons-vue'

defineProps<{
  search: string
  showTagNodes: boolean
}>()

const emit = defineEmits<{
  'update:search': [value: string]
  'update:showTagNodes': [value: boolean]
  fit: []
}>()
</script>

<style scoped>
.topology-toolbar {
  display: grid;
  grid-template-columns: minmax(260px, 1fr) auto auto;
  gap: 12px;
  align-items: center;
  padding: 14px;
  border: 1px solid #dbe4ef;
  border-radius: 8px;
  background: #ffffff;
}

.topology-toolbar :deep(.el-switch__label) {
  color: #475569;
}

@media (max-width: 960px) {
  .topology-toolbar {
    grid-template-columns: 1fr;
    justify-items: start;
  }
}
</style>
