<template>
  <a-drawer :open="visible" :title="tx('错误详情')" width="420px" @update:open="emit('update:visible', $event)">
    <a-empty v-if="!error" :description="tx('暂无错误')" />
    <div v-else class="error-detail">
      <a-tag :color="tagColor(error.category ? 'danger' : 'warning')">{{ error.category || 'Runtime' }}</a-tag>
      <h3>{{ tx(error.message || '-') }}</h3>
      <dl>
        <dt>{{ tx('通道') }}</dt>
        <dd>{{ error.channelName || '-' }}</dd>
        <dt>{{ tx('设备') }}</dt>
        <dd>{{ error.deviceName || '-' }}</dd>
        <dt>{{ tx('分组') }}</dt>
        <dd>{{ error.groupName || '-' }}</dd>
        <dt>{{ tx('标签') }}</dt>
        <dd>{{ error.tagName || '-' }}</dd>
        <dt>{{ tx('来源') }}</dt>
        <dd>{{ error.source || '-' }}</dd>
        <dt>{{ tx('时间') }}</dt>
        <dd>{{ formatDateTime(error.timestamp) }}</dd>
      </dl>
      <section class="error-detail__suggestion">
        <span>{{ tx('处理建议') }}</span>
        <p>{{ tx(error.suggestion || '检查设备连接、标签地址和协议参数。') }}</p>
      </section>
    </div>
  </a-drawer>
</template>

<script setup lang="ts">
import { tx } from '../i18n'
import { tagColor } from '../utils/ui'
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
