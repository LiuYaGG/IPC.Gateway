import { tx } from './index'

// Translate chart UI metadata; graph node names and numeric samples remain untouched.
export function localizeChartOption<T>(option: T): T {
  const source = option as any
  const translateList = (items: any, convert: (item: any) => any) =>
    Array.isArray(items) ? items.map(convert) : items ? convert(items) : items
  return {
    ...source,
    ...(source.title
      ? { title: translateList(source.title, (item) => ({ ...item, text: tx(item.text), subtext: tx(item.subtext) })) }
      : {}),
    ...(source.legend
      ? {
          legend: translateList(source.legend, (item) => ({
            ...item,
            ...(item.data
              ? {
                  data: item.data.map((entry: any) =>
                    typeof entry === 'string' ? tx(entry) : { ...entry, name: tx(entry.name) }
                  )
                }
              : {})
          }))
        }
      : {}),
    ...(source.xAxis
      ? { xAxis: translateList(source.xAxis, (item) => ({ ...item, ...(item.name ? { name: tx(item.name) } : {}) })) }
      : {}),
    ...(source.yAxis
      ? { yAxis: translateList(source.yAxis, (item) => ({ ...item, ...(item.name ? { name: tx(item.name) } : {}) })) }
      : {}),
    ...(source.series
      ? {
          series: translateList(source.series, (item) => ({
            ...item,
            ...(item.name ? { name: tx(item.name) } : {}),
            ...(['pie', 'gauge'].includes(item.type) && item.data
              ? {
                  data: item.data.map((entry: any) =>
                    typeof entry === 'object' ? { ...entry, ...(entry.name ? { name: tx(entry.name) } : {}) } : entry
                  )
                }
              : {})
          }))
        }
      : {})
  } as T
}
