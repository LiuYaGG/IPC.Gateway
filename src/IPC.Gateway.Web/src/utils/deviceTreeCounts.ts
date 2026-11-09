interface CountNode {
  type: 'root' | 'channel' | 'device' | 'group'
  channel?: { id: string }
  device?: { tags?: unknown[]; groups?: { tags?: unknown[] }[] }
  group?: { tags?: unknown[] }
}
export function deviceTreeMetric(
  node: CountNode,
  channels: readonly unknown[],
  devices: readonly { channelId?: string }[]
) {
  if (node.type === 'root') return { count: channels.length, kind: 'channels' as const }
  if (node.type === 'channel')
    return {
      count: node.channel
        ? devices.filter((device) => device.channelId?.toLowerCase() === node.channel!.id.toLowerCase()).length
        : 0,
      kind: 'devices' as const
    }
  if (node.type === 'group') return { count: node.group?.tags?.length ?? 0, kind: 'tags' as const }
  return {
    count:
      (node.device?.tags?.length ?? 0) +
      (node.device?.groups ?? []).reduce((sum, group) => sum + (group.tags?.length ?? 0), 0),
    kind: 'tags' as const
  }
}
