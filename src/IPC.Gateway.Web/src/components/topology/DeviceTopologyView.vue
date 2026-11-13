<template>
  <section class="channel-topology">
    <header class="topology-tools">
      <a-input v-model:value="search" allow-clear :aria-label="tx('搜索设备、协议、分组、标签')" :placeholder="tx('搜索通道、设备、协议、分组或标签')">
        <template #prefix><SearchOutlined /></template>
      </a-input>
      <span>{{ formatMessage('{0} 个通道 · {1} 台设备 · {2} 个点位', [model.channels.length, model.deviceCount, model.tagCount]) }}</span>
    </header>

    <TopologyViewport>
      <div class="network-stage">
        <section class="network-side south">
          <h3>{{ tx('南向通道') }} <small>{{ channels.length }}</small></h3>
          <div v-if="channels.length" ref="channelList" class="channel-scroll">
            <div class="south-stack">
              <div v-for="column in channelColumns" :key="column[0].id" class="channel-column">
                <a-button v-for="item in column" :key="item.id" class="channel-node" :data-channel="item.id"
                  :aria-pressed="channel?.id === item.id" @click="selectChannel(item)">
                  <ApartmentOutlined class="node-icon" />
                  <span class="node-copy">
                    <strong :title="item.name">{{ item.name }}</strong>
                    <small :title="channelHealth(item)"><i :class="['light', channelTone(item)]" />{{ channelHealth(item) }}</small>
                  </span>
                  <span class="node-count">{{ formatMessage('{0} 台', [item.devices.length]) }} <RightOutlined /></span>
                </a-button>
              </div>
            </div>
          </div>
          <div v-else class="empty-node">{{ tx(query ? '无匹配项' : '暂无通道') }}</div>
        </section>

        <button type="button" class="gateway-node" data-gateway :aria-pressed="focus === 'gateway'" @click="focus = 'gateway'">
          <img :src="gatewayImage" alt="" />
          <strong :title="gatewayName">{{ gatewayName }}</strong>
          <small><i :class="['light', !status ? 'unknown' : status.isRunning ? 'online' : 'disabled']" />
            {{ tx(!status ? '未知' : status.isRunning ? '运行中' : '未运行') }}</small>
        </button>

        <section class="network-side north">
          <h3>{{ tx('北向服务') }} <small>{{ services.length }}</small></h3>
          <div class="node-stack north-stack">
            <TopologyNorthbound :services="services" :selected-id="focus === 'service' ? serviceId : null" @select="selectService" />
          </div>
        </section>
      </div>
    </TopologyViewport>

    <section v-if="channel" class="channel-devices">
      <header>
        <h3>{{ channel.name }} <small>{{ formatMessage('{0} 台设备', [devices.length]) }}</small></h3>
        <a-pagination v-if="devices.length > devicesPerPage" size="small" simple :aria-label="tx('设备分页')"
          :current="devicePage" :page-size="devicesPerPage" :total="devices.length" @change="changeDevicePage" />
      </header>
      <div v-if="deviceSlice.length" ref="deviceList" class="device-chips"
        :style="{ '--device-rows': Math.min(itemsPerColumn, deviceSlice.length) }">
        <a-button v-for="item in deviceSlice" :key="item.id" :data-device="item.id"
          :aria-pressed="focus === 'device' && device?.id === item.id" @click="selectDevice(item)">
          <i :class="['light', item.state]" />
          <span class="device-name" :title="item.name">{{ item.name }}</span>
          <small>{{ deviceHealth(item) }}</small>
        </a-button>
      </div>
      <span v-else class="muted">{{ tx('此通道下没有设备') }}</span>
    </section>

    <section class="topology-inspector" data-detail :aria-label="tx('节点详情')">
      <header><h3>{{ detailTitle }}</h3>
        <a-button v-if="focus === 'device' && device && device.tagCount > 0" size="small" @click="showTags = true">
          <template #icon><UnorderedListOutlined /></template>{{ tx('查看标签') }}
        </a-button>
      </header>
      <dl><div v-for="row in detailRows" :key="row.label"><dt>{{ row.label }}</dt><dd>{{ row.value }}</dd></div></dl>
      <div v-if="errors.length" class="topology-errors">
        <strong>{{ tx('最近错误') }}</strong>
        <a-button v-for="(err, i) in errors" :key="i" type="link" danger @click="emit('select-error', err)">
          {{ formatDateTime(err.timestamp) }}　{{ tx(err.message || '未知错误') }}
        </a-button>
      </div>
    </section>
    <TopologyDeviceTags v-if="showTags && device" :device="device.config" :runtime-tags="runtimeTags"
      :channel-enabled="channel?.enabled !== false"
      :initial-search="tagSearch" @close="showTags = false" />
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ApartmentOutlined, RightOutlined, SearchOutlined, UnorderedListOutlined } from '@ant-design/icons-vue'
import type { GatewayStatus, ProjectConfig, RuntimeErrorDetail, TagValueSnapshot } from '../../api'
import { formatMessage, tx } from '../../i18n'
import { formatDateTime } from '../../utils/format'
import gatewayImage from '../../assets/topology-gateway.svg'
import { buildChannelTopology, type TopologyChannel, type TopologyDevice } from './channelTopologyModel'
import { buildNorthboundServices, type NorthboundService } from './northboundTopology'
import { normalizeKey } from './topologyStatus'
import TopologyNorthbound from './TopologyNorthbound.vue'
import TopologyViewport from './TopologyViewport.vue'
import TopologyDeviceTags from './TopologyDeviceTags.vue'

