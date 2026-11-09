export const minimumColumnWidth = 80
export const maximumColumnWidth = 1200
export type ColumnWidths = Record<string, number>
export function columnWidth(value: unknown, fallback = 140) {
  const number = Number(value)
  return Number.isFinite(number)
    ? Math.max(minimumColumnWidth, Math.min(maximumColumnWidth, Math.round(number)))
    : fallback
}
export function readColumnWidths(key: string): ColumnWidths {
  try {
    const source = JSON.parse(localStorage.getItem(key) ?? '{}')
    if (!source || Array.isArray(source) || typeof source !== 'object') return {}
    return Object.fromEntries(
      Object.entries(source)
        .filter(([, value]) => typeof value === 'number' && Number.isFinite(value))
        .map(([name, value]) => [name, columnWidth(value)])
    )
  } catch {
    return {}
  }
}
export function saveColumnWidths(key: string, widths: ColumnWidths) {
  try {
    localStorage.setItem(key, JSON.stringify(widths))
  } catch {
    /* Resizing still works when storage is unavailable. */
  }
}
