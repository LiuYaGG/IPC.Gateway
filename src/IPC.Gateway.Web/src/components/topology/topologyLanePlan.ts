export const tagRowSpacing = 32
const minLaneHeight = 92

export interface TopologyDeviceLane {
  key: string
  channelKey: string
  groups: Array<{ key: string; visibleTagCount: number }>
}

export function buildTopologyLanePlan(channelKeys: string[], devices: TopologyDeviceLane[]) {
  const groupY = new Map<string, number>()
  const deviceY = new Map<string, number>()
  const channelY = new Map<string, number>()
  const channels = new Map<string, TopologyDeviceLane[]>(channelKeys.map((key) => [key, []]))
  for (const device of devices) {
    if (!channels.has(device.channelKey)) channels.set(device.channelKey, [])
    channels.get(device.channelKey)!.push(device)
  }

  let cursor = 0
  for (const [channelKey, channelDevices] of channels) {
    const channelStart = cursor
    for (const device of channelDevices) {
      const deviceStart = cursor
      for (const group of device.groups) {
        const span = Math.max(minLaneHeight, group.visibleTagCount * tagRowSpacing + 24)
        groupY.set(group.key, cursor + span / 2)
        cursor += span
      }
      if (!device.groups.length) cursor += minLaneHeight
      deviceY.set(device.key, (deviceStart + cursor) / 2)
    }
    if (!channelDevices.length) cursor += minLaneHeight
    channelY.set(channelKey, (channelStart + cursor) / 2)
  }
  return { gatewayY: cursor / 2, groupY, deviceY, channelY }
}
