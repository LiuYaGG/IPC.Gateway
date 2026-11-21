<template>
  <a-modal :open="true" centered :title="text('规则历史回放')" :width="960" @cancel="close">
    <div class="replay-body">
      <div class="replay-control">
        <div class="replay-identity"><strong>{{ rule.name }}</strong>
          <a-tag color="blue">{{ text('隔离回放') }}</a-tag></div>
        <a-form layout="vertical" class="replay-dates">
          <a-form-item :label="text('规则版本')">
            <a-select v-model:value="selectedVersion" :options="versions" :disabled="running || !savedRule" />
          </a-form-item>
          <a-form-item :label="text('开始时间')">
            <a-date-picker v-model:value="from" :disabled="running" show-time format="YYYY-MM-DD HH:mm:ss" />
          </a-form-item>
          <a-form-item :label="text('结束时间')">
            <a-date-picker v-model:value="to" :disabled="running" show-time format="YYYY-MM-DD HH:mm:ss" />
          </a-form-item>
          <a-space class="replay-run">
            <a-button v-if="running" @click="stop">{{ text('停止回放') }}</a-button>
            <a-button type="primary" :loading="running" :disabled="!from || !to" @click="run">{{ text('开始回放') }}</a-button>
          </a-space>
        </a-form>
      </div>
      <a-alert show-icon type="info" :message="text('回放使用历史样本，动作仅作预览。')" />
      <a-alert v-if="error" show-icon type="error" :message="error" />
      <a-alert v-if="stopped" show-icon type="info" :message="text('回放已停止。')" />
      <template v-if="report">
        <div class="replay-summary">
          <span>{{ text('输入点位') }} <strong>{{ report.coverage.length }}</strong></span>
          <span>{{ text('样本数') }} <strong>{{ report.coverage.reduce((count, point) => count + point.samples, 0) }}</strong></span>
          <span>{{ text('触发') }} <strong>{{ report.triggered }}</strong></span>
          <span>{{ text('恢复') }} <strong>{{ report.cleared }}</strong></span>
          <a-tag :color="limited ? 'orange' : 'green'">
            {{ text(limited ? '覆盖有限' : '已完成') }}
          </a-tag>
        </div>
        <section>
          <h3>{{ text('历史数据覆盖') }}</h3>
          <a-table :data-source="report.coverage" :columns="coverageColumns" row-key="tagId" :pagination="false"
            size="small" :scroll="{ x: 585, y: 200 }">
            <template #bodyCell="{ column, record }">
              <template v-if="column.key === 'tag'"><strong>{{ record.name }}</strong><small class="replay-path">{{ record.path }}</small></template>
              <template v-else-if="column.key === 'coverage'">
                <a-tag v-if="!record.first" color="red">{{ text('无可用记录') }}</a-tag>
                <a-tag v-else-if="!record.preheated || !record.samples" color="orange">{{ text('覆盖有限') }}</a-tag>
                <a-tag v-else color="green">{{ text('有可用记录') }}</a-tag>
              </template>
              <template v-else-if="column.key === 'first'">{{ time(record.first) }}</template>
              <template v-else-if="column.key === 'last'">{{ time(record.last) }}</template>
            </template>
          </a-table>
          <ul class="replay-warnings">
            <li v-for="warning in report.warnings" :key="warning">{{ warn(warning) }}</li>
            <li v-if="report.truncated">{{ text('事件明细已达到展示上限。') }}</li>
          </ul>
        </section>
        <section class="replay-events">
          <div>
          <div class="replay-heading">
            <h3>{{ text('回放事件') }}</h3>
            <span>{{ text('计算输出') }} {{ report.outputs }} · {{ text('求值错误') }} {{ report.errors }}</span>
            <a-select v-model:value="filter" size="small" :aria-label="text('事件类型')" :options="filters" />
          </div>
          <a-table :data-source="visibleEvents" :columns="eventColumns" :row-key="eventKey" size="small"
            :pagination="{ pageSize: 10, hideOnSinglePage: true, showSizeChanger: false }" :scroll="{ x: 600, y: 290 }">
            <template #bodyCell="{ column, record }">
              <template v-if="column.key === 'time'">{{ time(record.timestamp) }}</template>
              <a-tag v-else-if="column.key === 'type'" :color="record.eventType === 'active' ? 'orange' : record.eventType === 'clear' ? 'green' : 'blue'">{{ eventType(record.eventType) }}</a-tag>
              <a-button v-else-if="column.key === 'detail'" type="link" size="small" @click="selected = record">{{ text('详情') }}</a-button>
            </template>
          </a-table>
          </div>
          <div v-if="selected" class="replay-event-detail">
            <div class="replay-heading">
              <strong>{{ text('事件明细') }} · {{ time(selected.timestamp) }}</strong>
              <a-button type="text" size="small" @click="selected = undefined">{{ text('关闭详情') }}</a-button>
            </div>
            <p>{{ selected.tagName }} · {{ text('历史输入') }} {{ selected.originalValue || '—' }} · {{ tx(selected.quality) }}</p>
            <p>{{ text('规则判断') }} · {{ tx(selected.condition) }} · {{ tx(selected.state) }}</p>
            <p v-if="selected.threshold != null">{{ text('判断参考值') }} · {{ selected.threshold }}</p>
            <p>{{ selected.message }}</p>
            <h4>{{ text('动作预览') }}</h4>
            <div v-if="selected.actions.length">
              <div v-for="(action, index) in selected.actions" :key="index">
                <a-tag>{{ actionName(action.type) }}</a-tag>{{ action.topic || '' }}
                <span v-if="action.delaySeconds"> · {{ text('延迟') }} {{ action.delaySeconds }} s</span>
                <pre v-if="action.payload">{{ action.payload }}</pre>
              </div>
            </div>
            <p v-else>{{ text('本事件没有动作输出。') }}</p>
          </div>
        </section>
      </template>
    </div>
    <template #footer><a-button @click="close">{{ text('关闭') }}</a-button></template>
  </a-modal>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import dayjs, { type Dayjs } from 'dayjs'
