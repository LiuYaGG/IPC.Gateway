import type {
  ChannelConfig,
  DeviceConfig,
  DeviceRuntimeStatus,
  GatewayStatus,
  GroupConfig,
  ProjectConfig,
  RuntimeErrorDetail,
  TagConfig,
  TagValueSnapshot
} from '../../api'
import { nodeCategory, nodeSize } from './topologyLayout'
import { buildTopologyLanePlan, tagRowSpacing } from './topologyLanePlan'
import {
  filterErrors,
  mergeTone,
  normalizeKey,
  resolveDeviceTone,
  resolveTagTone,
  tagConfigKey,
  tagSnapshotKey,
  toneLabel
} from './topologyStatus'
import type { DeviceTopologyModel, TopologyLink, TopologyNode, TopologyTone } from './topologyTypes'

const tagNodeLimit = 220

export interface BuildTopologyInput {
  project: ProjectConfig | null
  status: GatewayStatus | null
  search: string
  showTagNodes: boolean
}

export function buildDeviceTopology(input: BuildTopologyInput): DeviceTopologyModel {
  const channels = input.project?.channels ?? []
  const devices = input.project?.devices ?? []
  const query = normalizeKey(input.search)
  const runtimeMap = buildDeviceRuntimeMap(input.status?.devices ?? [])
  const snapshotMap = buildTagSnapshotMap(input.status?.tags ?? [])
  const errors = input.status?.recentErrors ?? []
  const channelMap = new Map(channels.map((channel) => [normalizeKey(channel.id), channel]))
  const visibleDevices = devices.filter((device) =>
    matchesDevice(device, channelMap.get(normalizeKey(device.channelId)), query)
  )
  const visibleChannels = channels.filter((channel) => {
    if (!query) return true
    return (
      matchesChannel(channel, query) ||
      visibleDevices.some((device) => normalizeKey(device.channelId) === normalizeKey(channel.id))
    )
  })
  const channelKeys = visibleChannels.map(channelLayoutKey)
  const groupsByDevice = buildGroupRows(visibleDevices, input.showTagNodes)
  const groupRows = [...groupsByDevice.values()].flat()
  const tagCount = groupRows.reduce((sum, row) => sum + row.tags.length, 0)
  const renderedTagNodes = groupRows.reduce((sum, row) => sum + (row.expanded ? row.tags.length : 0), 0)
  const lanePlan = buildTopologyLanePlan(
    channelKeys,
    visibleDevices.map((device) => ({
      key: deviceLayoutKey(device),
      channelKey: normalizeKey(device.channelId),
      groups: groupsByDevice.get(device)!.map((row) => ({
        key: groupRowKey(row),
        visibleTagCount: row.expanded ? row.tags.length : 0
      }))
    }))
  )

  const nodes: TopologyNode[] = []
  const links: TopologyLink[] = []
  const matchedDeviceIds = new Set<string>()

  nodes.push(
    createNode({
      id: 'gateway',
      name: input.project?.name || input.status?.projectName || 'IPC Gateway',
      type: 'gateway',
      tone: input.status?.isRunning ? 'good' : 'warn',
      x: 90,
      y: lanePlan.gatewayY,
      value: visibleDevices.length,
      rows: [
        { label: '项目', value: input.project?.name || input.status?.projectName || '-' },
        { label: '设备', value: visibleDevices.length },
        { label: '标签', value: tagCount },
        { label: '状态', value: input.status?.isRunning ? '运行中' : '未运行' }
      ],
      errors
    })
  )

  addServiceNodes(nodes, links, input.status, lanePlan.gatewayY)
  addChannelNodes(nodes, links, visibleChannels, visibleDevices, runtimeMap, lanePlan, errors)

  visibleDevices.forEach((device, index) => {
    const deviceId = deviceNodeId(device, index)
    matchedDeviceIds.add(device.id || device.name)
    const channel = channelMap.get(normalizeKey(device.channelId))
    const runtime = runtimeMap.get(deviceRuntimeMapKey(device.channelId, device.id))
    const deviceTags = collectDeviceTags(device)
    const deviceErrors = filterErrors(errors, device.channelId, device.id)
    const y = lanePlan.deviceY.get(deviceLayoutKey(device))!
    const tone = resolveDeviceTone(device, runtime)

    nodes.push(
      createNode({
        id: deviceId,
        name: device.name || device.id || '未命名设备',
        type: 'device',
        tone,
        x: 520,
        y,
        value: deviceTags.length,
        rows: [
          { label: '通道', value: channel?.name || '-' },
          { label: '协议', value: device.protocol || '-' },
          { label: '状态', value: toneLabel(tone) },
          { label: '成功率', value: `${Number(runtime?.successRate || 0).toFixed(1)}%` },
          { label: '标签', value: deviceTags.length },
          { label: '最近错误', value: runtime?.lastError || '-' }
        ],
        errors: deviceErrors
      })
    )
    links.push({ source: channelNodeId(device.channelId), target: deviceId, value: channel?.name || '-' })

    addDeviceGroups({
      device,
      deviceId,
      nodes,
      links,
      groupRows: groupsByDevice.get(device)!,
      lanePlan,
      snapshotMap,
      errors,
      showTagNodes: input.showTagNodes
    })
  })

  return {
    nodes,
    links,
    matchedDeviceIds,
    tagNodeLimitReached: input.showTagNodes && tagCount > tagNodeLimit,
    summary: {
      channels: visibleChannels.length,
      devices: visibleDevices.length,
      onlineDevices: visibleDevices.filter((device) => {
        const runtime = runtimeMap.get(deviceRuntimeMapKey(device.channelId, device.id))
        return device.enabled && !!runtime?.isConnected
      }).length,
      offlineDevices: visibleDevices.filter((device) => {
        const runtime = runtimeMap.get(deviceRuntimeMapKey(device.channelId, device.id))
        return device.enabled && !runtime?.isConnected
      }).length,
      disabledDevices: visibleDevices.filter((device) => !device.enabled).length,
      groups: groupRows.filter((row) => row.group).length,
      tags: tagCount,
      visibleTags: renderedTagNodes,
      hiddenTags: tagCount - renderedTagNodes,
      recentErrors: errors.filter(
        (error) =>
          !query ||
          visibleDevices.some((device) => normalizeKey(device.id) === normalizeKey(error.deviceId)) ||
          visibleChannels.some((channel) => normalizeKey(channel.id) === normalizeKey(error.channelId))
      ).length
    }
  }
}

