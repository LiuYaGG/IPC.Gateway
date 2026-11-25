<template>
  <div class="protocol-bench">
    <section class="bench-panel">
      <a-alert v-if="!haCanDispatch" type="info" show-icon :message="tx('本机当前禁止在线操作。')" />
      <div class="bench-heading"><h3>{{ t('诊断目标') }}</h3><a-tag>{{ t('临时连接') }}</a-tag></div>
      <a-alert v-if="catalogError" type="error" show-icon :message="t(catalogError)">
        <template #action><a-button size="small" @click="loadCatalog">{{ tx('重试') }}</a-button></template>
      </a-alert>
      <a-form layout="vertical" :disabled="busy">
        <div class="bench-target">
          <a-form-item :label="t('参数来源')">
            <a-select v-model:value="source" :options="sourceOptions" @change="resetSource" />
          </a-form-item>
          <a-form-item v-if="source === 'existing'" :label="tx('设备')">
            <a-select show-search option-filter-prop="label" :value="sourceDeviceId || undefined"
              :placeholder="tx('选择设备')" :options="deviceOptions" @change="copyDevice" />
          </a-form-item>
          <a-form-item :label="tx('协议驱动')">
            <a-select :value="selectedKey || undefined" :options="driverOptions" :loading="catalogLoading"
              :disabled="busy || source === 'existing'" show-search option-filter-prop="label" @change="changeDriver" />
          </a-form-item>
        </div>
        <details v-if="driver && readyTarget" class="connection-details" open>
          <summary>{{ tx('连接参数') }}</summary>
          <DeviceConnectionFields :key="editorVersion" :device="device" :protocol="device.protocol" :parameters="connectionFields" />
          <small v-if="source === 'existing'">{{ t('如需凭据，请为本次连接重新填写。') }}</small>
        </details>
      </a-form>
    </section>
    <section class="bench-panel">
      <div class="bench-heading"><h3>{{ t('诊断操作') }}</h3></div>
      <div class="bench-tabs" role="group" :aria-label="t('诊断操作')">
        <a-button v-for="op in operations" :key="op" :type="operation === op ? 'primary' : 'default'"
          :aria-pressed="operation === op" :disabled="busy || (op === 'write' && !canWrite)" @click="operation = op">
          <template #icon><component :is="icons[op]" /></template>{{ t(operationText[op]) }}
        </a-button>
      </div>
      <a-form v-if="showPoints" class="bench-points" layout="vertical" :disabled="busy">
        <a-alert v-if="needsProbe" show-icon type="info" :message="t('UDP 连接测试通过一次读取验证通信，请填写可读点位。')" />
        <div v-if="source === 'existing' && readyTarget" class="tag-chooser">
          <label>{{ t('从设备标签带入') }}</label>
          <a-select :value="undefined" :options="tagOptions" show-search option-filter-prop="label"
            :placeholder="t('输入标签名或地址筛选')" @change="copyTag" />
        </div>
        <div v-if="operation === 'batch'" class="batch-editor">
          <div class="batch-actions"><span>{{ t('批读点位') }} · {{ batch.length }} / 32</span>
            <a-button size="small" :disabled="busy || batch.length >= 32" @click="addPoint"><template #icon><PlusOutlined /></template>{{ t('添加点位') }}</a-button>
            <a-button size="small" :disabled="busy" @click="pasteOpen = true">{{ t('批量粘贴') }}</a-button>
          </div>
        </div>
        <div v-for="(row, i) in visibleRows" :key="i" class="bench-point">
          <a-form-item :label="operation === 'batch' ? (i + 1) + ' · ' + tx('地址') : tx('地址')">
            <a-input v-model:value="row.address" :maxlength="256" :placeholder="addressHint" />
          </a-form-item>
          <a-form-item :label="tx('数据类型')">
            <a-select v-model:value="row.dataType" :options="typeOptions" show-search option-filter-prop="label" />
          </a-form-item>
          <a-form-item :label="t('元素个数 / 长度')"><a-input-number v-model:value="row.elementCount" :min="1" :max="4096" :precision="0" /></a-form-item>
          <a-form-item :label="t('元素偏移')"><a-input-number v-model:value="row.elementOffset" :min="0" :max="65535" :precision="0" /></a-form-item>
          <a-button v-if="operation === 'batch'" type="text" danger :disabled="busy || batch.length < 2"
            :title="tx('删除')" :aria-label="tx('删除') + ' ' + (i + 1)" @click="batch.splice(i, 1)"><template #icon><DeleteOutlined /></template></a-button>
        </div>
        <div v-if="operation === 'write'" class="write-options">
          <a-form-item :label="t('写入值')"><a-input v-model:value="single.valueText" :maxlength="32768" :placeholder="t('按所选类型填写值')" /></a-form-item>
          <a-checkbox v-model:checked="readBack" :disabled="busy || !driver?.capabilities.supportsRead">{{ t('写后回读') }}</a-checkbox>
        </div>
      </a-form>
      <div class="bench-run"><a-button type="primary" :loading="busy" :disabled="!readyTarget || !canRun || readUnsupported || (operation === 'write' && !canWrite)"
        @click="prepare"><template #icon><PlayCircleOutlined /></template>{{ t(runText[operation]) }}</a-button>
        <span v-if="readyTarget && readUnsupported" class="muted">{{ t('此驱动不支持读取。') }}</span>
        <span v-else-if="readyTarget && !canWrite && operation !== 'write'" class="muted">{{ t('写入需要可写驱动及诊断写入权限。') }}</span>
      </div>
      <a-alert v-if="runError" show-icon type="error" :message="t(runError)" />
      <a-alert v-if="result?.writeExecution" show-icon :type="result.writeExecution.outcome==='Confirmed'?'success':'warning'" :message="executionSummary(result.writeExecution)" />
    </section>
    <div class="bench-results" aria-live="polite">
      <section class="bench-panel">
        <div class="bench-heading"><h3 id="protocol-result-title">{{ t('本次结果') }}</h3><a-tag v-if="result" :color="outcomeColor(result.outcome)">{{ label(result.outcome) }}</a-tag></div>
        <div class="bench-result-body" role="region" aria-labelledby="protocol-result-title" :tabindex="result ? 0 : undefined">
          <template v-if="result">
            <div class="bench-meta"><strong>{{ targetSummary }}</strong><span>{{ t(operationText[result.operation]) }} · {{ result.elapsedMs }} ms · {{ label(result.mode) }}</span></div>
            <a-alert v-if="result.error" show-icon type="error" :message="t(result.error)" />
            <div v-if="result.items.length" class="result-list">
              <button v-for="(item, i) in result.items" :key="i" type="button" class="result-row" :aria-pressed="selectedIndex === i"
                @click="selectedIndex = i"><strong>{{ item.address }}</strong><span>{{ item.dataType }}</span>
                <a-tag :color="outcomeColor(item.status)">{{ label(item.status) }}</a-tag><span class="row-value">{{ item.value }}</span>
              </button>
            </div>
          </template>
          <a-empty v-else :description="t('执行诊断后查看结果')" />
        </div>
      </section>
      <section class="bench-panel">
        <div class="bench-heading"><h3 id="protocol-frames-title">{{ t('通信明细') }}</h3><a-tag v-if="result">{{ label(result.stage) }}</a-tag></div>
        <div class="bench-result-body" role="region" aria-labelledby="protocol-frames-title" :tabindex="result ? 0 : undefined">
          <template v-if="result">
            <template v-if="selected">
              <div class="bench-meta"><strong>{{ selected.address }}</strong><span>{{ selected.dataType }} · {{ t('请求组') }} {{ selected.batch }}</span></div>
              <p v-if="selected.error" class="error-text">{{ t(selected.error) }}</p>
              <p v-if="selected.status === 'Success' && result.operation !== 'write'" class="value-detail">{{ t('值') }} · {{ selected.value }}</p>
              <p v-if="selected.scope && selected.scope !== 'None'">{{ t('错误范围') }} · {{ label(selected.scope) }}</p>
              <p v-if="selected.readBackStatus && selected.readBackStatus !== 'NotRequested'">
                {{ t('写后回读') }} · {{ label(selected.readBackStatus) }} <span class="value-detail">{{ selected.readBackValue }}</span>
              </p>
            </template>
            <div v-if="result.frames.length" class="frames"><div v-for="(frame, i) in visibleFrames" :key="i">
              <small>{{ frame.direction }} · {{ label(frame.stage) }} · {{ frame.byteCount }} B <span v-if="frame.truncated"> · {{ t('已截断') }}</span></small>
              <pre>{{ frame.hex }}</pre>
            </div></div>
            <small v-if="!visibleFrames.length" class="muted">{{ t('此步骤没有可展示的原始报文。') }}</small>
            <p v-if="result.framesTruncated" class="muted">{{ t('报文记录已达到本次诊断上限。') }}</p>
            <a-checkbox v-if="result.frames.length && selected" v-model:checked="showAllFrames">{{ t('展示本次全部报文') }}</a-checkbox>
          </template>
          <a-empty v-else :description="t('等待诊断结果')" />
        </div>
      </section>
    </div>
    <a-modal v-model:open="pasteOpen" centered :title="t('批量粘贴')" :ok-text="t('添加到批读列表')" :cancel-text="tx('取消')" @ok="applyPaste">
      <p>{{ t('每行一个地址。可从表格粘贴四列：地址、数据类型、元素个数、元素偏移，以制表符分隔。') }}</p>
      <a-textarea v-model:value="pasteText" :rows="6" :maxlength="32768" />
      <a-alert v-if="pasteError" type="error" :message="t(pasteError)" />
    </a-modal>
    <a-modal v-model:open="confirmOpen" centered destroy-on-close :mask-closable="false" :title="t('确认诊断写入')"
      :ok-text="t('确认写入')" :cancel-text="tx('取消')" :ok-button-props="{ danger: true }" @ok="confirmWrite" @cancel="pendingWrite = null">
      <p>{{ t('确认后将按以下参数直接写入目标设备。诊断写入独立于项目标签配置。') }}</p>
      <dl v-if="pendingWrite" class="write-review">
        <dt>{{ t('目标设备') }}</dt><dd>{{ describeTarget(pendingWrite.connection) }}</dd>
        <dt>{{ tx('地址') }}</dt><dd>{{ pendingWrite.items[0].address }}</dd>
        <dt>{{ tx('数据类型') }}</dt><dd>{{ pendingWrite.items[0].dataType }} · {{ t('元素个数 / 长度') }} {{ pendingWrite.items[0].elementCount }} · {{ t('元素偏移') }} {{ pendingWrite.items[0].elementOffset }}</dd>
        <dt>{{ t('写入值') }}</dt><dd>{{ pendingWrite.items[0].valueText }}</dd>
        <dt>{{ t('写后回读') }}</dt><dd>{{ tx(pendingWrite.readBack ? '启用' : '停用') }}</dd>
      </dl>
    </a-modal>
  </div>