import { request, type ApiResult, type FlowRuleDefinition } from '../api'
import { locale, tx } from '../i18n'

interface Coverage {
  tagId: string
  name: string
  path: string
  samples: number
  excluded: number
  first?: string
  last?: string
  preheated: boolean
}
interface ActionPreview { type: string; topic?: string; payload?: string; delaySeconds: number }
interface ReplayEvent {
  timestamp: string
  eventType: string
  state: string
  condition: string
  message: string
  value?: number
  threshold?: number
  tagName: string
  quality: string
  originalValue: string
  actions: ActionPreview[]
}
interface Report {
  coverage: Coverage[]
  events: ReplayEvent[]
  triggered: number
  cleared: number
  outputs: number
  errors: number
  truncated: boolean
  warnings: string[]
}

const props = defineProps<{ rule: FlowRuleDefinition; draft: boolean; savedRule?: FlowRuleDefinition }>()
const emit = defineEmits<{ close: [] }>()
const from = ref<Dayjs | null>(dayjs().subtract(1, 'hour').startOf('minute'))
const to = ref<Dayjs | null>(dayjs())
const running = ref(false)
const stopped = ref(false)
const error = ref('')
const report = shallowRef<Report>()
const selected = shallowRef<ReplayEvent>()
const filter = ref('all')
const selectedVersion = ref(props.draft ? 'draft' : 'saved')
let currentRequest: AbortController | undefined
const labels: Record<string, string> = {
  '规则历史回放': 'Rule history replay', '当前草稿': 'Current draft', '已保存版本': 'Saved version',
  '规则版本': 'Rule version', '隔离回放': 'Isolated replay', '输入点位': 'Input tags', '已完成': 'Completed',
  '开始时间': 'From', '结束时间': 'To', '开始回放': 'Run replay', '停止回放': 'Stop replay',
  '回放使用历史样本，动作仅作预览。': 'Replays stored samples and previews actions.',
  '回放已停止。': 'Replay stopped.', '历史数据覆盖': 'Historical data coverage', '点位': 'Tag', '样本数': 'Samples',
  '记录状态': 'Coverage', '无可用记录': 'No usable records', '覆盖有限': 'Limited coverage', '有可用记录': 'Records available',
  '首条记录': 'First record', '末条记录': 'Last record', '回放事件': 'Replay events', '触发': 'Triggered',
  '恢复': 'Cleared', '计算输出': 'Computed output', '求值错误': 'Evaluation errors', '事件类型': 'Event type',
  '全部事件': 'All events', '时间': 'Time', '状态': 'State', '结果值': 'Value', '操作': 'Action',
  '详情': 'Details', '事件明细': 'Event details', '关闭详情': 'Close details', '历史输入': 'Recorded input',
  '规则判断': 'Rule condition', '判断参考值': 'Comparison reference',
  '动作预览': 'Action preview', '本事件没有动作输出。': 'This event has no output actions.', '延迟': 'Delay',
  '事件明细已达到展示上限。': 'The event detail display limit has been reached.', '关闭': 'Close',
  '请选择有效时间，最长可回放 24 小时。': 'Choose a valid time range of up to 24 hours.',
  '回放失败。': 'Replay failed.', 'MQTT 发布': 'MQTT publish', '报警记录': 'Alarm record', '邮件通知': 'Email', 'Webhook 调用': 'Webhook', '调试输出': 'Debug output'
}
const warnings: Record<string, [string, string]> = {
  EVENT_HISTORY: ['历史库可能按变化或过滤记录，结果仅代表已存样本和虚拟时钟下的模拟。', 'History may be stored on change or filtered. Results are a simulation based on recorded samples and a virtual clock.'],
  DERIVED_SKIPPED: ['聚合、补值或时间对齐的记录已跳过。', 'Aggregated, filled and time-aligned records were skipped.'],
  NO_BASELINE: ['部分点位缺少起点前的样本。已有输入首次齐备后才显示模拟事件。', 'Some tags lack samples before the start. Events are shown once all required inputs have been observed.'],
  WARMUP_LIMIT: ['规则需要超过 30 分钟的预热，靠近回放起点的结果可能不完整。', 'The rule requires more than 30 minutes of warm-up. Results near the start may be incomplete.'],
  INPUT_WINDOW_EMPTY: ['部分点位在所选时间内无原始记录，期间状态无法完整判定。', 'Some tags have no original records in the selected range. Their state cannot be fully determined.'],
  NO_CHANGE_LIMITED: ['连续无变化判断需要有效的样本间隔。历史库只存变化时，记录空白无法证明点位持续正常。', 'No-change detection requires valid sample timing. A gap in change-only history cannot prove that the input kept producing valid samples.']
}
const text = (label: string) => locale.value === 'en-US' ? labels[label] || tx(label) : label
const limited = computed(() => report.value?.warnings.some(code => ['NO_BASELINE', 'WARMUP_LIMIT', 'INPUT_WINDOW_EMPTY'].includes(code)) ?? false)
const warn = (code: string) => warnings[code]?.[locale.value === 'en-US' ? 1 : 0] ?? code
const time = (value?: string) => value ? dayjs(value).format('YYYY-MM-DD HH:mm:ss.SSS') : '—'
const eventType = (value: string) => text(({ active: '触发', clear: '恢复', value: '计算输出' } as Record<string, string>)[value] ?? value)
const actionName = (value: string) => text(({ MqttPublish:'MQTT 发布', AlarmRecord:'报警记录', EmailNotify:'邮件通知', WebhookCall:'Webhook 调用', DebugProbe:'调试输出' } as Record<string, string>)[value] ?? value)
const versions = computed(() => {
  const saved = props.draft ? props.savedRule : props.rule
  return [
    ...(saved ? [{ label: text('已保存版本') + ' ' + saved.version, value: 'saved' }] : []),
    ...(props.draft ? [{ label: text('当前草稿'), value: 'draft' }] : [])
  ]
})
const filters = computed(() => ['all', 'active', 'clear', 'value'].map(value => ({ value, label: value === 'all' ? text('全部事件') : eventType(value) })))
const visibleEvents = computed(() => (report.value?.events ?? []).map((event, index) => ({ ...event, index }))
  .filter(event => filter.value === 'all' || event.eventType === filter.value))
