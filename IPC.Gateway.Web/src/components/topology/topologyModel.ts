import type { DeviceConfig, DeviceRuntimeStatus, GatewayStatus, GroupConfig, ProjectConfig, RuntimeErrorDetail, TagConfig, TagValueSnapshot } from '../../api'
import { nodeCategory, nodeSize } from './topologyLayout'
import { buildTopologyLanePlan } from './topologyLanePlan'
import { filterErrors, mergeTone, normalizeKey, resolveDeviceTone, resolveTagTone, tagConfigKey, tagSnapshotKey, toneLabel } from './topologyStatus'
import type { DeviceTopologyModel, TopologyLink, TopologyNode, TopologyTone } from './topologyTypes'

const tagNodeLimit = 220

export interface BuildTopologyInput {
  project: ProjectConfig | null
  status: GatewayStatus | null
  search: string
  showTagNodes: boolean
}

export function buildDeviceTopology(input: BuildTopologyInput): DeviceTopologyModel {
  const devices = input.project?.devices ?? []
  const query = normalizeKey(input.search)
  const runtimeMap = buildDeviceRuntimeMap(input.status?.devices ?? [])
  const snapshotMap = buildTagSnapshotMap(input.status?.tags ?? [])
  const errors = input.status?.recentErrors ?? []
  const visibleDevices = devices.filter(device => matchesDevice(device, query))
  const protocolNames = Array.from(new Set(visibleDevices.map(device => device.protocol || 'Unknown'))).sort()
  const groupRows = buildGroupRows(visibleDevices)
  const tagCount = countTags(visibleDevices)
  const lanePlan = buildTopologyLanePlan(
    groupRows.map(row => ({
      key: groupRowKey(row),
      deviceKey: deviceLayoutKey(row.device),
      protocol: row.device.protocol || 'Unknown',
      tagCount: row.tags.length
    })),
    protocolNames,
    input.showTagNodes
  )
  const height = lanePlan.height

  const nodes: TopologyNode[] = []
  const links: TopologyLink[] = []
  const matchedDeviceIds = new Set<string>()
  let renderedTagNodes = 0
  let hiddenTags = 0

  nodes.push(createNode({
    id: 'gateway',
    name: input.project?.name || input.status?.projectName || 'IPC Gateway',
    type: 'gateway',
    tone: input.status?.isRunning ? 'good' : 'warn',
    x: 90,
    y: Math.round(height / 2),
    value: visibleDevices.length,
    rows: [
      { label: '项目', value: input.project?.name || input.status?.projectName || '-' },
      { label: '设备', value: visibleDevices.length },
      { label: '标签', value: tagCount },
      { label: '状态', value: input.status?.isRunning ? '运行中' : '未运行' }
    ],
    errors
  }))

  addServiceNodes(nodes, links, input.status)
  addProtocolNodes(nodes, links, protocolNames, visibleDevices, runtimeMap, lanePlan)

  visibleDevices.forEach((device, index) => {
    const deviceId = deviceNodeId(device, index)
    matchedDeviceIds.add(device.id || device.name)
    const runtime = runtimeMap.get(normalizeKey(device.id)) ?? runtimeMap.get(normalizeKey(device.name))
    const deviceTags = collectDeviceTags(device)
    const deviceErrors = filterErrors(errors, device.name)
    const y = lanePlan.deviceY.get(deviceLayoutKey(device)) ?? Math.round(height / 2)
    const tone = resolveDeviceTone(device, runtime)

    nodes.push(createNode({
      id: deviceId,
      name: device.name || device.id || '未命名设备',
      type: 'device',
      tone,
      x: 520,
      y,
      value: deviceTags.length,
      rows: [
        { label: '协议', value: device.protocol || '-' },
        { label: '状态', value: toneLabel(tone) },
        { label: '成功率', value: `${Number(runtime?.successRate || 0).toFixed(1)}%` },
        { label: '标签', value: deviceTags.length },
        { label: '最近错误', value: runtime?.lastError || '-' }
      ],
      errors: deviceErrors
    }))
    links.push({ source: `protocol:${device.protocol || 'Unknown'}`, target: deviceId, value: device.protocol || '-' })

    addDeviceGroups({
      device,
      deviceId,
      nodes,
      links,
      groupRows,
      lanePlan,
      height,
      snapshotMap,
      errors,
      showTagNodes: input.showTagNodes,
      renderedTagNodes
    })
    renderedTagNodes = countRenderedTagNodes(nodes)
    hiddenTags = Math.max(0, tagCount - renderedTagNodes)
  })

  return {
    nodes,
    links,
    matchedDeviceIds,
    tagNodeLimitReached: input.showTagNodes && tagCount > tagNodeLimit,
    summary: {
      protocols: protocolNames.length,
      devices: visibleDevices.length,
      onlineDevices: visibleDevices.filter(device => {
        const runtime = runtimeMap.get(normalizeKey(device.id)) ?? runtimeMap.get(normalizeKey(device.name))
        return device.enabled && !!runtime?.isConnected
      }).length,
      offlineDevices: visibleDevices.filter(device => {
        const runtime = runtimeMap.get(normalizeKey(device.id)) ?? runtimeMap.get(normalizeKey(device.name))
        return device.enabled && !runtime?.isConnected
      }).length,
      disabledDevices: visibleDevices.filter(device => !device.enabled).length,
      groups: groupRows.filter(row => row.group).length,
      tags: tagCount,
      visibleTags: renderedTagNodes,
      hiddenTags,
      recentErrors: errors.filter(error => !query || visibleDevices.some(device => normalizeKey(device.name) === normalizeKey(error.deviceName))).length
    }
  }
}

