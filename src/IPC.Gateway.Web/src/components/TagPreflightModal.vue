<template>
  <a-modal :open="true" centered :width="1000" :title="tx('点表上线前检查')" :mask-closable="!applying"
    :closable="!applying" :keyboard="!applying" @cancel="close">
    <div class="preflight">
      <div class="scope">
        <label>{{ l('检查范围', 'Scope') }}</label>
        <a-select v-model:value="scope" :options="scopes" :disabled="applying" show-search option-filter-prop="label" />
        <a-tag v-if="filename">{{ filename }}</a-tag>
        <a-button :loading="busy" :disabled="applying || sampling" @click="check">{{ l('重新检查', 'Run checks') }}</a-button>
      </div>
      <a-alert v-if="error" type="error" show-icon :message="error" />
      <div v-if="report" class="summary" aria-live="polite">
        <span>{{ l('检查点位', 'Tags checked') }} <b>{{ report.checked }}</b></span>
        <a-tag :color="report.errors ? 'red' : 'green'">{{ l('阻断', 'Blocking') }} {{ report.errors }}</a-tag>
        <a-tag :color="report.warnings ? 'orange' : 'default'">{{ l('警告', 'Warnings') }} {{ report.warnings }}</a-tag>
        <template v-if="csv !== undefined">
          <span>{{ l('新增', 'New') }} <b>{{ report.added }}</b></span>
          <span>{{ l('更新', 'Updates') }} <b>{{ report.updated }}</b></span>
        </template>
      </div>
      <a-table v-if="report" size="small" :columns="columns" :data-source="rows" row-key="key"
        :pagination="rows.length > 10 ? { pageSize: 10, showSizeChanger: false } : false" :scroll="{ x: 650 }">
        <template #emptyText>{{ l('检查完成，未发现问题', 'Checks complete. No issues found.') }}</template>
        <template #bodyCell="{ column, record }">
          <template v-if="column.key === 'severity'">
            <a-tag :color="record.severity === 'error' ? 'red' : 'orange'">
              {{ record.severity === 'error' ? l('阻断', 'Blocking') : l('警告', 'Warning') }}
            </a-tag>
          </template>
          <template v-if="column.key === 'path'">
            <div>{{ record.path }}</div><small>{{ record.address }}</small>
            <small v-if="record.row">CSV · {{ l('行', 'Row') }} {{ record.row }}</small>
          </template>
          <div v-if="column.key === 'message'" class="preflight-finding">
            {{ issueText(record.code) }} <span v-if="record.detail">{{ tx(record.detail) }}</span>
          </div>
          <template v-if="column.key === 'action'">
            <a-button v-if="record.tagId && csv === undefined" type="link" size="small" @click="emit('locate', record.tagId, record.groupId)">
              {{ l('定位', 'Locate') }}</a-button><span v-else>—</span>
          </template>
        </template>
      </a-table>
      <small v-if="report && report.errors + report.warnings > rows.length">
        {{ l('阻断和警告分别显示前 250 条，统计包含全部结果。', 'Showing the first 250 issues of each severity. Totals include all results.') }}
      </small>
      <section v-if="canSample && csv === undefined" class="sample">
        <div class="sample-controls"><label>{{ l('只读抽测', 'Read-only sample') }}</label>
          <a-select mode="multiple" show-search option-filter-prop="label" v-model:value="sampleIds" :options="sampleOptions"
            :max-tag-count="2" :placeholder="l('选择 1 至 8 个点位', 'Select 1 to 8 tags')" :disabled="sampling" />
          <a-button :loading="sampling" :disabled="!sampleIds.length || busy" @click="sample">{{ l('执行抽读', 'Read samples') }}</a-button>
        </div>
        <a-alert v-if="sampleError" type="error" :message="sampleError" show-icon />
        <template v-if="sampleResult">
          <a-alert v-if="sampleResult.error" type="warning" :message="tx(sampleResult.error)" show-icon />
          <div v-for="item in sampleResult.items" :key="item.index" class="sample-row">
            <code>{{ item.address }}</code><a-tag :color="item.status === 'Success' ? 'green' : 'orange'">{{ sampleState(item.status) }}</a-tag>
            <span>{{ item.error ? tx(item.error) : item.value }}</span>
          </div>
        </template>
      </section>
    </div>
    <template #footer>
      <a-button :disabled="applying" @click="close">{{ l('关闭', 'Close') }}</a-button>
      <a-button v-if="csv !== undefined" type="primary" :loading="applying"
        :disabled="busy || !report || report.errors > 0 || !previewToken" @click="apply">{{ l('确认导入', 'Import tags') }}</a-button>
    </template>
  </a-modal>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { request, HttpRequestError, type ApiResult, type DeviceConfig } from '../api'
