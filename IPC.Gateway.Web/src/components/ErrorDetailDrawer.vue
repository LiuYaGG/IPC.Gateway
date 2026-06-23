<template>
  <el-drawer
    :model-value="visible"
    title="错误详情"
    size="420px"
    @update:model-value="emit('update:visible', $event)"
  >
    <el-empty v-if="!error" description="暂无错误" />
    <div v-else class="error-detail">
      <el-tag :type="error.category ? 'danger' : 'warning'" effect="dark">
        {{ error.category || 'Runtime' }}
      </el-tag>

      <h3>{{ error.message || '-' }}</h3>

      <dl>
        <dt>设备</dt>
        <dd>{{ error.deviceName || '-' }}</dd>
        <dt>分组</dt>
        <dd>{{ error.groupName || '-' }}</dd>
        <dt>标签</dt>
        <dd>{{ error.tagName || '-' }}</dd>
        <dt>来源</dt>
        <dd>{{ error.source || '-' }}</dd>
        <dt>时间</dt>
        <dd>{{ formatDateTime(error.timestamp) }}</dd>
      </dl>

      <section class="error-detail__suggestion">
        <span>处理建议</span>
        <p>{{ error.suggestion || '检查设备连接、标签地址和协议参数。' }}</p>
      </section>
    </div>
  </el-drawer>
</template>

<script setup lang="ts">
import type { RuntimeErrorDetail } from '../api'
import { formatDateTime } from '../utils/format'

defineProps<{
  visible: boolean
  error: RuntimeErrorDetail | null
}>()

const emit = defineEmits<{
  'update:visible': [value: boolean]
}>()
</script>
