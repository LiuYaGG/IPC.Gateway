import type { DeviceConfig, ProjectConfig, TagConfig } from '../api'

export const DIRECT_TAG_GROUP_KEY = '__direct__'

export interface TagSelection {
  key: string
  channelId: string
  channelName: string
  deviceId: string
  groupId: string
  tagId: string
  deviceName: string
  groupName: string
  tagName: string
  pointCode: string
  dataType: string
  address: string
  label: string
  groupLabel: string
}

export interface TagSourceIdentity {
  sourceTagId?: string
}

export function buildTagSelections(project: ProjectConfig | null | undefined): TagSelection[] {
  const items: TagSelection[] = []
  for (const device of project?.devices ?? []) {
    const channel = project?.channels?.find(item => item.id === device.channelId)
    const channelId = channel?.id || device.channelId || ''
    const channelName = channel?.name || '未分配通道'
    for (const tag of device.tags ?? []) {
      items.push(toTagSelection(channelId, channelName, device, '', '', tag))
    }
    for (const group of device.groups ?? []) {
      for (const tag of group.tags ?? []) {
        items.push(toTagSelection(channelId, channelName, device, group.id, group.name, tag))
      }
    }
  }
  return items
}

export function findTagSelectionKey(project: ProjectConfig | null | undefined, source: TagSourceIdentity) {
  return findTagSelection(project, source)?.key ?? ''
}

export function findTagSelection(project: ProjectConfig | null | undefined, source: TagSourceIdentity) {
  const tagId = normalize(source.sourceTagId)
  return tagId
    ? buildTagSelections(project).find(item => normalize(item.tagId) === tagId)
    : undefined
}

function toTagSelection(channelId: string, channelName: string, device: DeviceConfig, groupId: string, groupName: string, tag: TagConfig): TagSelection {
  const groupKey = groupId || DIRECT_TAG_GROUP_KEY
  const groupLabel = groupName || '直属标签'
  const pointCode = tag.pointCode || ''
  return {
    key: [channelId, device.id, groupKey, tag.id].join('::'),
    channelId,
    channelName,
    deviceId: device.id,
    groupId,
    tagId: tag.id,
    deviceName: device.name,
    groupName,
    tagName: tag.name,
    pointCode,
    dataType: tag.dataType || '',
    address: tag.address || '',
    groupLabel,
    label: `${channelName} / ${device.name} / ${groupLabel} / ${tag.name}`
  }
}

function normalize(value?: string) {
  return (value ?? '').trim().toLowerCase()
}
