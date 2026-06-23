<template>
  <div class="gauge-ring" :class="toneClass">
    <svg viewBox="0 0 112 112" role="img" :aria-label="label">
      <circle class="gauge-ring__track" cx="56" cy="56" r="44" />
      <circle
        class="gauge-ring__value"
        cx="56"
        cy="56"
        r="44"
        :stroke-dasharray="circumference"
        :stroke-dashoffset="dashOffset"
      />
    </svg>
    <div class="gauge-ring__content">
      <strong>{{ displayValue }}</strong>
      <span>{{ label }}</span>
      <small v-if="caption">{{ caption }}</small>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  label: string
  value: number
  suffix?: string
  caption?: string
  tone?: 'normal' | 'good' | 'warn' | 'bad'
}>()

const radius = 44
const circumference = Number((2 * Math.PI * radius).toFixed(2))
const normalizedValue = computed(() => Math.max(0, Math.min(100, Number(props.value) || 0)))
const dashOffset = computed(() => Number((circumference * (1 - normalizedValue.value / 100)).toFixed(2)))
const displayValue = computed(() => `${Number(normalizedValue.value.toFixed(1))}${props.suffix ?? ''}`)
const toneClass = computed(() => `gauge-ring--${props.tone ?? 'normal'}`)
</script>