const props = defineProps<{
  project: ProjectConfig | null
  status: GatewayStatus | null
  failedTagsByDevice?: ReadonlyMap<string, number>
  runtimeTags?: ReadonlyMap<string, TagValueSnapshot>
}>()
const emit = defineEmits<{ 'select-error': [error: RuntimeErrorDetail] }>()
const model = computed(() => buildChannelTopology(props.project, props.status, props.failedTagsByDevice))
const services = computed(() => buildNorthboundServices(props.status))
const gatewayName = computed(() => props.project?.name || props.status?.projectName || 'IPC Gateway')
const focus = ref<'channel' | 'device' | 'service' | 'gateway'>('channel')
const serviceId = ref<NorthboundService['id']>('mqtt')
const search = ref('')
const query = computed(() => normalizeKey(search.value))
const itemsPerColumn = 4
const devicesPerPage = itemsPerColumn * 3
const channelList = ref<HTMLDivElement>()
const deviceList = ref<HTMLDivElement>()
const devicePage = ref(1)
const channelId = ref<string>(), deviceId = ref<string>()
const showTags = ref(false)

// Only configuration edits and search input invalidate these matches. Live value updates do not re-scan tag names.
const matches = computed(() => {
  if (!query.value) return null
  const includes = (s?: string) => normalizeKey(s).includes(query.value)
  const whole = new Set((props.project?.channels ?? []).filter(c => [c.name,c.id,c.protocol].some(includes)).map(c => normalizeKey(c.id)))
  const byChannel = new Map<string, Set<string>>()
  for (const d of props.project?.devices ?? []) {
    const key = normalizeKey(d.channelId)
    if (whole.has(key)) continue
    if (![d.name,d.id,d.protocol].some(includes) && !d.tags?.some(t => includes(t.name) || includes(t.address)) &&
        !d.groups?.some(g => includes(g.name) || g.tags?.some(t => includes(t.name) || includes(t.address)))) continue
    const ids = byChannel.get(key) ?? new Set<string>()
    ids.add(d.id)
    byChannel.set(key, ids)
  }
  return {whole, byChannel}
})
const channels = computed(() => model.value.channels.filter(c => !matches.value ||
  matches.value.whole.has(normalizeKey(c.id)) || matches.value.byChannel.has(normalizeKey(c.id))))
const channelColumns = computed(() => {
  const columns: TopologyChannel[][] = []
  for (let i = 0; i < channels.value.length; i += itemsPerColumn) {
    columns.push(channels.value.slice(i, i + itemsPerColumn))
  }
  return columns
})
const channel = computed(() => channels.value.find(c => c.id === channelId.value) ?? channels.value[0])
const devices = computed(() => {
  if (!channel.value) return []
  const ids = matches.value?.byChannel.get(normalizeKey(channel.value.id))
  return ids ? channel.value.devices.filter(d => ids.has(d.id)) : channel.value.devices
})
const deviceSlice = computed(() => devices.value.slice((devicePage.value - 1) * devicesPerPage, devicePage.value * devicesPerPage))
const device = computed(() => deviceSlice.value.find(item => item.id === deviceId.value))
const service = computed(() => services.value.find(s => s.id === serviceId.value))
const tagSearch = computed(() => device.value && [channel.value?.name, channel.value?.protocol, device.value.id, device.value.name, device.value.protocol]
  .some(s => normalizeKey(s).includes(query.value)) ? '' : search.value)

