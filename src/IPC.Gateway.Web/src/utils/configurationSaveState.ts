import { computed, ref } from 'vue'

const pending = ref(0)
const silentPending = ref(0)
export const configurationSaving = computed(() => pending.value > 0)
export const configurationSaveDialogVisible = computed(() => configurationSaving.value && silentPending.value === 0)
export function beginConfigurationSave(showDialog = true) {
  pending.value++
  if (!showDialog) silentPending.value++
}
export function endConfigurationSave(showDialog = true) {
  pending.value = Math.max(0, pending.value - 1)
  if (!showDialog) silentPending.value = Math.max(0, silentPending.value - 1)
}
