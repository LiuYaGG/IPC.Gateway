import { nextTick, shallowRef } from 'vue'
import type { TagValueSnapshot } from '../api'

const received = new WeakMap<TagValueSnapshot, number>()
let target: { deviceId: string; tagId: string; expires: number } | undefined
export const browserTrace = shallowRef<{
  timestamp: string; value: string; pushMs: number; domMs?: number
}>()

export function observeBrowserTrace(deviceId: string, tagId: string, durationMs: number) {
  const selection = { deviceId, tagId, expires: performance.now() + durationMs }
  target = selection
  browserTrace.value = undefined
  return () => {
    if (target === selection) {
      target = undefined
      browserTrace.value = undefined
    }
  }
}

// Use this browser's monotonic clock at both ends of the DOM measurement.
export function traceReceived(tag: TagValueSnapshot, arrived: number) {
  if (target && arrived < target.expires && tag.deviceId === target.deviceId && tag.tagId === target.tagId)
    received.set(tag, arrived)
}

export function traceApplied(tag: TagValueSnapshot) {
  const arrived = received.get(tag)
  if (arrived === undefined || tag.diagnosticPushQueueMs === undefined || !target ||
      tag.deviceId !== target.deviceId || tag.tagId !== target.tagId || performance.now() >= target.expires) return
  const sample = { timestamp: tag.timestamp, value: tag.valueText, pushMs: tag.diagnosticPushQueueMs }
  browserTrace.value = sample
  void nextTick(() => {
    if (browserTrace.value === sample) browserTrace.value = { ...sample, domMs: performance.now() - arrived }
  })
}