function addServiceNodes(nodes: TopologyNode[], links: TopologyLink[], status: GatewayStatus | null, gatewayY: number) {
  const services = [
    {
      id: 'service:mqtt',
      name: 'MQTT',
      tone: moduleTone(
        status?.mqtt?.enabled,
        status?.mqtt?.isRunning && status?.mqtt?.isConnected,
        status?.mqtt?.lastError
      ),
      message: status?.mqtt?.lastError || status?.mqtt?.lastMessage || '-'
    },
    {
      id: 'service:opcua',
      name: 'OPC UA',
      tone: moduleTone(status?.opcUa?.enabled, status?.opcUa?.isRunning, status?.opcUa?.lastError),
      message: status?.opcUa?.lastError || status?.opcUa?.lastMessage || '-'
    },
    {
      id: 'service:history',
      name: '历史库',
      tone: moduleTone(status?.history?.enabled, status?.history?.isRunning, status?.history?.lastError),
      message: status?.history?.lastError || '-'
    },
    {
      id: 'service:rules',
      name: '规则引擎',
      tone: moduleTone(
        status?.flowRuleEngine?.enabled,
        status?.flowRuleEngine?.isRunning,
        status?.flowRuleEngine?.lastError
      ),
      message: status?.flowRuleEngine?.lastError || '-'
    }
  ]

  services.forEach((service, index) => {
    nodes.push(
      createNode({
        id: service.id,
        name: service.name,
        type: 'service',
        tone: service.tone,
        x: -120,
        y: gatewayY + (index - (services.length - 1) / 2) * 78,
        rows: [
          { label: '状态', value: toneLabel(service.tone) },
          { label: '信息', value: service.message }
        ],
        errors: []
      })
    )
    links.push({ source: 'gateway', target: service.id })
  })
}

function addChannelNodes(
  nodes: TopologyNode[],
  links: TopologyLink[],
  channels: ChannelConfig[],
  devices: DeviceConfig[],
  runtimeMap: Map<string, DeviceRuntimeStatus>,
  lanePlan: ReturnType<typeof buildTopologyLanePlan>,
  errors: RuntimeErrorDetail[]
) {
  channels.forEach((channel) => {
    const channelDevices = devices.filter((device) => normalizeKey(device.channelId) === normalizeKey(channel.id))
    const tones = channelDevices.map((device) => {
      const runtime = runtimeMap.get(deviceRuntimeMapKey(device.channelId, device.id))
      return resolveDeviceTone(device, runtime)
    })
    const tone = channel.enabled ? mergeTone(tones) : 'disabled'
    nodes.push(
      createNode({
        id: channelNodeId(channel.id),
        name: channel.name || channel.id || '未命名通道',
        type: 'channel',
        tone,
        x: 300,
        y: lanePlan.channelY.get(channelLayoutKey(channel))!,
        value: channelDevices.length,
        rows: [
          { label: '通道', value: channel.name || '-' },
          { label: '协议', value: channel.protocol || '-' },
          { label: '设备数', value: channelDevices.length },
          { label: '并发上限', value: channel.maxConcurrentDevicePolls || 0 },
          { label: '状态', value: toneLabel(tone) }
        ],
        errors: filterErrors(errors, channel.id)
      })
    )
    links.push({ source: 'gateway', target: channelNodeId(channel.id) })
  })
}