</template>

<script setup lang="ts">
import { haCanDispatch, haIdentity, haWriteHeaders, explainWriteFailure, executionSummary } from '../useHighAvailability'
import { computed, onBeforeUnmount, onDeactivated, onMounted, ref, shallowRef, watch } from 'vue'
import { ApiOutlined, DeleteOutlined, EditOutlined, PlayCircleOutlined, PlusOutlined, ReadOutlined, UnorderedListOutlined } from '@ant-design/icons-vue'
import type { DeviceConfig, GatewayProtocolCatalogItem, PlcConnection, ProjectConfig, TagConfig } from '../api'
import { tx } from '../i18n'
import { diagnosticProtocols, runDiagnostic, type DiagnosticOperation, type DiagnosticPoint, type DiagnosticRequest, type DiagnosticResult } from '../protocolDiagnosticsApi'
import { createDefaultConnection, createDeviceDraft, protocolLabel } from '../utils/deviceDefaults'
import { PERMISSIONS, usePermissions } from '../utils/permissions'
import { applyCatalogProtocol } from '../utils/protocolCatalogDefaults'
import { dataTypeOptionGroups, tagAddressPlaceholder } from '../utils/tagDefaults'
import { diagnosticText as t, diagnosticCode as label } from '../i18n/protocolDiagnostics'
import DeviceConnectionFields from './DeviceConnectionFields.vue'

