<template>
  <a-card class="model-center" v-busy="loading" size="small">
    <template #title>
      <div class="header-row">
        <div>
          <strong>{{ tx('ONNX 模型中心') }}</strong>
          <p>{{ tx('统一管理模型版本、张量结构、发布状态和虚拟标签测试。已发布版本不可覆盖。') }}</p>
        </div>
        <div class="header-actions">
          <a-button @click="load">{{ tx('刷新') }}</a-button>
          <a-button v-if="canEdit" type="primary" @click="openModelDialog()">{{ tx('新建模型') }}</a-button>
        </div>
      </div>
    </template>
    <a-row :gutter="16">
      <a-col :span="8">
        <GatewayTable
          resize-key="components.models.ModelCenterView.vue.0"
          :data-source="models"
          :custom-row="(record: any) => ({ onClick: () => selectModel(record) })"
          :row-class-name="(record: any) => record.id === selected?.id ? 'is-current-model' : ''"
          row-key="id"
          size="small"
          :pagination="false"
          :columns="
            localizeColumns([
              { key: 'column-0', title: tx('模型'), dataIndex: 'name', width: '130' },
              { key: 'column-1', title: tx('发布'), width: '82' },
              { key: 'column-2', title: '', width: '62' }
            ])
          "
          :scroll="{ x: 274, y: NaN }"
        >
          <template #bodyCell="tableCell">
            <template v-if="tableCell.column.key === 'column-1'">
              <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
                <a-tag :color="tagColor(scope.row.publishedVersion ? 'success' : 'info')">
                  {{ scope.row.publishedVersion ? `v${scope.row.publishedVersion}` : tx('未发布') }}
                </a-tag>
              </template>
            </template>
            <template v-if="tableCell.column.key === 'column-2'">
              <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
                <a-button v-if="canEdit" @click.stop="openModelDialog(scope.row)" type="link">
                  {{ tx('编辑') }}
                </a-button>
              </template>
            </template>
          </template>
        </GatewayTable>
      </a-col>
      <a-col :span="16">
        <a-empty v-if="!selected" :description="tx('选择一个模型查看版本和测试')" />
        <a-tabs v-else v-model:active-key="activeTab">
          <a-tab-pane :tab="tx('版本')" key="versions">
            <div class="model-summary">
              <div>
                <b>{{ selected.name }}</b>
                <span>{{ tx(purposeLabel(selected.purpose)) }}</span>
              </div>
              <p>{{ selected.description || tx('暂无说明') }}</p>
            </div>
            <GatewayTable
              resize-key="components.models.ModelCenterView.vue.1"
              :data-source="selected.versions"
              max-height="430"
              :row-key="(_row: any, index: any) => index"
              size="small"
              :pagination="false"
              :columns="
                localizeColumns([
                  { key: 'column-0', title: tx('版本'), width: '70' },
                  {
                    key: 'column-1',
                    title: tx('状态'),
                    dataIndex: 'status',
                    customRender: ({ text }: any) => statusText(text),
                    width: '90'
                  },
                  { key: 'column-2', title: tx('输入/输出'), width: '170' },
                  { key: 'column-3', title: tx('大小'), width: '90' },
                  { key: 'column-4', title: tx('操作'), width: '195', fixed: 'right' }
                ])
              "
              :scroll="{ x: 615 }"
            >
              <template #bodyCell="tableCell">
                <template v-if="tableCell.column.key === 'column-0'">
                  <template
                    v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]"
                    :key="tableCell.index"
                  >
                    v{{ scope.row.version }}
                  </template>
                </template>
                <template v-if="tableCell.column.key === 'column-2'">
                  <template
                    v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]"
                    :key="tableCell.index"
                  >
                    {{ tensorSummary(scope.row) }}
                  </template>
                </template>
                <template v-if="tableCell.column.key === 'column-3'">
                  <template
                    v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]"
                    :key="tableCell.index"
                  >
                    {{ formatBytes(scope.row.fileSize) }}
                  </template>
                </template>
                <template v-if="tableCell.column.key === 'column-4'">
                  <template
                    v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]"
                    :key="tableCell.index"
                  >
                    <a-button @click="prepareTest(scope.row)" type="link">{{ tx('测试') }}</a-button>
                    <a-button
                      v-if="canPublish && scope.row.status === 'Draft'"
                      @click="publishVersion(scope.row)"
                      type="link"
                    >
                      {{ tx('发布') }}
                    </a-button>
                    <a-button
                      v-if="canEdit && scope.row.status === 'Draft'"
                      @click="removeVersion(scope.row)"
                      type="link"
                      danger
                    >
                      {{ tx('删除') }}
                    </a-button>
                  </template>
                </template>
              </template>
            </GatewayTable>
            <div v-if="canUpload" class="upload-row">
              <input ref="fileInput" type="file" accept=".onnx" @change="pickFile" />
              <gateway-input v-model="uploadNotes" :placeholder="tx('版本说明（可选）')" />
              <a-button type="primary" :disabled="!uploadFile" :loading="uploading" @click="uploadVersion">
                {{ tx('上传并检查') }}
              </a-button>
            </div>
          </a-tab-pane>
          <a-tab-pane :tab="tx('模型测试')" key="test">
            <a-alert type="info" :closable="false" :message="tx('测试只读取实时缓存，不写标签、不触发规则动作。')" />
            <div class="test-toolbar">
              <gateway-select v-model="testVersion" :placeholder="tx('选择版本')" @change="resetFeatures">
                <a-select-option
                  v-for="item in selected.versions"
                  :key="item.version"
                  :label="tx(`v${item.version} · ${item.status}`)"
                  :value="item.version"
                >
                  {{ tx(statusText(`v${item.version} · ${item.status}`)) }}
                </a-select-option>
              </gateway-select>
              <gateway-number-input v-model="outputIndex" :min="0" :controls="false" :placeholder="tx('输出序号')" />
              <gateway-number-input v-model="timeoutMs" :min="10" :max="30000" :controls="false" />
            </div>
            <GatewayTable
              resize-key="components.models.ModelCenterView.vue.2"
              :data-source="features"
              max-height="340"
              :row-key="(_row: any, index: any) => index"
              size="small"
              :pagination="false"
              :columns="
                localizeColumns([
                  { key: 'column-0', title: tx('特征'), width: '80' },
                  { key: 'column-1', title: tx('实时标签'), width: '220' },
                  { key: 'column-2', title: tx('测试值'), width: '130' },
                  { key: 'column-3', title: '', width: '65' }
                ])
              "
              :scroll="{ x: 495 }"
            >
              <template #bodyCell="tableCell">
                <template v-if="tableCell.column.key === 'column-0'">
                  <template
                    v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]"
                    :key="tableCell.index"
                  >
                    #{{ scope.$index + 1 }}
                  </template>
                </template>
                <template v-if="tableCell.column.key === 'column-1'">
                  <template
                    v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]"
                    :key="tableCell.index"
                  >
                    <gateway-select
                      v-model="scope.row.tagPath"
                      searchable
                      allow-clear
                      :placeholder="tx('可选：从实时标签取值')"
                      @change="fillLiveValue(scope.row)"
                    >
                      <a-select-option
                        v-for="tag in tagOptions"
                        :key="tag.value"
                        :label="tx(tag.label)"
                        :value="tag.value"
                      >
                        {{ tag.label }}
                      </a-select-option>
                    </gateway-select>
                  </template>
                </template>
                <template v-if="tableCell.column.key === 'column-2'">
                  <template
                    v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]"
                    :key="tableCell.index"
                  >
                    <gateway-number-input v-model="scope.row.value" :controls="false" />
                  </template>
                </template>
                <template v-if="tableCell.column.key === 'column-3'">
                  <template
                    v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]"
                    :key="tableCell.index"
                  >
                    <a-button @click="features.splice(scope.$index, 1)" type="link" danger>{{ tx('删除') }}</a-button>
                  </template>
                </template>
              </template>
            </GatewayTable>
            <div class="test-actions">
              <a-button @click="features.push({ value: 0, tagPath: '' })">{{ tx('增加特征') }}</a-button>
              <a-button v-if="canTest" type="primary" :loading="testing" @click="runTest">
                {{ tx('执行测试') }}
              </a-button>
            </div>
            <a-descriptions v-if="testResult" :column="2" bordered class="test-result">
              <a-descriptions-item :label="tx('状态')">
                <a-tag :color="tagColor(testResult.success ? 'success' : 'danger')">
                  {{ testResult.success ? tx('成功') : tx('失败') }}
                </a-tag>
              </a-descriptions-item>
              <a-descriptions-item :label="tx('耗时')">{{ testResult.durationMilliseconds }} ms</a-descriptions-item>
              <a-descriptions-item :label="tx('选中输出')">{{ testResult.score }}</a-descriptions-item>
              <a-descriptions-item :label="tx('全部输出')">
                {{ testResult.outputs?.join(', ') || '-' }}
              </a-descriptions-item>
              <a-descriptions-item v-if="testResult.errorMessage" :label="tx('错误')" :span="2">
                {{ testResult.errorMessage }}
              </a-descriptions-item>
            </a-descriptions>
          </a-tab-pane>
          <a-tab-pane :tab="tx('运行状态')" key="runtime">
            <a-descriptions :column="2" bordered>
              <a-descriptions-item :label="tx('执行总数')">{{ runtime.totalRuns }}</a-descriptions-item>
              <a-descriptions-item :label="tx('成功/失败')">
                {{ runtime.successfulRuns }} / {{ runtime.failedRuns }}
              </a-descriptions-item>
              <a-descriptions-item :label="tx('平均耗时')">{{ averageDuration }} ms</a-descriptions-item>
              <a-descriptions-item :label="tx('最后执行')">
                {{ runtime.lastRunUtc ? formatDateTime(runtime.lastRunUtc) : '-' }}
              </a-descriptions-item>
              <a-descriptions-item :label="tx('最后错误')" :span="2">
                {{ tx(runtime.lastError || '-') }}
              </a-descriptions-item>
            </a-descriptions>
          </a-tab-pane>
        </a-tabs>
      </a-col>
    </a-row>
  </a-card>
  <a-modal v-model:open="modelDialog" :title="tx('模型信息')" width="520px" :footer="null" centered>
    <a-form :label-col="{ style: { width: '90px' } }" :wrapper-col="{ style: { flex: 1 } }" label-align="left">
      <a-form-item :label="tx('模型名称')"><gateway-input v-model="modelForm.name" /></a-form-item>
      <a-form-item :label="tx('用途')">
        <gateway-select v-model="modelForm.purpose">
          <a-select-option :label="tx('设备异常')" value="DeviceAnomaly">{{ tx('设备异常') }}</a-select-option>
          <a-select-option :label="tx('质量预测')" value="QualityPrediction">{{ tx('质量预测') }}</a-select-option>
          <a-select-option :label="tx('通用推理')" value="General">{{ tx('通用推理') }}</a-select-option>
        </gateway-select>
      </a-form-item>
      <a-form-item :label="tx('说明')">
        <gateway-input v-model="modelForm.description" type="textarea" :rows="3" />
      </a-form-item>
    </a-form>
    <template #footer>
      <a-button @click="modelDialog = false">{{ tx('取消') }}</a-button>
      <a-button type="primary" :loading="saving" @click="persistModel">{{ tx('保存') }}</a-button>
    </template>
  </a-modal>
