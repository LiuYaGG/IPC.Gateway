import { locale, tx } from '../../i18n'

const english: Record<string, string> = {
  '点位曲线': 'Tag trends',
  '查询曲线': 'Load trends',
  '选择数值点位': 'Select numeric tags',
  '最多选择 6 个点位，切换通道、设备或分组会保留已选点位。': 'Select up to 6 tags. Switching channels, devices or groups keeps the current selection.',
  '请先选择通道、设备和分组': 'Select a channel, device and group first',
  '请先选择数值点位和时间范围': 'Select numeric tags and a time range',
  '所选时间范围内没有可绘制的有效数值': 'No valid numeric samples to plot in this range',
  '部分点位在所选时段没有可绘制的有效数值': 'Some tags have no valid numeric samples in the selected range',
  '无单位': 'No unit',
  '本地时间 · 工程值 · 缩放查看细节': 'Local time · Engineering value · Zoom to inspect',
  '切换曲线显示': 'Toggle trend visibility',
  '压缩时间范围后重新查询可查看更多样本。': 'Query a narrower time range to see more samples.',
  '补点': 'Filled',
  '时间对齐': 'Aligned',
  '聚合': 'Aggregated'
}

export function trendText(text: string) {
  return locale.value === 'en-US' ? english[text] ?? tx(text) : text
}
