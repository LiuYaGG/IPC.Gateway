<template>
  <aside v-if="modelValue" class="value-detail-panel" :aria-label="tx('完整值详情')">
    <header class="value-detail-panel__header">
      <strong>{{ tx('完整值详情') }}</strong>
      <a-button type="text" :aria-label="tx('关闭完整值详情')" @click="emit('update:modelValue', false)">
        <template #icon>
          <span class="ui-icon"><Close /></span>
        </template>
      </a-button>
    </header>
    <div class="value-detail-panel__body">
      <template v-if="tag">
        <dl class="value-detail-meta">
          <dt>{{ tx('标签') }}</dt>
          <dd>{{ tag.name }}</dd>
          <dt>{{ tx('地址 / 类型') }}</dt>
          <dd>{{ tag.address }} / {{ tag.dataType }}</dd>
          <dt>{{ tx('采样时间') }}</dt>
          <dd>{{ formatDateTime(displayed?.timestamp) }}</dd>
          <dt>{{ tx('质量 / 单位') }}</dt>
          <dd>{{ tx(statusText(displayed?.quality || tx('尚无采样'))) }} / {{ displayed?.unit || tag.unit || '-' }}</dd>
        </dl>
        <div class="value-detail-controls">
          <a-radio-group v-model:value="kind" size="small" button-style="solid">
            <a-radio-button value="current">{{ tx('当前值') }}</a-radio-button>
            <a-radio-button value="raw">{{ tx('原始值') }}</a-radio-button>
          </a-radio-group>
          <a-button @click="togglePaused">{{ paused ? tx('继续实时更新') : tx('暂停更新') }}</a-button>
          <a-button :disabled="!displayed" @click="copyAll">{{ tx('复制全部') }}</a-button>
        </div>
        <p class="value-detail-summary" aria-live="polite">
          {{ paused ? tx('已暂停，显示暂停时的采样') : tx('实时更新') }} ·
          {{
            elements
              ? formatMessage('{0} 个元素（起始索引 {1}）', [elements.length, tag.elementOffset || 0])
              : formatMessage('{0} 个字符', [characterCount])
          }}
        </p>
        <a-alert v-if="!displayed" :message="tx('尚无采样数据')" type="info" />
        <template v-else-if="elements">
          <div class="value-array-header">
            <span>{{ tx('元素索引') }}</span>
            <span>{{ tx('数值') }}</span>
          </div>
          <div
            ref="scroller"
            class="value-array-scroll"
            tabindex="0"
            :aria-label="tx('数组元素')"
            @scroll="scrollTop = scroller?.scrollTop ?? 0"
          >
            <div class="value-array-spacer" :style="{ height: `${elements.length * 36}px` }">
              <div
                v-for="item in visibleElements"
                :key="item.index"
                class="value-array-row"
                :style="{ transform: `translateY(${item.index * 36}px)` }"
              >
                <span>{{ item.index + (tag.elementOffset || 0) }}</span>
                <span>{{ item.text }}</span>
              </div>
            </div>
          </div>
        </template>
        <pre v-else class="value-full-text" tabindex="0" :aria-label="tx('完整文本值')">{{
          text === '' ? tx('（空字符串）') : text
        }}</pre>
        <a-alert v-if="displayed?.errorMessage" :message="tx(displayed.errorMessage)" type="warning" />
      </template>
    </div>
  </aside>
</template>

<script setup lang="ts">
import { formatMessage } from '../i18n'
import { notify } from '../utils/ui'
import { tx } from '../i18n'
import { statusText } from '../utils/display'
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'

import { CloseOutlined as Close } from '@ant-design/icons-vue'
import type { TagConfig, TagValueSnapshot } from '../api'
import { formatDateTime } from '../utils/format'
import { tagArrayElements, visibleArrayRange } from '../utils/tagValueDetails'

const props = defineProps<{
  modelValue: boolean
  tag?: TagConfig
  snapshot?: TagValueSnapshot
  initialKind?: 'raw' | 'current'
}>()
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>()
const kind = ref<'current' | 'raw'>('current')
const paused = ref(false)
const frozen = ref<TagValueSnapshot>()
const displayed = computed(() => (paused.value ? frozen.value : props.snapshot))
const text = computed(() => (kind.value === 'raw' ? displayed.value?.rawValueText : displayed.value?.valueText) ?? '')
const characterCount = computed(() => [...text.value].length)
const elements = computed(() => tagArrayElements(props.tag?.dataType ?? '', text.value))
const scroller = ref<HTMLElement>()
const scrollTop = ref(0)
const viewportHeight = ref(360)
let observer: ResizeObserver | undefined
const visibleElements = computed(() => {
  const values = elements.value ?? []
  const range = visibleArrayRange(scrollTop.value, viewportHeight.value, values.length)
  return values.slice(range.start, range.end).map((text, offset) => ({ text, index: range.start + offset }))
})
watch(scroller, (element) => {
  observer?.disconnect()
  if (!element) return
  observer = new ResizeObserver(() => {
    viewportHeight.value = element.clientHeight
  })
  observer.observe(element)
})
watch(
  () => props.modelValue,
  () => {
    paused.value = false
    frozen.value = undefined
    kind.value = props.initialKind ?? 'current'
    scrollTop.value = 0
  }
)
watch([kind, () => elements.value?.length], async () => {
  await nextTick()
  if (scroller.value) {
    scroller.value.scrollTop = Math.min(
      scroller.value.scrollTop,
      Math.max(0, (elements.value?.length ?? 0) * 36 - scroller.value.clientHeight)
    )
    scrollTop.value = scroller.value.scrollTop
  }
})
onBeforeUnmount(() => observer?.disconnect())
function togglePaused() {
  frozen.value = props.snapshot ? { ...props.snapshot } : undefined
  paused.value = !paused.value
}
async function copyAll() {
  try {
    if (navigator.clipboard) await navigator.clipboard.writeText(text.value)
    else {
      const input = document.createElement('textarea')
      const focused = document.activeElement as HTMLElement | null
      input.value = text.value
      input.style.cssText = 'position:fixed;opacity:0;left:0;top:0;'
      document.body.append(input)
      try {
        input.select()
        if (!document.execCommand('copy')) throw new Error('Copy unavailable')
      } finally {
        input.remove()
        focused?.focus()
      }
    }
    notify.success('已复制完整值')
  } catch {
    notify.warning('复制失败，请手动选择完整值复制')
  }
}
</script>