</template>

<script setup lang="ts">
import { formatMessage } from '../../i18n'
import { notify } from '../../utils/ui'
import { tx, localizeColumns } from '../../i18n'
import { statusText } from '../../utils/display'
import { tagColor, confirmAction } from '../../utils/ui'
import { computed, onMounted, ref } from 'vue'

import type { ProjectConfig, TagValueSnapshot } from '../../api'
import {
  deleteModelVersion,
  loadModelRuntime,
  loadModels,
  publishModelVersion,
  saveModel,
  testModel,
  uploadModelVersion,
  type OnnxModelDefinition,
  type OnnxModelRuntimeStats,
  type OnnxModelTestResult,
  type OnnxModelVersion
} from '../../modelApi'
import { formatDateTime } from '../../utils/format'
import { PERMISSIONS, usePermissions } from '../../utils/permissions'

const props = defineProps<{ project: ProjectConfig | null; runtimeTags: TagValueSnapshot[] }>()
const { hasPermission } = usePermissions()
const loading = ref(false),
  uploading = ref(false),
  testing = ref(false),
  saving = ref(false)
const models = ref<OnnxModelDefinition[]>([])
const selected = ref<OnnxModelDefinition | null>(null)
const activeTab = ref('versions')
const runtime = ref<OnnxModelRuntimeStats>({
  totalRuns: 0,
  successfulRuns: 0,
  failedRuns: 0,
  totalDurationMilliseconds: 0,
  lastError: ''
})
const modelDialog = ref(false)
const modelForm = ref({ id: '', name: '', purpose: 'DeviceAnomaly', description: '' })
const uploadFile = ref<File | null>(null),
  uploadNotes = ref(''),
  fileInput = ref<HTMLInputElement>()
