<template>
  <el-form :model="model" label-position="top" class="channel-form">
    <div class="channel-form__grid">
      <el-form-item label="通道名称" required>
        <el-input v-model="model.name" maxlength="80" show-word-limit placeholder="例如：三菱生产线通道" />
      </el-form-item>
      <el-form-item label="协议驱动" required>
        <el-select v-model="selectedDriver" filterable :disabled="driverLocked" placeholder="选择协议驱动">
          <el-option v-for="item in driverOptions" :key="item.value" :label="item.label" :value="item.value" />
        </el-select>
        <small v-if="driverLocked" class="channel-form__hint">通道已有设备，协议驱动不可修改</small>
      </el-form-item>
      <el-form-item label="最大并发设备轮询">
        <el-input-number v-model="model.maxConcurrentDevicePolls" :min="1" :max="256" controls-position="right" />
        <small class="channel-form__hint">限制该通道同时执行的设备读循环数量</small>
      </el-form-item>
      <el-form-item label="调度权重">
        <el-input-number v-model="model.schedulingWeight" :min="1" :max="100" controls-position="right" />
        <small class="channel-form__hint">通道繁忙时，权重越高获得轮询机会越多</small>
      </el-form-item>
    </div>
    <el-form-item label="启用通道" class="channel-form__switch">
      <el-switch v-model="model.enabled" active-text="启用" inactive-text="停用" />
    </el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { ChannelConfig } from '../api'

const props = defineProps<{
  model: ChannelConfig
  driverOptions: Array<{ label: string; value: string; protocol: string; driverId: string }>
  driverLocked?: boolean
}>()

const selectedDriver = computed({
  get: () => `${props.model.protocol}::${props.model.driverId || ''}`,
  set: value => {
    const option = props.driverOptions.find(item => item.value === value)
    if (!option) return
    props.model.protocol = option.protocol
    props.model.driverId = option.driverId
  }
})
</script>

<style scoped>
.channel-form__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 4px 18px;
}

.channel-form :deep(.el-select),
.channel-form :deep(.el-input-number) {
  width: 100%;
}

.channel-form__hint {
  display: block;
  margin-top: 6px;
  color: var(--el-text-color-secondary);
  line-height: 1.4;
}

.channel-form__switch {
  margin-top: 4px;
}

@media (max-width: 720px) {
  .channel-form__grid {
    grid-template-columns: 1fr;
  }
}
</style>
