import type { TopologyTone } from './topologyTypes'

export const topologyCategories = [
  { name: '网关' },
  { name: '协议' },
  { name: '设备' },
  { name: '分组' },
  { name: '标签' },
  { name: '服务' }
]

export const toneColorMap: Record<TopologyTone, string> = {
  good: '#16a34a',
  warn: '#d97706',
  bad: '#dc2626',
  disabled: '#94a3b8',
  normal: '#2563eb'
}

export function distributeY(index: number, count: number, height: number) {
  if (count <= 1) return Math.round(height / 2)
  const gap = height / (count + 1)
  return Math.round(gap * (index + 1))
}

export function nodeCategory(type: string) {
  if (type === 'gateway') return 0
  if (type === 'protocol') return 1
  if (type === 'device') return 2
  if (type === 'group') return 3
  if (type === 'tag' || type === 'tagSummary') return 4
  return 5
}

export function nodeSize(type: string, value = 1) {
  if (type === 'gateway') return 68
  if (type === 'protocol') return 48
  if (type === 'device') return Math.min(58, 40 + value * 2)
  if (type === 'group') return 34
  if (type === 'tag') return 22
  if (type === 'tagSummary') return 30
  return 36
}