const props = defineProps<{ project: ProjectConfig | null }>()
const { hasPermission } = usePermissions()
const canRun = computed(() => hasPermission(PERMISSIONS.protocolDiagnosticsRun) && haCanDispatch.value)
let preparedWriteIdentity=''
const canCopyDevice = computed(() => hasPermission(PERMISSIONS.devicesView))
const canWrite = computed(() => hasPermission(PERMISSIONS.protocolDiagnosticsWrite) && !!driver.value?.capabilities.supportsWrite)
const operations: DiagnosticOperation[] = ['connect', 'read', 'batch', 'write']
const icons = { connect: ApiOutlined, read: ReadOutlined, batch: UnorderedListOutlined, write: EditOutlined }
const operationText = { connect: '连接测试', read: '单点读', batch: '批量读', write: '单点写' }
const runText = { connect: '测试连接', read: '读取点位', batch: '执行批读', write: '写入点位' }
const operation = ref<DiagnosticOperation>('connect')
const catalog = shallowRef<GatewayProtocolCatalogItem[]>([])
const catalogLoading = ref(false)
const catalogError = ref('')
const source = ref<'manual' | 'existing'>('manual')
const sourceOptions = computed(() => [
  { label: t('手动填写'), value: 'manual' },
  ...(canCopyDevice.value ? [{ label: t('从已有设备带入'), value: 'existing' }] : [])
])
const selectedKey = ref('')
const sourceDeviceId = ref('')
const device = ref(createDeviceDraft())
const editorVersion = ref(0)
function driverKey(item: GatewayProtocolCatalogItem) {
  return item.builtIn ? item.protocol : 'driver:' + item.driverId
}
const driver = computed(() => catalog.value.find(item => driverKey(item) === selectedKey.value))
const connectionFields = computed(() => driver.value?.parameters.map(p => p.key === 'timeoutMilliseconds'
  ? { ...p, min: Math.max(100, p.min ?? 100), max: Math.min(10000, p.max ?? 10000) } : p))
