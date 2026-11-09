import { reactive, readonly } from 'vue'
import type { FileUploadProgress } from './multipartUpload'

const state = reactive({ active: false, fileName: '', percent: 0, known: false, phase: 'uploading' as 'uploading' | 'verifying' })
export const upgradeUploadState = readonly(state)
export function beginUpgradeUpload(fileName: string) {
  if (state.active) throw new Error('已有升级包正在上传，请稍候。')
  Object.assign(state, { active: true, fileName, percent: 0, known: false, phase: 'uploading' })
}
export function updateUpgradeUpload(progress: FileUploadProgress) {
  if (!state.active) return
  state.known = progress.lengthComputable && progress.total > 0
  if (state.known) state.percent = Math.min(100, Math.max(0, Math.floor(progress.loaded * 100 / progress.total)))
  if (state.percent === 100) markUpgradeTransferred()
}
export function markUpgradeTransferred() {
  if (!state.active) return
  Object.assign(state, { phase: 'verifying', percent: 100, known: true })
}
export function finishUpgradeUpload() { state.active = false }
