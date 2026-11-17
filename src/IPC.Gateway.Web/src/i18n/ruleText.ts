import catalog from '../../../IPC.Gateway.Core/Localization/rule-text.json'

const labels = new Map<string, string[]>()
for (const [code, pair] of Object.entries(catalog.labels)) {
  labels.set(code.toLowerCase(), pair)
  for (const alias of pair) if (!labels.has(alias.toLowerCase())) labels.set(alias.toLowerCase(), pair)
}

export function ruleStateText(value: string | undefined | null, language: string, depth = 0): string {
  if (!value) return '—'
  const index = language === 'en-US' ? 1 : 0
  const pair = labels.get(value.toLowerCase())
  if (pair) return pair[index]!
  if (depth < 8) for (const [code, prefix] of Object.entries(catalog.prefixes)) {
    for (const candidate of [code, ...prefix]) if (value.toLowerCase().startsWith(candidate.toLowerCase()))
      return prefix[index] + ruleStateText(value.slice(candidate.length).trimStart(), language, depth + 1)
  }
  return value
}

// Used by the general runtime translator for both legacy English errors and system messages.
export const ruleMessages: ReadonlyArray<readonly [string, string]> = catalog.messages.map(pair => [pair[0]!, pair[1]!])
