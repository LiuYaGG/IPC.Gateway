<template>
  <a-form :model="model" class="channel-form" layout="vertical">
    <div class="channel-form__grid">
      <a-form-item :label="tx('通道名称')" required>
        <gateway-input v-model="model.name" maxlength="80" show-word-limit :placeholder="tx('例如：三菱生产线通道')" />
      </a-form-item>
      <a-form-item :label="tx('协议驱动')" required>
        <gateway-select v-model="selectedDriver" searchable :disabled="driverLocked" :placeholder="tx('选择协议驱动')">
          <a-select-option v-for="item in driverOptions" :key="item.value" :label="tx(item.label)" :value="item.value">
            {{ tx(item.label) }}
          </a-select-option>
        </gateway-select>
        <small v-if="driverLocked" class="channel-form__hint">{{ tx('通道已有设备，协议驱动不可修改') }}</small>
      </a-form-item>
      <a-form-item :label="tx('最大并发设备轮询')">
        <gateway-number-input v-model="model.maxConcurrentDevicePolls" :min="1" :max="256" />
        <small class="channel-form__hint">{{ tx('限制该通道同时执行的设备读循环数量') }}</small>
      </a-form-item>
      <a-form-item :label="tx('调度权重')">
        <gateway-number-input v-model="model.schedulingWeight" :min="1" :max="100" />
        <small class="channel-form__hint">{{ tx('通道繁忙时，权重越高获得轮询机会越多') }}</small>
      </a-form-item>
    </div>
    <a-form-item :label="tx('启用通道')" class="channel-form__switch">
      <a-switch v-model:checked="model.enabled" :checked-children="tx('启用')" :un-checked-children="tx('停用')" />
    </a-form-item>
  </a-form>
</template>

<script setup lang="ts">
import { tx } from '../i18n'
import { computed } from 'vue'
import type { ChannelConfig } from '../api'

const props = defineProps<{
  model: ChannelConfig
  driverOptions: Array<{ label: string; value: string; protocol: string; driverId: string }>
  driverLocked?: boolean
}>()

const selectedDriver = computed({
  get: () => `${props.model.protocol}::${props.model.driverId || ''}`,
  set: (value) => {
    const option = props.driverOptions.find((item) => item.value === value)
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

.channel-form :deep(.ant-select),
.channel-form :deep(.ant-input-number) {
  width: 100%;
}

.channel-form__hint {
  display: block;
  margin-top: 6px;
  color: var(--ui-text-color-secondary);
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