interface AddDeviceGroupArgs {
  device: DeviceConfig
  deviceId: string
  nodes: TopologyNode[]
  links: TopologyLink[]
  groupRows: GroupRow[]
  lanePlan: ReturnType<typeof buildTopologyLanePlan>
  snapshotMap: Map<string, TagValueSnapshot>
  errors: RuntimeErrorDetail[]
  showTagNodes: boolean
}

function addDeviceGroups(args: AddDeviceGroupArgs) {
  args.groupRows.forEach((row) => {
    const groupNode = groupNodeId(args.device, row.group, row.directIndex)
    const tags = row.tags
    const tagTones = tags.map((tag) =>
      resolveTagTone(tag, args.snapshotMap.get(tagConfigKey(args.device, row.group?.id || '', tag)))
    )
    const tone = row.group && !row.group.enabled ? 'disabled' : mergeTone(tagTones)
    const y = args.lanePlan.groupY.get(groupRowKey(row))!

    args.nodes.push(
      createNode({
        id: groupNode,
        name: row.group?.name || '直属标签',
        type: 'group',
        tone,
        x: 760,
        y,
        value: tags.length,
        rows: [
          { label: '设备', value: args.device.name || '-' },
          { label: '分组', value: row.group?.name || '直属标签' },
          { label: '标签数', value: tags.length },
          {
            label: '采集周期',
            value: row.group ? `${row.group.scanRateMs || 0} ms` : `${args.device.defaultScanRateMs || 0} ms`
          },
          { label: '状态', value: toneLabel(tone as TopologyTone) }
        ],
        errors: filterErrors(args.errors, args.device.channelId, args.device.id, row.group?.id || '')
      })
    )
    args.links.push({ source: args.deviceId, target: groupNode })

    addTagNodesOrSummary(args, row, groupNode, y)
  })
}

function addTagNodesOrSummary(args: AddDeviceGroupArgs, row: GroupRow, groupNode: string, groupY: number) {
  const tags = row.tags
  if (!row.expanded) {
    args.nodes.push(
      createNode({
        id: `${groupNode}:tags`,
        name: `${tags.length} 个标签`,
        type: 'tagSummary',
        tone: mergeTone(
          tags.map((tag) =>
            resolveTagTone(tag, args.snapshotMap.get(tagConfigKey(args.device, row.group?.id || '', tag)))
          )
        ),
        x: 1010,
        y: groupY,
        value: tags.length,
        rows: [
          { label: '设备', value: args.device.name || '-' },
          { label: '分组', value: row.group?.name || '直属标签' },
          { label: '标签数', value: tags.length },
          { label: '提示', value: args.showTagNodes ? '标签数量超过上限，已折叠显示' : '可打开“显示标签节点”查看明细' }
        ],
        errors: filterErrors(args.errors, args.device.channelId, args.device.id, row.group?.id || '')
      })
    )
    args.links.push({ source: groupNode, target: `${groupNode}:tags` })
    return
  }

  tags.forEach((tag, index) => {
    const snapshot = args.snapshotMap.get(tagConfigKey(args.device, row.group?.id || '', tag))
    const tone = resolveTagTone(tag, snapshot)
    const tagNode = `${groupNode}:tag:${tag.id || tag.name || index}`
    args.nodes.push(
      createNode({
        id: tagNode,
        name: tag.name || tag.address || '未命名标签',
        type: 'tag',
        tone,
        x: 1030,
        y: groupY + (index - (tags.length - 1) / 2) * tagRowSpacing,
        value: 1,
        rows: [
          { label: '地址', value: tag.address || tag.meterDataIdentifier || '-' },
          { label: '点位编码', value: tag.pointCode || '-' },
          { label: '当前值', value: snapshot?.valueText || '-' },
          { label: '质量', value: snapshot?.quality || '-' },
          { label: '单位', value: snapshot?.unit || tag.unit || '-' },
          { label: '最近错误', value: snapshot?.errorMessage || '-' }
        ],
        errors: filterErrors(args.errors, args.device.channelId, args.device.id, row.group?.id || '', tag.id)
      })
    )
    args.links.push({ source: groupNode, target: tagNode })
  })
}

