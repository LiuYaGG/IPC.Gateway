<template>
  <div class="history-point-field">
    <a-select
      :aria-label="label" :placeholder="label" :value="selected && optionKey(selected)"
      :options="options" :disabled="disabled" :loading="loading" :filter-option="false"
      allow-clear show-search :default-active-first-option="false" popup-class-name="history-point-options"
      @dropdown-visible-change="open = $event" @search="term = $event" @change="change"
    >
      <template #notFoundContent>
        <span>{{ loading ? tx('加载中') : tx('无匹配项') }}</span>
      </template>
    </a-select>
    <span v-if="error" role="alert" class="history-select-error">{{ tx(error) }}</span>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { loadHistoryRecordOptions, type HistoryOptionLevel, type HistoryRecordFilters, type HistoryRecordOption } from '../../api'
import { locale, tx } from '../../i18n'
import { historyQueryError } from './historyQueryError'

const props = defineProps<{
  level: HistoryOptionLevel
  label: string
  filters: HistoryRecordFilters
  selected?: HistoryRecordOption
  disabled?: boolean
  active: boolean
}>()
const emit = defineEmits<{ change: [option?: HistoryRecordOption] }>()
const open = ref(false)
const term = ref('')
const loading = ref(false)
const error = ref('')
const candidates = ref<HistoryRecordOption[]>([])
// Ant treats undefined as unselected. An empty group ID is a real, selectable group.
const optionKey = (item: HistoryRecordOption) => 'id:' + item.id
const options = computed(() => {
  const entries = [...candidates.value]
  if (props.selected && !entries.some(item => item.id === props.selected!.id)) entries.push(props.selected)
  return entries.map(item => ({
    value: optionKey(item),
    label: props.level === 'group' && !item.id ? tx('直属标签') : (item.name || item.id),
    title: item.path || item.name
  }))
})

watch([open, term, () => props.filters.channelId, () => props.filters.deviceId, () => props.filters.groupId, () => props.active, locale], (_, __, onCleanup) => {
  error.value = ''
  candidates.value = []
  loading.value = false
  if (!open.value || !props.active || props.disabled) {
    term.value = ''
    return
  }
  const controller = new AbortController()
  loading.value = true
  const timer = window.setTimeout(async () => {
    try {
      const keyword = term.value.trim()
      const findDirect = props.level === 'group' && keyword !== '' &&
        ['直属标签', 'direct tags', tx('直属标签')].some(label => label.toLowerCase().includes(keyword.toLowerCase()))
      const matching = await loadHistoryRecordOptions(props.level, props.filters, keyword, locale.value, controller.signal)
      if (findDirect && !matching.some(item => item.id === '')) {
        const groups = await loadHistoryRecordOptions('group', props.filters, '', locale.value, controller.signal)
        const direct = groups.find(item => item.id === '')
        if (direct) matching.unshift(direct)
      }
      if (!controller.signal.aborted) candidates.value = matching
    } catch (cause) {
      if (!controller.signal.aborted) error.value = historyQueryError(cause, '读取点位候选失败')
    } finally {
      if (!controller.signal.aborted) loading.value = false
    }
  }, term.value ? 220 : 0)
  onCleanup(() => { clearTimeout(timer); controller.abort() })
})
function change(key: unknown) {
  const selected = candidates.value.find(item => optionKey(item) === key)
  emit('change', selected)
}
</script>

<style scoped>
.history-point-field { min-width: 0; }
.history-point-field .ant-select { width: 100%; }
.history-select-error { display: block; color: #b42318; font-size: 12px; }
</style>
