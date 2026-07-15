import { distributeY } from './topologyLayout'

export interface TopologyLaneSource {
  key: string
  deviceKey: string
  channelKey: string
  tagCount: number
}

export interface TopologyLanePlan {
  height: number
  groupY: Map<string, number>
  deviceY: Map<string, number>
  channelY: Map<string, number>
}

export function buildTopologyLanePlan(sources: TopologyLaneSource[], channels: string[], showTagNodes: boolean): TopologyLanePlan {
  const groupY = new Map<string, number>()
  const deviceBands = new Map<string, number[]>()
  const channelBands = new Map<string, number[]>()
  let cursor = 120

  for (const source of sources) {
    const laneHeight = showTagNodes
      ? Math.max(96, source.tagCount * 34 + 56)
      : 88
    const y = Math.round(cursor + laneHeight / 2)
    groupY.set(source.key, y)
    addBand(deviceBands, source.deviceKey, y)
    addBand(channelBands, source.channelKey, y)
    cursor += laneHeight
  }

  const height = Math.max(720, cursor + 120)
  return {
    height,
    groupY,
    deviceY: averageBands(deviceBands),
    channelY: channels.length
      ? averageChannelBands(channelBands, channels, height)
      : new Map<string, number>()
  }
}

function addBand(map: Map<string, number[]>, key: string, y: number) {
  const values = map.get(key) ?? []
  values.push(y)
  map.set(key, values)
}

function averageBands(map: Map<string, number[]>) {
  const result = new Map<string, number>()
  for (const [key, values] of map.entries()) {
    result.set(key, Math.round(values.reduce((sum, value) => sum + value, 0) / Math.max(1, values.length)))
  }
  return result
}

function averageChannelBands(map: Map<string, number[]>, channels: string[], height: number) {
  const result = averageBands(map)
  channels.forEach((channel, index) => {
    if (!result.has(channel)) {
      result.set(channel, distributeY(index, channels.length, height))
    }
  })
  return result
}
