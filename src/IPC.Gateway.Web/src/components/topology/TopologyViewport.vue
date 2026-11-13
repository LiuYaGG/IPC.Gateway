<template>
  <section class="topology-viewport">
    <div class="viewport-toolbar">
      <a-button :title="tx('缩小画布')" :aria-label="tx('缩小画布')" :disabled="scale <= minScale" @click="zoomAt(scale - 0.1)">
        <template #icon><ZoomOutOutlined /></template>
      </a-button>
      <output>{{ Math.round(scale * 100) }}%</output>
      <a-button :title="tx('放大画布')" :aria-label="tx('放大画布')" :disabled="scale >= maxScale" @click="zoomAt(scale + 0.1)">
        <template #icon><ZoomInOutlined /></template>
      </a-button>
      <a-button :title="tx('重置视图')" :aria-label="tx('重置视图')" @click="resetView">
        <template #icon><ReloadOutlined /></template>
      </a-button>
    </div>
    <div ref="surface" class="viewport-surface" :class="{ dragging }"
      @pointerdown="beginPan" @pointermove="pan" @pointerup="endPan" @pointercancel="endPan" @lostpointercapture="endPan"
      @click.capture="suppressDragClick" @dragstart.prevent @wheel="wheel">
      <div class="viewport-world" :style="{ transform: `translate(${x}px, ${y}px) scale(${scale})` }">
        <slot />
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
import { onDeactivated, ref } from 'vue'
import { ReloadOutlined, ZoomInOutlined, ZoomOutOutlined } from '@ant-design/icons-vue'
import { tx } from '../../i18n'

const surface = ref<HTMLElement>()
const minScale = 0.5
const maxScale = 3
const scale = ref(1)
const x = ref(0)
const y = ref(0)
const dragging = ref(false)
let pointer: { id: number; startX: number; startY: number; x: number; y: number } | undefined
let ignoreClick = false

function resetView() {
  x.value = y.value = 0
  scale.value = 1
}

function zoomAt(requested: number, event?: WheelEvent) {
  if (!surface.value) return
  const next = Math.min(maxScale, Math.max(minScale, Math.round(requested * 100) / 100))
  if (next === scale.value) return
  const rect = surface.value.getBoundingClientRect()
  const pivotX = event ? event.clientX - rect.left : rect.width / 2
  const pivotY = event ? event.clientY - rect.top : rect.height / 2
  const ratio = next / scale.value
  x.value = pivotX - (pivotX - x.value) * ratio
  y.value = pivotY - (pivotY - y.value) * ratio
  scale.value = next
}

function wheel(event: WheelEvent) {
  if (event.shiftKey || !event.deltaY || Math.abs(event.deltaX) > Math.abs(event.deltaY)) return
  event.preventDefault()
  const units = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? 160 : 1
  const pixels = Math.max(-180, Math.min(180, event.deltaY * units))
  zoomAt(scale.value * Math.exp(-pixels * 0.002), event)
}

function beginPan(event: PointerEvent) {
  if (!event.isPrimary || event.button !== 0 || !(event.target instanceof Element)) return
  // Let the channel list's own scrollbar handle its thumb and track.
  if (event.target instanceof HTMLElement && event.target.scrollWidth > event.target.clientWidth) {
    const target = event.target
    const rect = target.getBoundingClientRect()
    if ((event.clientY - rect.top) / scale.value >= target.clientHeight + target.clientTop) return
  }
  pointer = { id: event.pointerId, startX: event.clientX, startY: event.clientY, x: x.value, y: y.value }
  ignoreClick = false
  event.target.setPointerCapture(event.pointerId)
}

function pan(event: PointerEvent) {
  if (pointer?.id !== event.pointerId) return
  const dx = event.clientX - pointer.startX
  const dy = event.clientY - pointer.startY
  if (!dragging.value && Math.hypot(dx, dy) < 5) return
  dragging.value = true
  ignoreClick = true
  x.value = pointer.x + dx
  y.value = pointer.y + dy
}

function endPan(event: PointerEvent) {
  if (pointer?.id === event.pointerId) {
    pointer = undefined
    dragging.value = false
  }
}

function suppressDragClick(event: MouseEvent) {
  if (!ignoreClick || !event.detail) return
  ignoreClick = false
  event.preventDefault()
  event.stopPropagation()
}

onDeactivated(() => {
  pointer = undefined
  dragging.value = false
  ignoreClick = false
})
</script>

<style scoped>
.topology-viewport {
  display: flex;
  flex-direction: column;
  flex: 1 0 380px;
  min-width: 0;
  min-height: 380px;
  overflow: clip;
  border: 1px solid var(--edge);
  border-radius: 7px;
  background: radial-gradient(#d4e0eb .7px, transparent .8px) 0 0 / 14px 14px, #f4f8fb;
}
.viewport-toolbar {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 6px;
  padding: 6px 10px 0;
}
.viewport-toolbar output { min-width: 4ch; text-align: center; font-variant-numeric: tabular-nums; }
.viewport-surface {
  flex: 1 0 auto;
  display: grid;
  min-width: 0;
  overflow: clip;
  touch-action: none;
  user-select: none;
  cursor: grab;
}
.viewport-surface.dragging, .viewport-surface.dragging :deep(button) { cursor: grabbing; }
.viewport-world { display: grid; min-width: 0; transform-origin: 0 0; }
@container (max-width: 620px) {
  .topology-viewport { flex-basis: auto; }
}
</style>