function addServiceNodes(nodes: TopologyNode[], links: TopologyLink[], status: GatewayStatus | null) {
  const services = [
    { id: 'service:mqtt', name: 'MQTT', tone: moduleTone(status?.mqtt?.enabled, status?.mqtt?.isRunning && status?.mqtt?.isConnected, status?.mqtt?.lastError), message: status?.mqtt?.lastError || status?.mqtt?.lastMessage || '-' },
    { id: 'service:opcua', name: 'OPC UA', tone: moduleTone(status?.opcUa?.enabled, status?.opcUa?.isRunning, status?.opcUa?.lastError), message: status?.opcUa?.lastError || status?.opcUa?.lastMessage || '-' },
    { id: 'service:history', name: '历史库', tone: moduleTone(status?.history?.enabled, status?.history?.isRunning, status?.history?.lastError), message: status?.history?.lastError || '-' },
    { id: 'service:rules', name: '规则引擎', tone: moduleTone(status?.ruleEngine?.enabled, status?.ruleEngine?.isRunning, status?.ruleEngine?.lastError), message: status?.ruleEngine?.lastError || '-' }
  ]

  services.forEach((service, index) => {
    nodes.push(createNode({
      id: service.id,
      name: service.name,
      type: 'service',
      tone: service.tone,
      x: 92,
      y: 58 + index * 58,
      rows: [
        { label: '状态', value: toneLabel(service.tone) },
        { label: '信息', value: service.message }
      ],
      errors: []
    }))
    links.push({ source: 'gateway', target: service.id })
  })
}

