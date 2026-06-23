import type { DeviceConfig, ProjectConfig, TagConfig } from '../api'

export const DIRECT_TAG_GROUP_KEY = '__direct__'

export interface TagSelection {
  key: string
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
  sourcePointCode?: string
  sourceDeviceName?: string
  sourceGroupName?: string
  sourceTagName?: string
}

export function buildTagSelections(project: ProjectConfig | null | undefined): TagSelection[] {
  const items: TagSelection[] = []
  for (const device of project?.devices ?? []) {
    for (const tag of device.tags ?? []) {
      items.push(toTagSelection(device, '', '', tag))
    }
    for (const group of device.groups ?? []) {
      for (const tag of group.tags ?? []) {
        items.push(toTagSelection(device, group.id, group.name, tag))
      }
    }
  }
  return items
}

export function findTagSelectionKey(project: ProjectConfig | null | undefined, source: TagSourceIdentity) {
  return findTagSelection(project, source)?.key ?? ''
}

export function findTagSelection(project: ProjectConfig | null | undefined, source: TagSourceIdentity) {
  const pointCode = normalize(source.sourcePointCode)
  const deviceName = normalize(source.sourceDeviceName)
  const groupName = normalize(source.sourceGroupName)
  const tagName = normalize(source.sourceTagName)

  return buildTagSelections(project).find(item =>
    (!!pointCode && normalize(item.pointCode) === pointCode) ||
    (
      normalize(item.deviceName) === deviceName &&
      normalize(item.groupName) === groupName &&
      normalize(item.tagName) === tagName
    )
  )
}

function toTagSelection(device: DeviceConfig, groupId: string, groupName: string, tag: TagConfig): TagSelection {
  const groupKey = groupId || DIRECT_TAG_GROUP_KEY
  const groupLabel = groupName || '直属标签'
  const pointCode = tag.pointCode || tag.address || tag.name
  return {
    key: [device.id || device.name, groupKey, tag.id || tag.name].join('::'),
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
    label: `${device.name} / ${groupLabel} / ${tag.name}`
  }
}

function normalize(value?: string) {
  return (value ?? '').trim().toLowerCase()
}
