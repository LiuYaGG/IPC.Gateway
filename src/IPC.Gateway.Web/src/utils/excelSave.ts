// File System Access is available in Chrome/Edge on HTTPS and loopback origins.
// Invoke the native picker before awaiting the export request to preserve user activation.
interface ExcelFileHandle {
  createWritable(): Promise<{ write(data: Blob): Promise<void>; close(): Promise<void>; abort(): Promise<void> }>
}
type SavePicker = (options: {
  suggestedName: string
  types: { description: string; accept: Record<string, string[]> }[]
  excludeAcceptAllOption: boolean
}) => Promise<ExcelFileHandle>

export function localExportFileName(prefix: string, page: number) {
  const now = new Date()
  const local = new Date(+now - now.getTimezoneOffset() * 60000).toISOString().slice(0, 19).replace(/[:T]/g, '-')
  return `${prefix}-${local}-p${page}.xlsx`
}

export async function saveExcelAs(name: string, getBlob: () => Promise<Blob>, signal: AbortSignal) {
  const picker = (window as unknown as { showSaveFilePicker?: SavePicker }).showSaveFilePicker
  if (!picker) throw new Error('当前浏览器环境不支持选择保存路径。请使用 Chrome 或 Edge，通过 HTTPS 或本机 localhost、127.0.0.1 访问。')
  try {
    const handle = await picker.call(window, {
      suggestedName: name,
      types: [{ description: 'Excel (.xlsx)', accept: { 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': ['.xlsx'] } }],
      excludeAcceptAllOption: true
    })
    signal.throwIfAborted()
    const blob = await getBlob()
    signal.throwIfAborted()
    const output = await handle.createWritable()
    try {
      signal.throwIfAborted()
      await output.write(blob)
      signal.throwIfAborted()
      await output.close()
    } catch (cause) {
      await output.abort().catch(() => undefined)
      throw cause
    }
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') return
    throw cause
  }
}
