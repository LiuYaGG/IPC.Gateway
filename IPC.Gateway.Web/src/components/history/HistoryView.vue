<template>
  <section class="view-stack history-view">
    <HistoryStatusBar :config="draft" :status="status" />

    <div class="history-toolbar">
      <div>
        <h3>历史库与边缘处理配置</h3>
        <span>{{ dirty ? '有未保存修改' : '配置已同步' }}</span>
      </div>
      <div class="history-toolbar__actions">
        <el-button :disabled="saving || !dirty" @click="resetDraft">重置</el-button>
        <el-button type="primary" :loading="saving" :disabled="!canSave" @click="submit">保存配置</el-button>
      </div>
    </div>

    <HistoryBaseForm :config="draft" :disabled="!canSave" />
    <HistoryProcessingTabs :config="draft" :disabled="!canSave" />
    <HistoryStoragePane :config="draft" :disabled="!canSave" />
  </section>
</template>

<script setup lang="ts">
import { nextTick, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import type { HistoryConfig, HistoryRuntimeStatus } from '../../api'
import HistoryBaseForm from './HistoryBaseForm.vue'
import HistoryProcessingTabs from './HistoryProcessingTabs.vue'
import HistoryStoragePane from './HistoryStoragePane.vue'
import HistoryStatusBar from './HistoryStatusBar.vue'
import { cloneHistoryConfig, createDefaultHistoryConfig, normalizeHistoryConfig, parseAggregationMethods } from './historyModel'

const props = withDefaults(defineProps<{
  history: HistoryConfig | null
  status?: HistoryRuntimeStatus
  saving?: boolean
  canSave?: boolean
}>(), {
  saving: false,
  canSave: true
})

const emit = defineEmits<{
  'persist-history': [config: HistoryConfig]
}>()

const draft = reactive(createDefaultHistoryConfig())
const dirty = ref(false)
let syncing = false

watch(() => props.history, value => {
  if (!dirty.value) syncDraft(value)
}, { immediate: true, deep: true })

watch(draft, () => {
  if (!syncing) dirty.value = true
}, { deep: true })

function syncDraft(value: HistoryConfig | null) {
  syncing = true
  const next = normalizeHistoryConfig(value)
  draft.enabled = next.enabled
  draft.directory = next.directory
  draft.retentionDays = next.retentionDays
  draft.maxViewRecords = next.maxViewRecords
  draft.dataProcessing = next.dataProcessing
  draft.storage = next.storage
  dirty.value = false
  nextTick(() => {
    syncing = false
  })
}

function resetDraft() {
  syncDraft(props.history)
}

function submit() {
  if (!props.canSave) {
    ElMessage.warning('当前用户没有保存历史库配置权限')
    return
  }

  const error = validateDraft()
  if (error) {
    ElMessage.warning(error)
    return
  }

  dirty.value = false
  emit('persist-history', cloneHistoryConfig(draft))
}

function validateDraft() {
  const directory = draft.directory.trim()
  if (!directory) return '请填写历史库保存目录'
  if (draft.retentionDays < 1 || draft.retentionDays > 3650) return '保留天数必须在 1 到 3650 之间'
  if (draft.maxViewRecords < 50 || draft.maxViewRecords > 10000) return '最大查询条数必须在 50 到 10000 之间'
  if (draft.storage.hotRetentionDays < 1 || draft.storage.hotRetentionDays > 3650) return '热数据保留天数必须在 1 到 3650 之间'
  if (draft.storage.coldRetentionDays < draft.storage.hotRetentionDays || draft.storage.coldRetentionDays > 3650) return '冷数据保留天数必须大于等于热数据保留天数，且不超过 3650'
  if (draft.storage.tieringEnabled && !draft.storage.coldDirectory.trim()) return '启用冷热分层时必须填写冷数据目录'
  if (draft.storage.compressionEnabled && draft.storage.compressAfterDays < 0) return '压缩等待天数不能小于 0'
  if (draft.storage.autoCleanupEnabled && (draft.storage.cleanupIntervalHours < 1 || draft.storage.cleanupIntervalHours > 720)) return '自动清理间隔必须在 1 到 720 小时之间'
  if (draft.storage.maxStorageMegabytes < 0 || draft.storage.maxStorageMegabytes > 1048576) return '容量上限必须在 0 到 1048576 MB 之间'

  const processing = draft.dataProcessing
  if (!processing.enabled) return ''
  if (processing.compressionEnabled && processing.compressionTolerance < 0) return '压缩容差不能小于 0'
  if (processing.downsamplingEnabled && processing.downsamplingIntervalMs <= 0) return '启用降采样时必须填写大于 0 的采样间隔'
  if (processing.alignmentEnabled && processing.alignmentIntervalMs <= 0) return '启用时间对齐时必须填写大于 0 的对齐间隔'
  if (processing.fillEnabled && resolveFillIntervalMs() <= 0) return '启用补点时必须填写补点间隔，或启用对齐/降采样/聚合间隔'
  if (processing.fillMaxGapSeconds < 0 || processing.fillMaxGapSeconds > 86400) return '最大补点间隔必须在 0 到 86400 秒之间'
  if (processing.maxSyntheticPointsPerInput < 1 || processing.maxSyntheticPointsPerInput > 10000) return '单次最大生成点数必须在 1 到 10000 之间'
  if (processing.aggregationEnabled && processing.aggregationIntervalSeconds <= 0) return '启用聚合时必须填写大于 0 的聚合窗口'
  if (processing.aggregationEnabled && parseAggregationMethods(processing.aggregationMethods).length === 0) return '请至少选择一种聚合方法'
  return ''
}

function resolveFillIntervalMs() {
  const processing = draft.dataProcessing
  if (processing.fillIntervalMs > 0) return processing.fillIntervalMs
  if (processing.alignmentEnabled && processing.alignmentIntervalMs > 0) return processing.alignmentIntervalMs
  if (processing.downsamplingEnabled && processing.downsamplingIntervalMs > 0) return processing.downsamplingIntervalMs
  if (processing.aggregationEnabled && processing.aggregationIntervalSeconds > 0) return processing.aggregationIntervalSeconds * 1000
  return 0
}
</script>

<style scoped>
.history-view {
  display: grid;
  gap: 16px;
}

.history-toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  padding: 16px 18px;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  background: #ffffff;
}

.history-toolbar h3 {
  margin: 0 0 4px;
  color: #172033;
  font-size: 16px;
}

.history-toolbar span {
  color: #64748b;
  font-size: 13px;
}

.history-toolbar__actions {
  display: flex;
  flex-wrap: nowrap;
  gap: 8px;
}

@media (max-width: 720px) {
  .history-toolbar {
    align-items: stretch;
    flex-direction: column;
  }

  .history-toolbar__actions {
    justify-content: flex-end;
  }
}
</style>
