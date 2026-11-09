<template>
  <span
    class="column-resize-handle"
    role="separator"
    tabindex="0"
    aria-orientation="vertical"
    :aria-label="formatMessage('调整{0}列宽', [label])"
    :title="tx('拖动调整列宽')"
    :aria-valuemin="80"
    :aria-valuemax="1200"
    :aria-valuenow="width"
    @pointerdown.stop.prevent="start"
    @pointermove.stop="move"
    @pointerup.stop="finish"
    @pointercancel="finish"
    @lostpointercapture="finish"
    @keydown.left.stop.prevent="adjust(-10, $event)"
    @keydown.right.stop.prevent="adjust(10, $event)"
  />
</template>
<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { formatMessage, tx } from '../../i18n'
import { columnWidth } from '../../utils/columnWidths'
const props = defineProps<{ label: string; width?: number }>()
const emit = defineEmits<{ resize: [width: number] }>()
const active = ref(false)
let startX = 0
let startWidth = 0
let handle: HTMLElement | null = null
function start(event: PointerEvent) {
  if (event.button !== 0) return
  handle = event.currentTarget as HTMLElement
  startX = event.clientX
  startWidth = handle.parentElement!.getBoundingClientRect().width
  handle.setPointerCapture(event.pointerId)
  active.value = true
  document.body.classList.add('is-resizing-column')
}
function move(event: PointerEvent) {
  if (active.value) emit('resize', columnWidth(startWidth + event.clientX - startX))
}
function finish() {
  active.value = false
  document.body.classList.remove('is-resizing-column')
}
function adjust(delta: number, event: KeyboardEvent) {
  const element = event.currentTarget as HTMLElement
  emit('resize', columnWidth((props.width ?? element.parentElement!.getBoundingClientRect().width) + delta))
}
onBeforeUnmount(finish)
</script>