import { locale, tx } from '../i18n'
import { PERMISSIONS, usePermissions } from '../utils/permissions'
import { notify } from '../utils/ui'

interface Issue { severity: string; code: string; path: string; address: string; detail: string; row: number; tagId: string; groupId: string }
interface Report { errors: number; warnings: number; checked: number; added: number; updated: number; issues: Issue[] }
interface Preview { report: Report; previewToken?: string }
interface Sample { error: string; items: { index: number; address: string; status: string; error: string; value: string }[] }
const props = defineProps<{ device: DeviceConfig; groupId?: string; csv?: string; filename?: string }>()
const emit = defineEmits<{ close: []; imported: []; locate: [tagId: string, groupId: string] }>()
const { hasPermission } = usePermissions()
const canSample = computed(() => hasPermission(PERMISSIONS.protocolDiagnosticsRun))
const l = (zh: string, en: string) => locale.value === 'en-US' ? en : zh
const scope = ref(props.groupId ?? '')
const scopes = computed(() => [{ value: '', label: props.device.name + ' · ' + l('全部点位', 'All tags') },
  ...props.device.groups.map(g => ({ value: g.id, label: g.name }))])
const sampleIds = ref<string[]>([])
const sampleOptions = computed(() => {
  const direct = scope.value ? [] : props.device.tags.map(tag => ({ tag, prefix: '' }))
  const groups = props.device.groups.filter(g => g.enabled && (!scope.value || g.id === scope.value))
    .flatMap(g => g.tags.map(tag => ({ tag, prefix: g.name + ' / ' })))
  return [...direct, ...groups].filter(({ tag }) => tag.enabled && !tag.isVirtual && tag.accessMode !== 'WriteOnly')
    .map(({ tag, prefix }) => ({ value: tag.id, label: prefix + tag.name + ' · ' + tag.address,
      disabled: sampleIds.value.length >= 8 && !sampleIds.value.includes(tag.id) }))
})
const report = ref<Report>()
const previewToken = ref('')
const sampleResult = ref<Sample>()
const busy = ref(false), sampling = ref(false), applying = ref(false)
const error = ref(''), sampleError = ref('')
let pending: AbortController | undefined
let read: AbortController | undefined
const rows = computed(() => (report.value?.issues ?? []).map((issue, key) => ({ ...issue, key }))
  .sort((a, b) => a.severity.localeCompare(b.severity) || a.row - b.row))
