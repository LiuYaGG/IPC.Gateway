import type { RuntimeErrorDetail } from '../../api'

export type TopologyNodeType = 'gateway' | 'protocol' | 'device' | 'group' | 'tag' | 'tagSummary' | 'service'
export type TopologyTone = 'good' | 'warn' | 'bad' | 'disabled' | 'normal'

export interface TopologyNodeMeta {
  title: string
  subtitle: string
  rows: Array<{ label: string; value: string | number }>
  errors: RuntimeErrorDetail[]
}

export interface TopologyNode {
  id: string
  name: string
  type: TopologyNodeType
  tone: TopologyTone
  category: number
  symbolSize: number
  x: number
  y: number
  value?: number
  meta: TopologyNodeMeta
}

export interface TopologyLink {
  source: string
  target: string
  value?: string
}

export interface TopologySummary {
  protocols: number
  devices: number
  onlineDevices: number
  offlineDevices: number
  disabledDevices: number
  groups: number
  tags: number
  visibleTags: number
  hiddenTags: number
  recentErrors: number
}

export interface DeviceTopologyModel {
  nodes: TopologyNode[]
  links: TopologyLink[]
  summary: TopologySummary
  matchedDeviceIds: Set<string>
  tagNodeLimitReached: boolean
}
