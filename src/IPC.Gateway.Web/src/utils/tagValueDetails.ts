export function tagArrayElements(type: string, text: string): string[] | undefined {
  if (!type.toLowerCase().endsWith('array')) return
  // Supported PLC arrays contain numbers or booleans and use invariant comma-separated formatting.
  // Keep element text unchanged in precision: never round-trip Int64/UInt64 through Number.
  return text === '' ? [] : text.split(',').map((item) => item.trim())
}

export function visibleArrayRange(scrollTop: number, height: number, length: number, rowHeight = 36) {
  const start = Math.max(0, Math.floor(scrollTop / rowHeight) - 6)
  return { start, end: Math.min(length, start + Math.ceil(height / rowHeight) + 12) }
}