const columns = computed(() => [
  { key: 'severity', title: l('级别', 'Severity'), width: 88 },
  { key: 'path', title: l('点位 / 地址', 'Tag / address'), width: '31%' },
  { key: 'message', title: l('检查结果', 'Finding') },
  { key: 'action', title: l('操作', 'Action'), width: 80, fixed: 'right' as const }
])
const messages: Record<string, [string, string]> = {
  duplicateColumn: ['CSV 表头重复', 'Duplicate CSV column'], missingColumns: ['缺少必填列', 'Missing required columns'],
  csvColumns: ['字段数量与表头不符', 'Column count differs from the header'], wrongDevice: ['请核对行中的通道和设备', 'Check the channel and device in this row'],
  missingIdentity: ['必须填写 tagId 和 tagName', 'Both tagId and tagName are required'], duplicateRow: ['文件中重复出现此标签', 'Repeated tag in this file'],
  invalidField: ['字段值无效', 'Invalid field value'], emptyFile: ['文件没有数据行', 'The file contains no data rows'],
  malformedCsv: ['CSV 引号或字段格式错误', 'Malformed CSV quotes or fields'], missingDriver: ['所选驱动未加载', 'The configured driver is unavailable'],
  wrongGroup: ['此行位于所选分组以外', 'This row is outside the selected group'], duplicateId: ['标签 ID 重复', 'Duplicate tag ID'],
  duplicateName: ['同层存在相同标签名', 'Duplicate tag name at this level'], invalidPath: ['路径名称或 ID 含有保留字符 /', 'Name or ID contains the reserved slash'],
  duplicateGroupName: ['设备中已存在同名分组', 'A group with this name already exists on the device'],
  duplicateGroupId: ['分组 ID 已由其他设备使用', 'Another device already uses this group ID'],
  validation: ['配置校验', 'Configuration validation'], missingReference: ['虚拟标签引用的点位不存在', 'Missing virtual tag input'],
  missingRuleReference: ['规则引用的点位不存在', 'Rule references a missing tag'], driverAccess: ['配置的读写权限超出驱动能力', 'Configured access exceeds driver capabilities'],
  readOnlyArea: ['地址属于只读区域，请核对写权限', 'The address is read-only. Check write access.'],
  mirror: ['与另一个点位共用地址，请确认是预期的镜像', 'Shared address. Verify that the alias is intentional.'],
  overlap: ['读写地址范围与另一个点位部分重叠', 'Address range overlaps another tag.'],
  stalePreview: ['配置或文件已变更，请重新检查后导入。', 'Configuration or file changed. Run checks again before importing.'],
  forbidden: ['没有执行此操作的权限。', 'Permission denied.'], fileSize: ['CSV 文件最大为 8 MiB。', 'Maximum CSV size is 8 MiB.'],
  busy: ['其他点表检查正在运行，请稍后再试。', 'Preflight is busy. Try again shortly.'],
  invalidScope: ['设备或分组不存在。', 'Device or group unavailable.'], blocked: ['请先修正阻断项。', 'Resolve blocking issues first.'],
  sampleSelection: ['请选择 1 至 8 个可读点位。', 'Select 1 to 8 readable tags.'],
  disabled: ['设备或通道不存在或已停用。', 'Device or channel unavailable or disabled.']
}
function issueText(key: string) { const pair = messages[key]; return pair ? l(...pair) : key }
function message(e: unknown) {
  if (e instanceof HttpRequestError && e.code && e.code !== 'validation' && messages[e.code]) return issueText(e.code)
  return e instanceof Error ? tx(e.message) : String(e)
}
function sampleState(s: string) { return s === 'Success' ? l('成功', 'Success') : s === 'Failed' ? l('失败', 'Failed') : l('未执行', 'Not attempted') }
function payload() { return { channelId: props.device.channelId, deviceId: props.device.id, groupId: scope.value || null, csv: props.csv } }
function post<T>(suffix: string, body: object, signal?: AbortSignal) {
  return request<ApiResult<T>>('/api/commercial/tags/preflight' + suffix, { method: 'POST', body: JSON.stringify(body), signal })
}
async function check() {
  pending?.abort()
  const controller = pending = new AbortController()
  busy.value = true; report.value = undefined; previewToken.value = ''; error.value = ''
  try {
    const { data } = await post<Preview>('', payload(), controller.signal)
    if (!controller.signal.aborted) { report.value = data.report; previewToken.value = data.previewToken ?? '' }
  } catch (e) { if (!controller.signal.aborted) error.value = message(e) }
  finally { if (pending === controller) busy.value = false }
}
async function sample() {
  const controller = read = new AbortController()
  sampling.value = true; sampleError.value = ''; sampleResult.value = undefined
  try {
    const { data } = await post<Sample>('/sample', { ...payload(), tagIds: sampleIds.value }, controller.signal)
    if (!controller.signal.aborted) sampleResult.value = data
  } catch (e) { if (!controller.signal.aborted) sampleError.value = message(e) }
  finally { if (read === controller) sampling.value = false }
}
async function apply() {
  if (applying.value || !previewToken.value || report.value?.errors !== 0) return
  applying.value = true; error.value = ''
  try {
    const { data } = await post<Preview>('/apply', { ...payload(), previewToken: previewToken.value })
    notify.success(l('点位导入完成', 'Tags imported') + ' · +' + data.report.added + ' / ~' + data.report.updated)
    emit('imported')
  } catch (e) {
    error.value = message(e)
    if (e instanceof HttpRequestError && (e.status === 409 || e.status === 422)) previewToken.value = ''
  } finally { applying.value = false }
}
function close() { if (!applying.value) emit('close') }
watch(scope, () => {
  read?.abort(); sampleResult.value = undefined; sampleError.value = ''; sampleIds.value = []; void check()
}, { immediate: true })
onBeforeUnmount(() => { pending?.abort(); read?.abort() })
</script>

<style scoped>
.preflight { display:grid; gap:14px; min-width:0; max-height:65dvh; overflow:auto; }
.scope,.summary,.sample-controls,.sample-row { display:flex; align-items:center; gap:10px; flex-wrap:wrap; }
.scope .ant-select,.sample-controls .ant-select { flex:1 1 220px; min-width:0; }
.summary b { font-variant-numeric:tabular-nums; margin-inline-start:4px; }
.summary .ant-tag { margin:0; }
.preflight small { display:block; color:#53657a; }
.preflight :deep(td) { overflow-wrap:anywhere; }
.preflight-finding { white-space:pre-line; overflow-wrap:anywhere; }
.sample { display:grid; gap:10px; border-top:1px solid #dfE6ed; padding-top:12px; }
.sample-row { overflow-wrap:anywhere; }
@media (max-width:600px) { .scope label,.sample-controls label { flex-basis:100%; } }
</style>