function addProtocolNodes(nodes: TopologyNode[], links: TopologyLink[], protocolNames: string[], devices: DeviceConfig[], runtimeMap: Map<string, DeviceRuntimeStatus>, lanePlan: ReturnType<typeof buildTopologyLanePlan>) {
  protocolNames.forEach((protocol, index) => {
    const protocolDevices = devices.filter(device => (device.protocol || 'Unknown') === protocol)
    const tones = protocolDevices.map(device => {
      const runtime = runtimeMap.get(normalizeKey(device.id)) ?? runtimeMap.get(normalizeKey(device.name))
      return resolveDeviceTone(device, runtime)
    })
    const tone = mergeTone(tones)
    nodes.push(createNode({
      id: `protocol:${protocol}`,
      name: protocol,
      type: 'protocol',
      tone,
      x: 300,
      y: lanePlan.protocolY.get(protocol) ?? 160 + index * 90,
      value: protocolDevices.length,
      rows: [
        { label: '协议', value: protocol },
        { label: '设备数', value: protocolDevices.length },
        { label: '状态', value: toneLabel(tone) }
      ],
      errors: []
    }))
    links.push({ source: 'gateway', target: `protocol:${protocol}` })
  })
}

interface AddDeviceGroupArgs {
  device: DeviceConfig
  deviceId: string
  nodes: TopologyNode[]
  links: TopologyLink[]
  groupRows: GroupRow[]
  lanePlan: ReturnType<typeof buildTopologyLanePlan>
  height: number
  snapshotMap: Map<string, TagValueSnapshot>
  errors: RuntimeErrorDetail[]
  showTagNodes: boolean
  renderedTagNodes: number
}

function addDeviceGroups(args: AddDeviceGroupArgs) {
  const rows = args.groupRows.filter(row => row.device === args.device)
  rows.forEach(row => {
    args.renderedTagNodes = countRenderedTagNodes(args.nodes)
    const groupNode = groupNodeId(args.device, row.group, row.directIndex)
    const tags = row.tags
    const tagTones = tags.map(tag => resolveTagTone(tag, args.snapshotMap.get(tagConfigKey(args.device, row.group?.id || '', tag))))
    const tone = row.group && !row.group.enabled ? 'disabled' : mergeTone(tagTones)
    const y = args.lanePlan.groupY.get(groupRowKey(row)) ?? Math.round(args.height / 2)

    args.nodes.push(createNode({
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
        { label: '采集周期', value: row.group ? `${row.group.scanRateMs || 0} ms` : `${args.device.defaultScanRateMs || 0} ms` },
        { label: '状态', value: toneLabel(tone as TopologyTone) }
      ],
      errors: filterErrors(args.errors, args.device.name, row.group?.name || '')
    }))
    args.links.push({ source: args.deviceId, target: groupNode })

    addTagNodesOrSummary(args, row, groupNode, y)
  })
}

