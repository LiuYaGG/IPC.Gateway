import { ref, watch } from 'vue'
import { english } from './en'
import { extraEnglish } from './extra'
import { helpEnglish } from './help'
Object.assign(english, extraEnglish, helpEnglish)

export type UiLocale = 'zh-CN' | 'en-US'
export const localeStorageKey = 'ipc.gateway.locale'
function savedLocale(): UiLocale {
  try {
    return localStorage.getItem(localeStorageKey) === 'en-US' ? 'en-US' : 'zh-CN'
  } catch {
    return 'zh-CN'
  }
}
export const locale = ref<UiLocale>(savedLocale())
export function setLocale(value: UiLocale) {
  locale.value = value
}
watch(
  locale,
  (value) => {
    try {
      localStorage.setItem(localeStorageKey, value)
    } catch {
      /* Keep the in-memory choice when storage is unavailable. */
    }
    if (typeof document !== 'undefined') document.documentElement.lang = value
  },
  { immediate: true }
)

const terms = Object.keys(english).sort((a, b) => b.length - a.length)
const termIndex = new Map<string, string[]>()
for (const term of terms) {
  const initial = term[0]
  const group = termIndex.get(initial) ?? []
  group.push(term)
  termIndex.set(initial, group)
}
const englishCache = new Map<string, string>()
const formattedValues = new Set<string>()

// Translate UI labels only; all call sites explicitly exclude names, addresses and collected values.
export function tx(value: unknown): string {
  if (value === undefined || value === null) return ''
  const text = String(value)
  if (formattedValues.has(text)) return text
  if (locale.value === 'zh-CN' || !/[\u3400-\u9fff]/.test(text)) return text
  const trimmed = text.trim()
  let translated = english[trimmed] ?? englishCache.get(trimmed)
  if (translated === undefined) {
    translated = translateUiText(trimmed)
    englishCache.set(trimmed, translated)
  }
  return text.slice(0, text.indexOf(trimmed)) + translated + text.slice(text.indexOf(trimmed) + trimmed.length)
}
export function formatMessage(source: string, values: unknown[]) {
  const translated = tx(source).replace(/\{(\d+)\}/g, (_, index) => String(values[Number(index)] ?? ''))
  formattedValues.add(translated)
  if (formattedValues.size > 1000) formattedValues.delete(formattedValues.values().next().value!)
  return translated
}
function translateUiText(text: string): string {
  const permission = /^当前用户没有(.+)权限$/.exec(text)
  if (permission) return `You do not have permission to ${translateUiText(permission[1]).toLowerCase()}.`
  const confirm = /^确认删除(.+?)[“「](.*)[”」][？?]$/.exec(text)
  if (confirm) return `Delete ${translateUiText(confirm[1]).toLowerCase()} "${confirm[2]}"?`
  const segments: string[] = []
  for (let index = 0; index < text.length; ) {
    const term = termIndex.get(text[index])?.find((term) => text.startsWith(term, index))
    if (term) {
      segments.push(english[term])
      index += term.length
      continue
    }
    const char = text[index]
    if (/[\u3400-\u9fff]/.test(char)) return text
    let end = index + 1
    while (end < text.length && !/[\u3400-\u9fff]/.test(text[end])) end++
    segments.push(text.slice(index, end))
    index = end
  }
  return segments
    .join(' ')
    .replace(/\s+([,.;:!?，。；：！？)\]])/g, '$1')
    .replace(/([([/])\s+/g, '$1')
    .replace(/\s+/g, ' ')
    .replaceAll('：', ': ')
    .replaceAll('，', ', ')
    .replaceAll('。', '. ')
    .replaceAll('；', '; ')
    .replaceAll('？', '?')
    .trim()
}

export function localizeColumns<T extends { title?: unknown; children?: any[] }>(columns: T[]): T[] {
  // Reading locale here also updates columns whose titles are produced by callbacks.
  void locale.value
  return columns.map((column) => ({
    ...column,
    title: typeof column.title === 'string' ? tx(column.title) : column.title,
    ...(column.children ? { children: localizeColumns(column.children) } : {})
  }))
}
