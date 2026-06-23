<template>
  <div class="sparkline">
    <div class="sparkline__meta">
      <span>{{ title }}</span>
      <strong>{{ latestLabel }}</strong>
    </div>
    <svg viewBox="0 0 360 128" role="img" :aria-label="title">
      <line x1="0" y1="104" x2="360" y2="104" class="sparkline__axis" />
      <polyline v-if="areaPoints" :points="areaPoints" class="sparkline__area" />
      <polyline v-if="linePoints" :points="linePoints" class="sparkline__line" />
      <circle v-if="latestPoint" :cx="latestPoint.x" :cy="latestPoint.y" r="4" class="sparkline__dot" />
    </svg>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  title: string
  values: number[]
  suffix?: string
  max?: number
}>()

const width = 360
const top = 14
const bottom = 104

const points = computed(() => {
  const values = props.values.slice(-48)
  if (values.length === 0) return []
  const maxValue = Math.max(props.max ?? 0, ...values, 1)
  return values.map((value, index) => {
    const x = values.length === 1 ? width : (index / (values.length - 1)) * width
    const ratio = Math.max(0, Math.min(1, value / maxValue))
    const y = bottom - ratio * (bottom - top)
    return { x, y, value }
  })
})

const linePoints = computed(() => points.value.map(point => `${point.x.toFixed(1)},${point.y.toFixed(1)}`).join(' '))
const areaPoints = computed(() => {
  if (!linePoints.value) return ''
  return `0,${bottom} ${linePoints.value} ${width},${bottom}`
})
const latestPoint = computed(() => points.value.at(-1))
const latestLabel = computed(() => {
  const latest = latestPoint.value?.value
  if (latest === undefined) return '-'
  return `${Number(latest.toFixed(1))}${props.suffix ?? ''}`
})
</script>
