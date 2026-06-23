<template>
  <section class="metric-card" :class="toneClass">
    <div class="metric-card__head">
      <span>{{ label }}</span>
      <el-icon v-if="icon">
        <component :is="icon" />
      </el-icon>
    </div>
    <strong>{{ value }}</strong>
    <small v-if="hint">{{ hint }}</small>
  </section>
</template>

<script setup lang="ts">
import { computed, type Component } from 'vue'

const props = defineProps<{
  label: string
  value: string | number
  hint?: string
  tone?: 'normal' | 'good' | 'warn' | 'bad'
  icon?: Component
}>()

const toneClass = computed(() => `metric-card--${props.tone ?? 'normal'}`)
</script>