const testVersion = ref(0),
  outputIndex = ref(0),
  timeoutMs = ref(1000)
const features = ref<Array<{ value: number; tagPath: string }>>([{ value: 0, tagPath: '' }])
const testResult = ref<OnnxModelTestResult | null>(null)

const canEdit = computed(() => hasPermission(PERMISSIONS.modelsEdit))
const canUpload = computed(() => hasPermission(PERMISSIONS.modelsUpload))
const canPublish = computed(() => hasPermission(PERMISSIONS.modelsPublish))
const canTest = computed(() => hasPermission(PERMISSIONS.modelsTest))
const averageDuration = computed(() =>
  runtime.value.totalRuns ? Math.round(runtime.value.totalDurationMilliseconds / runtime.value.totalRuns) : 0
)
const tagOptions = computed(() => {
  const channelNames = new Map((props.project?.channels ?? []).map((item) => [item.id, item.name || item.id]))
  const result: Array<{ value: string; label: string }> = []
  for (const device of props.project?.devices ?? []) {
    for (const tag of device.tags ?? [])
      result.push({
        value: `${device.channelId}/${device.id}//${tag.id}`,
        label: formatMessage('{0}-{1}-【设备直属】-{2}', [
          channelNames.get(device.channelId) || device.channelId,
          device.name,
          tag.name
        ])
      })
    for (const group of device.groups ?? [])
      for (const tag of group.tags ?? [])
        result.push({
          value: `${device.channelId}/${device.id}/${group.id}/${tag.id}`,
          label: `${channelNames.get(device.channelId) || device.channelId}-${device.name}-【${group.name}】-${tag.name}`
        })
  }
  return result
})

