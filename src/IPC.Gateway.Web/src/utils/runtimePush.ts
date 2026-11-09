import type { RuntimeEventEnvelope } from '../api'

export function shouldApplyRuntimeSnapshot(currentTime: string, incomingTime: string): boolean {
  const current = Date.parse(currentTime)
  const incoming = Date.parse(incomingTime)
  return !Number.isFinite(current) || (Number.isFinite(incoming) && incoming >= current)
}

export function createRuntimeEventGuard(now: () => number = Date.now, maxDelayMs = 15000) {
  const connectionStarted = now()
  let initialSequence = -1
  const sequences = new Map<string, number>()
  let clockOffset = 0
  let initialized = false
  return {
    reset(serverTime: string, snapshotSequence: number) {
      const time = Date.parse(serverTime)
      // Anchor the first hello to connection creation, so a hello buffered while
      // the browser is suspended cannot disguise the whole backlog as clock skew.
      if (!initialized) clockOffset = connectionStarted - time
      initialized = Number.isFinite(time) && Number.isSafeInteger(snapshotSequence)
      initialSequence = snapshotSequence
      sequences.clear()
    },
    check(event: RuntimeEventEnvelope<unknown>): 'accept' | 'ignore' | 'resync' {
      if (!initialized) return 'ignore'
      // Heartbeats can overtake an already queued data batch; track each event type separately.
      const previousSequence = sequences.get(event.type) ?? initialSequence
      if (!Number.isSafeInteger(event.sequence) || event.sequence < previousSequence) return 'ignore'
      const time = Date.parse(event.timestamp)
      if (!Number.isFinite(time)) return 'ignore'
      // Compare elapsed time after the hello handshake; browser/server clocks may differ.
      if (now() - clockOffset - time > maxDelayMs) return 'resync'
      // Initial snapshots and coalesced chunks intentionally share a sequence.
      sequences.set(event.type, event.sequence)
      return 'accept'
    }
  }
}