function addTagNodesOrSummary(args: AddDeviceGroupArgs, row: GroupRow, groupNode: string, groupY: number) {
  const tags = row.tags
  if (!args.showTagNodes || args.renderedTagNodes + tags.length > tagNodeLimit) {
    args.nodes.push(createNode({
      id: `${groupNode}:tags`,
      name: `${tags.length} 个标签`,
      type: 'tagSummary',
      tone: mergeTone(tags.map(tag => resolveTagTone(tag, args.snapshotMap.get(tagConfigKey(args.device, row.group?.id || '', tag))))),
      x: 1010,
      y: groupY,
      value: tags.length,
      rows: [
        { label: '设备', value: args.device.name || '-' },
        { label: '分组', value: row.group?.name || '直属标签' },
        { label: '标签数', value: tags.length },
        { label: '提示', value: args.showTagNodes ? '标签数量超过上限，已折叠显示' : '可打开“显示标签节点”查看明细' }
      ],
      errors: filterErrors(args.errors, args.device.name, row.group?.name || '')
    }))
    args.links.push({ source: groupNode, target: `${groupNode}:tags` })
    return
  }

  tags.forEach((tag, index) => {
    const snapshot = args.snapshotMap.get(tagConfigKey(args.device, row.group?.id || '', tag))
    const tone = resolveTagTone(tag, snapshot)
    const tagNode = `${groupNode}:tag:${tag.id || tag.name || index}`
    args.nodes.push(createNode({
      id: tagNode,
      name: tag.name || tag.address || '未命名标签',
      type: 'tag',
      tone,
      x: 1030,
      y: groupY + (index - (tags.length - 1) / 2) * 30,
      value: 1,
      rows: [
        { label: '地址', value: tag.address || tag.meterDataIdentifier || '-' },
        { label: '点位编码', value: tag.pointCode || '-' },
        { label: '当前值', value: snapshot?.valueText || '-' },
        { label: '质量', value: snapshot?.quality || '-' },
        { label: '单位', value: snapshot?.unit || tag.unit || '-' },
        { label: '最近错误', value: snapshot?.errorMessage || '-' }
      ],
      errors: filterErrors(args.errors, args.device.name, row.group?.name || '', tag.name)
    }))
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
}

function buildGroupRows(devices: DeviceConfig[]) {
  const rows: GroupRow[] = []
  devices.forEach(device => {
    if ((device.tags ?? []).length > 0) {
      rows.push({ device, group: null, tags: device.tags ?? [], directIndex: 0 })
    }
    ;(device.groups ?? []).forEach((group, index) => rows.push({ device, group, tags: group.tags ?? [], directIndex: index + 1 }))
  })
  return rows.length > 0 ? rows : devices.map((device, index) => ({ device, group: null, tags: [], directIndex: index }))
}

function collectDeviceTags(device: DeviceConfig) {
  return [...(device.tags ?? []), ...(device.groups ?? []).flatMap(group => group.tags ?? [])]
}

function countTags(devices: DeviceConfig[]) {
  return devices.reduce((sum, device) => sum + collectDeviceTags(device).length, 0)
}

function matchesDevice(device: DeviceConfig, query: string) {
  if (!query) return true
  const tags = collectDeviceTags(device)
  const text = [
    device.id,
    device.name,
    device.protocol,
    ...(device.groups ?? []).map(group => group.name),
    ...tags.flatMap(tag => [tag.name, tag.address, tag.pointCode, tag.meterAddress, tag.meterDataIdentifier])
  ].map(normalizeKey).join(' ')
  return text.includes(query)
}

function buildDeviceRuntimeMap(devices: DeviceRuntimeStatus[]) {
  const map = new Map<string, DeviceRuntimeStatus>()
  devices.forEach(device => {
    map.set(normalizeKey(device.deviceId), device)
    map.set(normalizeKey(device.deviceName), device)
    map.set(deviceRuntimeKeyFallback(device), device)
  })
  return map
}

function buildTagSnapshotMap(tags: TagValueSnapshot[]) {
  const map = new Map<string, TagValueSnapshot>()
  tags.forEach(tag => {
    map.set(tagSnapshotKey(tag), tag)
    map.set([
      normalizeKey(tag.deviceName),
      normalizeKey(tag.groupName),
      normalizeKey(tag.tagName)
    ].join('/'), tag)
  })
  return map
}

function deviceNodeId(device: DeviceConfig, index: number) {
  return `device:${device.id || device.name || index}`
}

function groupNodeId(device: DeviceConfig, group: GroupConfig | null, index: number) {
  return `group:${device.id || device.name}:${group?.id || group?.name || `direct-${index}`}`
}

function groupRowKey(row: GroupRow) {
  return `${deviceLayoutKey(row.device)}:${row.group?.id || row.group?.name || `direct-${row.directIndex}`}`
}

function deviceLayoutKey(device: DeviceConfig) {
  return device.id || device.name || 'device'
}

function moduleTone(enabled?: boolean, running?: boolean, error?: string): TopologyTone {
  if (enabled === false) return 'disabled'
  if (error) return 'bad'
  return running ? 'good' : 'warn'
}

function countRenderedTagNodes(nodes: TopologyNode[]) {
  return nodes.filter(node => node.type === 'tag').length
}

function deviceRuntimeKeyFallback(device: DeviceRuntimeStatus) {
  return `${normalizeKey(device.deviceId)}|${normalizeKey(device.deviceName)}`
}
