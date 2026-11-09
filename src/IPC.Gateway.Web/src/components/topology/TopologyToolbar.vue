<template>
  <section class="topology-toolbar">
    <gateway-input
      :model-value="search"
      allow-clear
      :placeholder="tx('搜索设备、协议、分组、标签')"
      @update:model-value="emit('update:search', String($event))"
    />
    <a-switch
      :checked="showTagNodes"
      :checked-children="tx('显示标签节点')"
      :un-checked-children="tx('折叠标签')"
      @update:checked="emit('update:showTagNodes', Boolean($event))"
    />
    <a-button @click="emit('fit')">
      <template #icon>
        <span class="ui-icon"><component :is="Aim" /></span>
      </template>
      {{ tx('适配画布') }}
    </a-button>
  </section>
</template>

<script setup lang="ts">
import { tx } from '../../i18n'
import { AimOutlined as Aim } from '@ant-design/icons-vue'

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

.topology-toolbar :deep(.ant-switch__label) {
  color: #475569;
}

@media (max-width: 960px) {
  .topology-toolbar {
    grid-template-columns: 1fr;
    justify-items: start;
  }
}
</style>