const eventKey = (event: ReplayEvent & { index: number }) => event.index
const coverageColumns = computed(() => [
  { title: text('点位'), key: 'tag', width: 190 }, { title: text('样本数'), dataIndex: 'samples', width: 70 },
  { title: text('记录状态'), key: 'coverage', width: 130 }, { title: text('首条记录'), key: 'first', width: 175 },
  { title: text('末条记录'), key: 'last', width: 175 }
])
const eventColumns = computed(() => [
  { title: text('时间'), key: 'time', width: 185 }, { title: text('事件类型'), key: 'type', width: 120 },
  { title: text('点位'), dataIndex: 'tagName', ellipsis: true, width: 130 },
  { title: text('结果值'), dataIndex: 'value', width: 100 },
  { title: text('操作'), key: 'detail', fixed: 'right' as const, width: 75 }
])

function stop() {
  currentRequest?.abort()
  currentRequest = undefined
  running.value = false
  stopped.value = true
}
function close() {
  if (running.value) stop()
  emit('close')
}
async function run() {
  if (running.value) return
  error.value = ''
  stopped.value = false
  if (!from.value?.isValid() || !to.value?.isValid() || from.value >= to.value || to.value.diff(from.value) > 86_400_000) {
    error.value = text('请选择有效时间，最长可回放 24 小时。')
    return
  }
  const controller = new AbortController()
  currentRequest = controller
  running.value = true
  report.value = undefined
  selected.value = undefined
  filter.value = 'all'
  try {
    const range = { from: from.value.toISOString(), to: to.value.toISOString() }
    const rule = props.savedRule ?? props.rule
    const result = await request<ApiResult<Report>>('/api/diagnostics/rules/replay', {
      method: 'POST',
      signal: controller.signal,
      headers: { 'Content-Type': 'application/json', 'Accept-Language': locale.value },
      body: JSON.stringify(selectedVersion.value === 'draft' ? { ...range, draft: props.rule } : { ...range, ruleId: rule.id, version: rule.version })
    })
    if (!controller.signal.aborted) {
      if (result.success) report.value = result.data
      else error.value = result.errorMessage || text('回放失败。')
    }
  } catch (failure) {
    if (!controller.signal.aborted) error.value = failure instanceof Error ? failure.message : text('回放失败。')
  } finally {
    if (currentRequest === controller) {
      currentRequest = undefined
      running.value = false
    }
  }
}
onBeforeUnmount(() => currentRequest?.abort())
watch(selectedVersion, () => { report.value = undefined; selected.value = undefined; error.value = ''; stopped.value = false })
</script>