const driverOptions = computed(() => catalog.value.map(item => ({ label: tx(item.displayName || protocolLabel(item.protocol)), value: driverKey(item) })))
const channelById = computed(() => new Map((props.project?.channels ?? []).map(channel => [channel.id, channel])))
const deviceOptions = computed(() => canCopyDevice.value ? (props.project?.devices ?? []).map(d => ({
  label: [channelById.value.get(d.channelId)?.name, d.name].filter(Boolean).join(' / '),
  value: JSON.stringify([d.channelId, d.id]),
  device: d
})) : [])
const original = computed(() => source.value === 'existing' ? deviceOptions.value.find(o => o.value === sourceDeviceId.value)?.device : undefined)
const readyTarget = computed(() => !!driver.value && (source.value === 'manual' || !!original.value))
const typeOptions = computed(() => dataTypeOptionGroups(device.value.protocol, device.value.connection.driverOptionsJson)
  .flatMap(group => group.options.map(value => ({ value, label: value }))))
const addressHint = computed(() => {
  if (device.value.protocol === 'Dlt6452007') return 'DLT645:000000000001:00010000'
  if (device.value.protocol.startsWith('Cjt188')) return 'CJ188:12345678901234:901F'
  return tx(tagAddressPlaceholder(device.value.protocol))
})
function newPoint(): DiagnosticPoint {
  const types = typeOptions.value
  return { address: '', dataType: types.find(o => o.value === 'UInt16')?.value ?? types[0]?.value ?? 'Int16', elementCount: 1, elementOffset: 0, valueText: '' }
}
const single = ref<DiagnosticPoint>(newPoint())
const batch = ref<DiagnosticPoint[]>([newPoint()])
const readBack = ref(false)
const visibleRows = computed(() => operation.value === 'batch' ? batch.value : [single.value])
const needsProbe = computed(() => operation.value === 'connect' && (
  ['ModbusUdp', 'BacnetIp', 'Snmp'].includes(device.value.protocol) || device.value.connection.transport?.toLowerCase() === 'udp'))
