import type { EChartsOption } from 'echarts'
import { toneColorMap, topologyCategories } from './topologyLayout'
import type { DeviceTopologyModel, TopologyNode } from './topologyTypes'

export function buildTopologyOption(model: DeviceTopologyModel, selectedNodeId: string): EChartsOption {
  return {
    animationDurationUpdate: 320,
    tooltip: {
      trigger: 'item',
      formatter: params => formatTooltip((params as any).data?.rawNode)
    },
    legend: {
      top: 8,
      left: 12,
      itemWidth: 10,
      itemHeight: 10,
      textStyle: { color: '#475569', fontSize: 12 },
      data: topologyCategories.map(item => item.name)
    },
    series: [{
      type: 'graph',
      layout: 'none',
      roam: true,
      scaleLimit: { min: 0.35, max: 2.6 },
      categories: topologyCategories,
      data: model.nodes.map(node => ({
        id: node.id,
        name: shortLabel(node.name),
        category: node.category,
        symbolSize: node.symbolSize,
        x: node.x,
        y: node.y,
        value: node.value,
        rawNode: node,
        itemStyle: {
          color: toneColorMap[node.tone],
          borderColor: selectedNodeId === node.id ? '#0f172a' : '#ffffff',
          borderWidth: selectedNodeId === node.id ? 4 : 2,
          shadowBlur: selectedNodeId === node.id ? 14 : 6,
          shadowColor: 'rgba(15, 23, 42, 0.18)'
        },
        label: {
          show: true,
          position: node.type === 'tag' ? 'right' : 'bottom',
          distance: node.type === 'tag' ? 6 : 8,
          color: '#0f172a',
          fontSize: node.type === 'tag' ? 11 : 12,
          fontWeight: node.type === 'gateway' || node.type === 'device' ? 700 : 500
        }
      })),
      links: model.links.map(link => ({
        source: link.source,
        target: link.target,
        lineStyle: { color: '#94a3b8', width: 1.5, curveness: 0.08 },
        emphasis: { lineStyle: { color: '#2563eb', width: 2.5 } }
      })),
      edgeSymbol: ['none', 'arrow'],
      edgeSymbolSize: [0, 7],
      emphasis: {
        focus: 'adjacency',
        lineStyle: { width: 3 }
      },
      labelLayout: { hideOverlap: true },
      left: 24,
      right: 24,
      top: 44,
      bottom: 24
    }]
  }
}

function formatTooltip(node?: TopologyNode) {
  if (!node) return ''
  const rows = node.meta.rows
    .slice(0, 5)
    .map(row => `<div><span style="color:#64748b">${escapeHtml(row.label)}：</span>${escapeHtml(String(row.value || '-'))}</div>`)
    .join('')
  return `<strong>${escapeHtml(node.meta.title)}</strong><div style="margin-top:6px">${rows}</div>`
}

function shortLabel(value: string) {
  const text = value || '-'
  return text.length > 16 ? `${text.slice(0, 15)}...` : text
}

function escapeHtml(value: string) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}