<style scoped>
.replay-body { display: grid; gap: 14px; max-height: max(230px, calc(100dvh - 154px)); overflow: auto; padding: 1px 3px 10px; }
.replay-body h3, .replay-body h4 { margin: 0 0 6px; font-size: 1em; }
.replay-identity, .replay-heading { display: flex; align-items: center; flex-wrap: wrap; gap: 8px 12px; }
.replay-dates { display: flex; align-items: end; gap: 10px 12px; flex-wrap: wrap; margin-top: 7px; }
.replay-dates :deep(.ant-form-item) { margin-bottom: 0; }
.replay-dates :deep(.ant-select) { min-width: 130px; }
.replay-path { display: block; color: #526779; overflow-wrap: anywhere; }
.replay-heading .ant-select { min-width: 104px; margin-inline-start: auto; }
.replay-warnings { padding-inline-start: 20px; margin-block: 6px 0; color: #586b80; }
.replay-summary { display: flex; align-items: center; flex-wrap: wrap; gap: 6px 20px; padding: 8px 10px; border: 1px solid #d9e4ef; border-radius: 6px; }
.replay-events:has(.replay-event-detail) { display: grid; grid-template-columns: minmax(0, 1.5fr) minmax(0, 1fr); gap: 12px; align-items: start; }
.replay-events > div { min-width: 0; }
.replay-event-detail { margin-top: 10px; padding: 8px 12px; border: 1px solid #d9e4ef; border-radius: 6px; overflow-wrap: anywhere; }
.replay-event-detail p { margin: 4px 0; }
.replay-event-detail pre { margin: 4px 0; padding: 6px 8px; white-space: pre-wrap; background: #f2f6fa; }
@media (max-width: 760px) {
  .replay-events:has(.replay-event-detail) { grid-template-columns: minmax(0, 1fr); }
}
@media (max-width: 560px) {
  .replay-dates { display: grid; grid-template-columns: minmax(0, 1fr); }
  .replay-dates :deep(.ant-picker) { width: 100%; }
}
</style>