function clearDevice() { devicePage.value = 1; deviceId.value = undefined; showTags.value = false }
function selectChannel(item: TopologyChannel) { channelId.value = item.id; clearDevice(); focus.value = 'channel' }
function changeDevicePage(next: number) { devicePage.value = next; deviceId.value = undefined; focus.value = 'channel' }
function selectDevice(item: TopologyDevice) { deviceId.value = item.id; focus.value = 'device' }
function selectService(id: NorthboundService['id']) { serviceId.value = id; focus.value = 'service' }
watch(query, () => {
  channelId.value = undefined
  clearDevice()
  focus.value = 'channel'
  if (channelList.value) channelList.value.scrollLeft = 0
})
watch(() => channel.value?.id, clearDevice)
watch([() => channel.value?.id, query, devicePage], () => {
  if (deviceList.value) deviceList.value.scrollLeft = 0
})
watch(() => devices.value.length, n => { devicePage.value = Math.min(devicePage.value, Math.max(1, Math.ceil(n / devicesPerPage))) })
watch(() => device.value?.id, id => { if (!id && focus.value === 'device') { focus.value = 'channel'; showTags.value = false } })

function channelTone(c: TopologyChannel) {
  if (!c.enabled) return 'disabled'
  if (c.counts.offline) return 'offline'
  if (c.counts.warning) return 'warning'
  if (c.counts.unknown || !c.devices.length) return 'unknown'
  return c.counts.online ? 'online' : 'disabled'
}
function deviceHealth(d: TopologyDevice) {
  if (d.state === 'warning') return tx(d.badTagCount ? '点位异常' : '通信降级')
  return tx(({online:'在线',offline:'离线',disabled:'停用',unknown:'未知'})[d.state])
}
function channelHealth(c: TopologyChannel) {
  if (!c.enabled) return tx('已停用')
  if (!c.devices.length) return tx('暂无设备')
  const labels = []
  if (c.counts.offline) labels.push(formatMessage('{0} 台离线', [c.counts.offline]))
  if (c.pointIssueDevices) labels.push(formatMessage('{0} 台点位异常', [c.pointIssueDevices]))
  if (c.counts.warning > c.pointIssueDevices) labels.push(formatMessage('{0} 台通信降级', [c.counts.warning - c.pointIssueDevices]))
  if (c.counts.disabled) labels.push(formatMessage('{0} 台停用', [c.counts.disabled]))
  if (c.counts.unknown) labels.push(formatMessage('{0} 台状态未知', [c.counts.unknown]))
  return labels.join(' · ') || tx('全部在线')
}
const detailTitle = computed(() => {
  if (focus.value === 'service' && service.value) return service.value.name
  if (focus.value === 'device' && device.value) return device.value.name
  if (focus.value === 'channel' && channel.value) return channel.value.name
  return gatewayName.value
})
function row(label: string, value: string | number | undefined) { return {label:tx(label), value:value ?? '—'} }
function moduleState(module?: { enabled: boolean; isRunning: boolean; lastError?: string }) {
  return tx(!module ? '未知' : !module.enabled ? '停用' : module.lastError ? '异常' : module.isRunning ? '运行中' : '未运行')
}
const detailRows = computed(() => {
  if (focus.value === 'service' && service.value) return service.value.rows
  const c = channel.value, d = device.value
  if (focus.value === 'device' && d) return [row('通道',c?.name), row('协议',d.protocol), row('状态',deviceHealth(d)),
    row('分组',d.groupCount), row('直属标签',d.directTagCount),row('标签总数',d.tagCount),row('异常点位数',d.badTagCount),
    row('采集周期(ms)',d.config.defaultScanRateMs),row('成功率', d.runtime?.totalReads ? d.runtime.successRate.toFixed(1)+'%' : undefined),
    row('最近错误',d.runtime?.lastError ? tx(d.runtime.lastError) : undefined)]
  if (focus.value === 'channel' && c) return [row('协议',c.protocol), row('状态',channelHealth(c)), row('设备数',c.devices.length),
    row('标签总数',c.tagCount),row('离线设备',c.counts.offline),row('点位异常设备',c.pointIssueDevices),
    row('通信降级设备',c.counts.warning-c.pointIssueDevices)]
  return [row('状态',tx(!props.status ? '未知' : props.status.isRunning ? '运行中' : '未运行')), row('通道',model.value.channels.length),
    row('设备数',model.value.deviceCount),row('标签总数',model.value.tagCount),row('历史库',moduleState(props.status?.history)),
    row('规则引擎',moduleState(props.status?.flowRuleEngine))]
})
const errors = computed(() => {
  if (focus.value === 'gateway') return props.status?.recentErrors ?? []
  if (focus.value === 'channel') return channel.value?.errors ?? []
  if (focus.value === 'device') return device.value?.errors ?? []
  return []
})
</script>

<style scoped src="./channel-topology.css"></style>
