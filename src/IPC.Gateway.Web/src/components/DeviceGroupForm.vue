<template>
  <a-form
    :model="model"
    class="device-form"
    :label-col="{ style: { width: '110px' } }"
    :wrapper-col="{ style: { flex: 1 } }"
    label-align="left"
  >
    <a-form-item :label="tx('分组名称')" required>
      <gateway-input v-model="model.name" :placeholder="tx('例如：运行参数')" />
    </a-form-item>
    <a-form-item :label="tx('启用')"><a-switch v-model:checked="model.enabled" /></a-form-item>
    <a-form-item :label="tx('采集周期(ms)')">
      <gateway-number-input v-model="model.scanRateMs" :min="100" :max="3600000" />
    </a-form-item>
  </a-form>
</template>

<script setup lang="ts">
import { tx } from '../i18n'
import type { GroupConfig } from '../api'

defineProps<{
  model: GroupConfig
}>()
</script>
