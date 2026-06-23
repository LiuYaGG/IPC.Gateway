<template>
  <el-form label-width="110px" :model="model" class="device-form">
    <el-form-item label="分组名称" required>
      <el-input v-model="model.name" placeholder="例如：运行参数" />
    </el-form-item>
    <el-form-item label="启用">
      <el-switch v-model="model.enabled" />
    </el-form-item>
    <el-form-item label="采集周期(ms)">
      <el-input-number v-model="model.scanRateMs" :min="100" :max="3600000" />
    </el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import type { GroupConfig } from '../api'

defineProps<{
  model: GroupConfig
}>()
</script>