const showPoints = computed(() => operation.value !== 'connect' || needsProbe.value)
const readUnsupported = computed(() => showPoints.value && operation.value !== 'write' && !driver.value?.capabilities.supportsRead)
const busy = ref(false)
const runError = ref('')
const result = shallowRef<DiagnosticResult | null>(null)
const selectedIndex = ref(0)
const selected = computed(() => result.value?.items[selectedIndex.value])
const showAllFrames = ref(false)
const visibleFrames = computed(() => {
  const frames = result.value?.frames ?? []
  return !selected.value || showAllFrames.value ? frames : frames.filter(frame => frame.batch === 0 || frame.batch === selected.value!.batch)
})
const targetSummary = ref('')
const confirmOpen = ref(false)
const pendingWrite = shallowRef<DiagnosticRequest | null>(null)
const pasteOpen = ref(false)
const pasteText = ref('')
const pasteError = ref('')
let catalogRequest: AbortController | undefined
let activeRequest: AbortController | undefined

function resetPoints() {
  single.value = newPoint()
  batch.value = [newPoint()]
  runError.value = ''
  confirmOpen.value = false
  pendingWrite.value = null
  readBack.value = false
  pasteText.value = ''
  pasteError.value = ''
  result.value = null
  if (operation.value === 'write' && !canWrite.value) operation.value = 'read'
}
function changeDriver(key: string) {
  const item = catalog.value.find(i => driverKey(i) === key)
  if (!item || busy.value) return
  const draft = createDeviceDraft()
  applyCatalogProtocol(draft, item)
  device.value = draft
  selectedKey.value = key
  editorVersion.value++
  resetPoints()
}
function resetSource() {
  sourceDeviceId.value = ''
  selectedKey.value = ''
  device.value = createDeviceDraft()
  resetPoints()
  const fallback = catalog.value.find(item => item.protocol === 'ModbusTcp') ?? catalog.value[0]
  if (source.value === 'manual' && fallback) changeDriver(driverKey(fallback))
}
async function loadCatalog() {
  catalogRequest?.abort()
  const request = new AbortController()
  catalogRequest = request
  catalogLoading.value = true
  catalogError.value = ''
  try {
    const items = await diagnosticProtocols(request.signal)
    if (request.signal.aborted) return
    catalog.value = items
    resetSource()
    if (!items.length) catalogError.value = '当前没有可用的诊断驱动。'
  } catch (err) {
    if (!request.signal.aborted) catalogError.value = err instanceof Error ? err.message : String(err)
  } finally {
    if (catalogRequest === request) catalogLoading.value = false
  }
}
function copyDevice(key: string) {
  if (!canCopyDevice.value || busy.value) return
  sourceDeviceId.value = key
  const d = original.value
  const driverId = d?.connection?.driverId || (d && channelById.value.get(d.channelId)?.driverId)
  const drivers = catalog.value.filter(entry => entry.protocol === d?.protocol)
  const item = driverId
    ? drivers.find(entry => entry.driverId.toLowerCase() === driverId.toLowerCase())
    : drivers.find(entry => entry.builtIn) ?? (drivers.length === 1 ? drivers[0] : undefined)
  selectedKey.value = item ? driverKey(item) : ''
  device.value = createDeviceDraft()
  if (d && item) {
    const connection = { ...createDefaultConnection(d.protocol), ...d.connection, protocol: item.protocol, driverId: item.builtIn ? '' : item.driverId, password: '' }
    if (connection.driverOptionsJson) {
      try {
        const options: Record<string, unknown> = JSON.parse(connection.driverOptionsJson)
        for (const parameter of item.parameters) {
          if ((parameter.secret || parameter.parameterType.toLowerCase() === 'password') && parameter.key.startsWith('driverOptions.'))
            delete options[parameter.key.slice('driverOptions.'.length)]
        }
        connection.driverOptionsJson = JSON.stringify(options)
      } catch { connection.driverOptionsJson = '' }
    }
    device.value.protocol = item.protocol
    device.value.connection = connection
  }
  editorVersion.value++
  resetPoints()
  if (!item) runError.value = '此设备的驱动当前不可用于诊断。'
}
function diagnosticAddress(tag: TagConfig) {
  if (tag.address?.trim()) return tag.address.trim()
  const protocol = device.value.protocol
  if (tag.meterAddress && tag.meterDataIdentifier) {
    if (protocol === 'Dlt6452007') return ['DLT645', tag.meterAddress, tag.meterDataIdentifier].join(':')
    if (protocol === 'Cjt1882004' || protocol === 'Cjt1882018')
      return [protocol === 'Cjt1882018' ? 'CJ188-2018' : 'CJ188', tag.meterType, tag.meterAddress, tag.meterDataIdentifier].filter(Boolean).join(':')
  }
  return ''
}
const tagOptions = computed(() => {
  const d = original.value
  if (!d) return []
  const groups = [{ id: '', name: '', tags: d.tags }, ...d.groups]
  return groups.flatMap(group => group.tags.filter(tag => !tag.isVirtual).map(tag => ({
    tag, value: JSON.stringify([group.id, tag.id]), label: [group.name, tag.name, diagnosticAddress(tag)].filter(Boolean).join(' / ')
  })))
})
function copyTag(key: string) {
  const tag = tagOptions.value.find(o => o.value === key)?.tag
  if (!tag || busy.value) return
  const point = { address: diagnosticAddress(tag), dataType: tag.dataType, elementCount: tag.elementCount || 1, elementOffset: tag.elementOffset || 0, valueText: '' }
  if (operation.value !== 'batch') single.value = point
  else {
    const blank = batch.value.findIndex(row => !row.address.trim())
    if (blank >= 0) batch.value[blank] = point
    else if (batch.value.length < 32) batch.value.push(point)
    else { runError.value = '每次最多批读 32 个点位。'; return }
  }
  runError.value = ''
}
function addPoint() {
  if (!busy.value && batch.value.length < 32) batch.value.push(newPoint())
}
function pointError(row: DiagnosticPoint) {
  if (!row.address.trim() || row.address.trim().length > 256) return '请填写有效地址，最多 256 个字符。'
  if (!typeOptions.value.some(o => o.value === row.dataType)) return '请选择当前驱动支持的数据类型。'
  if (!Number.isInteger(row.elementCount) || row.elementCount < 1 || row.elementCount > 4096) return '元素个数必须为 1 至 4096 的整数。'
  if (!Number.isInteger(row.elementOffset) || row.elementOffset < 0 || row.elementOffset > 65535) return '元素偏移必须为 0 至 65535 的整数。'
  return ''
}
function applyPaste() {
  const rows = pasteText.value.split(/\r?\n/).filter(line => line.trim())
  const next = batch.value.filter(row => row.address.trim())
  if (!rows.length) { pasteError.value = '请先粘贴点位地址。'; return }
  if (rows.length + next.length > 32) { pasteError.value = '每次最多批读 32 个点位。'; return }
  for (const [i, line] of rows.entries()) {
    const [address, type, count, offset, ...extra] = line.split('\t').map(cell => cell.trim())
    const row = newPoint()
    row.address = address
    if (type) row.dataType = typeOptions.value.find(o => o.value.toLowerCase() === type.toLowerCase())?.value ?? type
    if (count) row.elementCount = Number(count)
    if (offset) row.elementOffset = Number(offset)
    const error = extra.length ? '每行最多四列。' : pointError(row)
    if (error) { pasteError.value = (i + 1) + ' · ' + error; return }
    next.push(row)
  }
  batch.value = next
  pasteError.value = ''
  pasteText.value = ''
  pasteOpen.value = false
}
function describeTarget(connection: PlcConnection) {
  let endpoint = connection.host
  if (endpoint?.includes('://')) {
    try {
      const url = new URL(endpoint)
      endpoint = url.protocol + '//' + url.host + url.pathname
    } catch { endpoint = connection.protocol }
  } else if (endpoint && connection.port) endpoint += ':' + connection.port
  return [connection.driverId || protocolLabel(connection.protocol), endpoint].filter(Boolean).join(' · ')
}
function prepare() {
  if (busy.value || !readyTarget.value || !canRun.value || readUnsupported.value || (operation.value === 'write' && !canWrite.value)) return
  runError.value = ''
  const connection = { ...device.value.connection }
  if (!Number.isInteger(connection.timeoutMilliseconds) || connection.timeoutMilliseconds < 100 || connection.timeoutMilliseconds > 10000) {
    runError.value = '连接超时必须在 100 至 10000 毫秒之间。'
    return
  }
  const items = (showPoints.value ? visibleRows.value : []).map(row => ({ ...row, address: row.address.trim() }))
  for (const [i, row] of items.entries()) {
    const error = pointError(row)
    if (error) { runError.value = operation.value === 'batch' ? (i + 1) + ' · ' + error : error; return }
  }
  if (operation.value === 'write' && single.value.dataType !== 'String' && !single.value.valueText.trim()) {
    runError.value = '请填写写入值。'
    return
  }
  const input: DiagnosticRequest = { operation: operation.value, connection, items, readBack: operation.value === 'write' && readBack.value, confirmWrite: false }
  if (input.operation === 'write') {
    preparedWriteIdentity=haIdentity.value
    pendingWrite.value = input
    confirmOpen.value = true
  } else void execute(input)
}
async function execute(input: DiagnosticRequest) {
  if (busy.value || !canRun.value) return
  busy.value = true
  const request = new AbortController()
  activeRequest = request
  const headers=input.operation==='write'?haWriteHeaders():{}
  result.value = null
  targetSummary.value = describeTarget(input.connection)
  try {
    const response = await runDiagnostic(input, request.signal, headers)
    if (request.signal.aborted) return
    result.value = response
    const firstError = response.items.findIndex(row => row.status === 'Failed')
    selectedIndex.value = Math.max(0, firstError)
    showAllFrames.value = false
  } catch (err) {
    if (!request.signal.aborted) {
      const failure=await explainWriteFailure(err,headers,hasPermission(PERMISSIONS.haWritesView))
      if (!request.signal.aborted) runError.value=failure.message
    }
  } finally {
    if (activeRequest === request) busy.value = false
  }
}
function confirmWrite() {
  const input = pendingWrite.value
  pendingWrite.value = null
  confirmOpen.value = false
  if (input && preparedWriteIdentity!==haIdentity.value) {runError.value=tx('业务任期已变化，请重新确认写入。');return}
  if (input && canWrite.value) void execute({ ...input, confirmWrite: true })
}
function outcomeColor(status: string) {
  return ({ Success: 'green', Failed: 'red', Partial: 'orange' } as Record<string, string>)[status]
}
watch(operation, () => { runError.value = '' })
watch(canCopyDevice, allowed => {
  if (!allowed && source.value === 'existing') { source.value = 'manual'; resetSource() }
})
onMounted(loadCatalog)
onDeactivated(() => {
  confirmOpen.value = false
  pasteOpen.value = false
  pendingWrite.value = null
})
onBeforeUnmount(() => { catalogRequest?.abort(); activeRequest?.abort() })
</script>