interface CreateNodeArgs {
  id: string
  name: string
  type: TopologyNode['type']
  tone: TopologyTone
  x: number
  y: number
  value?: number
  rows: TopologyNode['meta']['rows']
  errors: RuntimeErrorDetail[]
}

function createNode(args: CreateNodeArgs): TopologyNode {
  return {
    id: args.id,
    name: args.name,
    type: args.type,
    tone: args.tone,
    category: nodeCategory(args.type),
    symbolSize: nodeSize(args.type, args.value),
    x: args.x,
    y: args.y,
    value: args.value,
    meta: {
      title: args.name,
      subtitle: toneLabel(args.tone),
      rows: args.rows,
      errors: args.errors.slice(0, 6)
    }
  }
}

interface GroupRow {
  device: DeviceConfig
  group: GroupConfig | null
  tags: TagConfig[]
  directIndex: number
  expanded: boolean
}

function buildGroupRows(devices: DeviceConfig[], showTagNodes: boolean) {
  const groupsByDevice = new Map<DeviceConfig, GroupRow[]>()
  const allDevicesEmpty = devices.every((device) => !device.tags?.length && !device.groups?.length)
  let remaining = tagNodeLimit
  for (const [index, device] of devices.entries()) {
    const rows: GroupRow[] = []
    function addGroup(group: GroupConfig | null, tags: TagConfig[], directIndex: number) {
      const expanded = showTagNodes && tags.length <= remaining
      if (expanded) remaining -= tags.length
      rows.push({ device, group, tags, directIndex, expanded })
    }
    if (device.tags?.length) addGroup(null, device.tags, 0)
    else if (allDevicesEmpty) addGroup(null, [], index)
    device.groups?.forEach((group, index) => addGroup(group, group.tags ?? [], index + 1))
    groupsByDevice.set(device, rows)
  }
  return groupsByDevice
}

function collectDeviceTags(device: DeviceConfig) {
  return [...(device.tags ?? []), ...(device.groups ?? []).flatMap((group) => group.tags ?? [])]
}

function matchesDevice(device: DeviceConfig, channel: ChannelConfig | undefined, query: string) {
  if (!query) return true
  const tags = collectDeviceTags(device)
  const text = [
    channel?.id,
    channel?.name,
    device.id,
    device.name,
    device.protocol,
    ...(device.groups ?? []).map((group) => group.name),
    ...tags.flatMap((tag) => [tag.name, tag.address, tag.pointCode, tag.meterAddress, tag.meterDataIdentifier])
  ]
    .map(normalizeKey)
    .join(' ')
  return text.includes(query)
}

function matchesChannel(channel: ChannelConfig, query: string) {
  return [channel.id, channel.name, channel.protocol, channel.driverId].map(normalizeKey).join(' ').includes(query)
}

function buildDeviceRuntimeMap(devices: DeviceRuntimeStatus[]) {
  const map = new Map<string, DeviceRuntimeStatus>()
  devices.forEach((device) => {
    map.set(deviceRuntimeMapKey(device.channelId, device.deviceId), device)
  })
  return map
}

function buildTagSnapshotMap(tags: TagValueSnapshot[]) {
  const map = new Map<string, TagValueSnapshot>()
  tags.forEach((tag) => {
    map.set(tagSnapshotKey(tag), tag)
  })
  return map
}

function deviceNodeId(device: DeviceConfig, index: number) {
  return `device:${device.channelId || '_'}:${device.id || index}`
}

function groupNodeId(device: DeviceConfig, group: GroupConfig | null, index: number) {
  return `group:${device.channelId || '_'}:${device.id || '_'}:${group?.id || `direct-${index}`}`
}

function groupRowKey(row: GroupRow) {
  return `${deviceLayoutKey(row.device)}:${row.group?.id || row.group?.name || `direct-${row.directIndex}`}`
}

function deviceLayoutKey(device: DeviceConfig) {
  return `${normalizeKey(device.channelId)}/${normalizeKey(device.id)}`
}

function channelLayoutKey(channel: ChannelConfig) {
  return normalizeKey(channel.id)
}

function channelNodeId(channelId: string) {
  return `channel:${channelId || '_'}`
}

function moduleTone(enabled?: boolean, running?: boolean, error?: string): TopologyTone {
  if (enabled === false) return 'disabled'
  if (error) return 'bad'
  return running ? 'good' : 'warn'
}

function deviceRuntimeMapKey(channelId: string, deviceId: string) {
  return `${normalizeKey(channelId)}/${normalizeKey(deviceId)}`
}