onMounted(load)

async function load() {
  loading.value = true
  try {
    const [catalog, stats] = await Promise.all([loadModels(), loadModelRuntime()])
    models.value = catalog.data
    runtime.value = stats.data
    if (selected.value) selected.value = models.value.find((item) => item.id === selected.value?.id) ?? null
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '模型中心加载失败')
  } finally {
    loading.value = false
  }
}
function selectModel(model: OnnxModelDefinition | null) {
  selected.value = model
  if (model) {
    testVersion.value = model.publishedVersion || model.versions[0]?.version || 0
    resetFeatures()
  }
}
function openModelDialog(model?: OnnxModelDefinition) {
  modelForm.value = model
    ? { id: model.id, name: model.name, purpose: model.purpose, description: model.description }
    : { id: '', name: '', purpose: 'DeviceAnomaly', description: '' }
  modelDialog.value = true
}
async function persistModel() {
  if (!modelForm.value.name.trim()) return notify.warning('请输入模型名称')
  saving.value = true
  try {
    await saveModel(modelForm.value)
    modelDialog.value = false
    await load()
    notify.success('模型信息已保存')
  } catch (e) {
    notify.error(e instanceof Error ? e.message : '保存失败')
  } finally {
    saving.value = false
  }
}
function pickFile(event: Event) {
  uploadFile.value = (event.target as HTMLInputElement).files?.[0] ?? null
}
async function uploadVersion() {
  if (!selected.value || !uploadFile.value) return
  uploading.value = true
  try {
    await uploadModelVersion(selected.value.id, uploadFile.value, uploadNotes.value)
    uploadFile.value = null
    uploadNotes.value = ''
    if (fileInput.value) fileInput.value.value = ''
    await load()
    notify.success('模型已上传并通过结构检查')
  } catch (e) {
    notify.error(e instanceof Error ? e.message : '上传失败')
  } finally {
    uploading.value = false
  }
}
async function publishVersion(version: OnnxModelVersion) {
  if (!selected.value) return
  await confirmAction(formatMessage('确认发布 v{0}？发布后该版本不可删除或覆盖。', [version.version]), '发布模型')
  try {
    await publishModelVersion(selected.value.id, version.version)
    await load()
    notify.success('模型版本已发布')
  } catch (e) {
    notify.error(e instanceof Error ? e.message : '发布失败')
  }
}
async function removeVersion(version: OnnxModelVersion) {
  if (!selected.value) return
  await confirmAction(formatMessage('删除草稿 v{0}？', [version.version]), '删除模型版本', { type: 'warning' })
  try {
    await deleteModelVersion(selected.value.id, version.version)
    await load()
  } catch (e) {
    notify.error(e instanceof Error ? e.message : '删除失败')
  }
}
function prepareTest(version: OnnxModelVersion) {
  testVersion.value = version.version
  activeTab.value = 'test'
  resetFeatures()
}
function resetFeatures() {
  const version = selected.value?.versions.find((item) => item.version === testVersion.value)
  const dims = version?.inputs[0]?.dimensions ?? [1]
  let count = dims.length > 1 ? dims.slice(1).reduce((a, b) => a * Math.max(1, b), 1) : Math.max(1, dims[0] || 1)
  count = Math.min(256, Math.max(1, count))
  features.value = Array.from({ length: count }, () => ({ value: 0, tagPath: '' }))
  testResult.value = null
}
function fillLiveValue(row: { value: number; tagPath: string }) {
  if (!row.tagPath) return
  const [channelId, deviceId, groupId, tagId] = row.tagPath.split('/')
  const snapshot = props.runtimeTags.find(
    (item) =>
      item.channelId === channelId &&
      item.deviceId === deviceId &&
      (item.groupId || '') === groupId &&
      item.tagId === tagId
  )
  const value = Number(snapshot?.valueText)
  if (Number.isFinite(value)) row.value = value
  else notify.warning('该标签当前没有可用数值')
}
async function runTest() {
  if (!selected.value || !testVersion.value || !features.value.length) return
  testing.value = true
  try {
    const result = await testModel(selected.value.id, {
      version: testVersion.value,
      outputIndex: outputIndex.value,
      features: features.value.map((item) => Number(item.value)),
      timeoutMilliseconds: timeoutMs.value
    })
    testResult.value = result.data
    await loadModelRuntime().then((item) => (runtime.value = item.data))
  } catch (e) {
    notify.error(e instanceof Error ? e.message : '测试失败')
  } finally {
    testing.value = false
  }
}
function tensorSummary(version: OnnxModelVersion) {
  return `${version.inputs.map((i) => `${i.name}[${i.dimensions.join('×')}]`).join(', ')} → ${version.outputs.map((i) => i.name).join(', ')}`
}
function formatBytes(value: number) {
  return value >= 1024 * 1024 ? `${(value / 1024 / 1024).toFixed(1)} MB` : `${Math.ceil(value / 1024)} KB`
}
function purposeLabel(value: string) {
  return (
    ({ DeviceAnomaly: '设备异常', QualityPrediction: '质量预测', General: '通用推理' } as Record<string, string>)[
      value
    ] || value
  )
}
</script>

<style scoped>
.model-center {
  min-height: calc(100vh - 124px);
}
.header-row,
.header-actions,
.test-toolbar,
.test-actions,
.upload-row {
  display: flex;
  align-items: center;
  gap: 12px;
}
.header-row {
  justify-content: space-between;
}
.header-row p,
.model-summary p {
  margin: 5px 0 0;
  color: var(--ui-text-color-secondary);
  font-size: calc(13px + 1pt);
}
.model-summary {
  margin-bottom: 14px;
}
.model-summary div {
  display: flex;
  gap: 12px;
  align-items: center;
}
.model-summary span {
  color: var(--ui-text-color-secondary);
}
.upload-row {
  margin-top: 16px;
  padding: 14px;
  border: 1px dashed var(--ui-border-color);
  border-radius: 8px;
}
.upload-row input {
  width: 220px;
}
.test-toolbar {
  margin: 14px 0;
}
.test-toolbar .ant-select {
  width: 180px;
}
.test-actions {
  justify-content: flex-end;
  margin-top: 14px;
}
.test-result {
  margin-top: 16px;
}
</style>