<style scoped>
.protocol-bench {
  container-type: inline-size;
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: 100%;
  height: 100%;
  min-width: 0;
  min-height: 0;
  overflow: auto;
}
.bench-panel {
  background: #fff;
  border: 1px solid #d9e3ed;
  border-radius: 7px;
  padding: 14px 16px;
  min-width: 0;
}
.protocol-bench > .bench-panel { flex: none; }
.bench-heading, .bench-run, .bench-tabs, .batch-actions, .bench-meta, .write-options {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px 12px;
}
.bench-heading { justify-content: space-between; margin-bottom: 12px; }
.bench-heading h3 { margin: 0; color: #171b22; font-size: 15px; font-weight: 650; }
.bench-panel :deep(.ant-tag) { margin-inline-end: 0; }
.bench-panel :deep(.ant-form-item) { margin-bottom: 0; min-width: 0; }
.bench-panel :deep(.ant-form-item-label) { padding-bottom: 3px; }
.bench-panel :deep(.ant-input-number), .bench-panel :deep(.ant-select) { width: 100%; min-width: 0; }
.bench-target, .connection-details :deep(.form-grid) { display: grid; grid-template-columns: repeat(auto-fit,minmax(min(195px,100%),1fr)); gap: 8px 14px; }
.connection-details { border-top: 1px solid #e5ebf1; margin-top: 12px; padding-top: 8px; }
.connection-details summary { cursor: pointer; font-weight: 600; }
.connection-details :deep(.connection-fields) { margin-top: 10px; }
.connection-details :deep(.connection-parameter-section) { min-width: 0; }
.connection-details :deep(.connection-parameter__help), .bench-panel small, .muted { color: #536778; overflow-wrap: anywhere; }
.bench-panel :deep(.ant-alert) { margin: 8px 0; overflow-wrap: anywhere; }
.bench-points { display: grid; gap: 10px; margin-top: 12px; }
.tag-chooser { display: grid; align-items: center; grid-template-columns: auto minmax(0,420px); gap: 6px 12px; }
.batch-actions { border-top: 1px solid #e5ebf1; padding-top: 10px; }
.bench-point { display: grid; grid-template-columns: minmax(140px,2fr) minmax(110px,1fr) repeat(2,minmax(80px,1fr)) auto; align-items: end; gap: 6px 12px; }
.bench-run { margin-top: 12px; }
.write-options { align-items: end; }
.write-options .ant-form-item { flex: 1 1 220px; max-width: 480px; }
.write-options .ant-checkbox-wrapper { padding-block: 5px; }
.bench-results {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  grid-template-rows: minmax(0, 1fr);
  flex: 1 0 0;
  gap: 12px;
  min-width: 0;
  min-height: 180px;
}
.bench-results > .bench-panel { display: flex; flex-direction: column; min-height: 0; }
.bench-results .bench-heading { flex: none; }
.bench-result-body { flex: 1; min-height: 0; overflow: auto; overflow-wrap: anywhere; }
.bench-result-body:has(> .ant-empty) { display: grid; align-content: safe center; }
.bench-result-body :deep(.ant-empty-image) { height: 48px; }
.bench-meta { gap: 4px 14px; overflow-wrap: anywhere; }
.bench-meta span { color: #536778; }
.result-list, .frames { margin-top: 10px; }
.result-row { display: grid; grid-template-columns: minmax(64px,1.3fr) minmax(48px,.8fr) auto minmax(48px,1fr); align-items: center; gap: 8px; padding: 8px; background: white; color: inherit; font: inherit; text-align: start; width: 100%; border: 1px solid transparent; border-bottom-color: #edf0f4; cursor: pointer; overflow-wrap: anywhere; }
.result-row[aria-pressed="true"] { border-color: #86aacd; background: #eff6fc; }
.result-row:hover { background: #f1f6fa; }
.row-value { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.value-detail, .error-text, .write-review dd { white-space: pre-wrap; overflow-wrap: anywhere; }
.error-text { color: #ab2732; }
.frames pre { margin: 3px 0 8px; padding: 6px 8px; background: #f4f7fa; color: #162f46; white-space: pre-wrap; overflow-wrap: anywhere; }
.write-review { display: grid; grid-template-columns: auto minmax(0,1fr); gap: 8px 14px; }
.write-review dt { color: #536778; }
.write-review dd { margin: 0; }
@container (max-width: 820px) {
  .bench-results {
    grid-template-columns: minmax(0, 1fr);
    grid-template-rows: repeat(2, minmax(0, 1fr));
    min-height: 372px;
  }
  .bench-point { grid-template-columns: minmax(0,1fr) minmax(0,1fr); padding-top: 8px; border-top: 1px solid #e5ebf1; }
  .bench-point > button { width: fit-content; }
}
@container (max-width: 450px) {
  .bench-panel { padding: 12px; }
  .bench-target, .tag-chooser { grid-template-columns: minmax(0,1fr); }
  .result-row { grid-template-columns: minmax(0,1fr) auto; }
  .bench-tabs { gap: 6px; }
  .bench-tabs :deep(.ant-btn) { flex: 1 1 40%; }
}
</style>
