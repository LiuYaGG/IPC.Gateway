<template>
  <el-card class="model-center" shadow="never" v-loading="loading">
    <template #header>
      <div class="header-row">
        <div><strong>ONNX 模型中心</strong><p>统一管理模型版本、张量结构、发布状态和虚拟标签测试。已发布版本不可覆盖。</p></div>
        <div class="header-actions">
          <el-button @click="load">刷新</el-button>
          <el-button v-if="canEdit" type="primary" @click="openModelDialog()">新建模型</el-button>
        </div>
      </div>
    </template>

    <el-row :gutter="16">
      <el-col :span="8">
        <el-table :data="models" highlight-current-row height="calc(100vh - 235px)" @current-change="selectModel">
          <el-table-column prop="name" label="模型" min-width="130" />
          <el-table-column label="发布" width="82">
            <template #default="scope"><el-tag :type="scope.row.publishedVersion ? 'success' : 'info'">{{ scope.row.publishedVersion ? `v${scope.row.publishedVersion}` : '未发布' }}</el-tag></template>
          </el-table-column>
          <el-table-column width="62"><template #default="scope"><el-button v-if="canEdit" text @click.stop="openModelDialog(scope.row)">编辑</el-button></template></el-table-column>
        </el-table>
      </el-col>
      <el-col :span="16">
        <el-empty v-if="!selected" description="选择一个模型查看版本和测试" />
        <el-tabs v-else v-model="activeTab">
          <el-tab-pane label="版本" name="versions">
            <div class="model-summary">
              <div><b>{{ selected.name }}</b><span>{{ purposeLabel(selected.purpose) }}</span></div>
              <p>{{ selected.description || '暂无说明' }}</p>
            </div>
            <el-table :data="selected.versions" max-height="430">
              <el-table-column label="版本" width="70"><template #default="scope">v{{ scope.row.version }}</template></el-table-column>
              <el-table-column prop="status" label="状态" width="90" />
              <el-table-column label="输入/输出" min-width="170"><template #default="scope">{{ tensorSummary(scope.row) }}</template></el-table-column>
              <el-table-column label="大小" width="90"><template #default="scope">{{ formatBytes(scope.row.fileSize) }}</template></el-table-column>
              <el-table-column label="操作" width="195" fixed="right">
                <template #default="scope">
                  <el-button text @click="prepareTest(scope.row)">测试</el-button>
                  <el-button v-if="canPublish && scope.row.status === 'Draft'" text type="success" @click="publishVersion(scope.row)">发布</el-button>
                  <el-button v-if="canEdit && scope.row.status === 'Draft'" text type="danger" @click="removeVersion(scope.row)">删除</el-button>
                </template>
              </el-table-column>
            </el-table>
            <div v-if="canUpload" class="upload-row">
              <input ref="fileInput" type="file" accept=".onnx" @change="pickFile" />
              <el-input v-model="uploadNotes" placeholder="版本说明（可选）" />
              <el-button type="primary" :disabled="!uploadFile" :loading="uploading" @click="uploadVersion">上传并检查</el-button>
            </div>
          </el-tab-pane>

          <el-tab-pane label="模型测试" name="test">
            <el-alert type="info" :closable="false" title="测试只读取实时缓存，不写标签、不触发规则动作。" />
            <div class="test-toolbar">
              <el-select v-model="testVersion" placeholder="选择版本" @change="resetFeatures">
                <el-option v-for="item in selected.versions" :key="item.version" :label="`v${item.version} · ${item.status}`" :value="item.version" />
              </el-select>
              <el-input-number v-model="outputIndex" :min="0" :controls="false" placeholder="输出序号" />
              <el-input-number v-model="timeoutMs" :min="10" :max="30000" :controls="false" />
            </div>
            <el-table :data="features" max-height="340">
              <el-table-column label="特征" width="80"><template #default="scope">#{{ scope.$index + 1 }}</template></el-table-column>
              <el-table-column label="实时标签" min-width="220">
                <template #default="scope">
                  <el-select v-model="scope.row.tagPath" filterable clearable placeholder="可选：从实时标签取值" @change="fillLiveValue(scope.row)">
                    <el-option v-for="tag in tagOptions" :key="tag.value" :label="tag.label" :value="tag.value" />
                  </el-select>
                </template>
              </el-table-column>
              <el-table-column label="测试值" min-width="130"><template #default="scope"><el-input-number v-model="scope.row.value" :controls="false" /></template></el-table-column>
              <el-table-column width="65"><template #default="scope"><el-button text type="danger" @click="features.splice(scope.$index, 1)">删除</el-button></template></el-table-column>
            </el-table>
            <div class="test-actions">
              <el-button @click="features.push({ value: 0, tagPath: '' })">增加特征</el-button>
              <el-button v-if="canTest" type="primary" :loading="testing" @click="runTest">执行测试</el-button>
            </div>
            <el-descriptions v-if="testResult" :column="2" border class="test-result">
              <el-descriptions-item label="状态"><el-tag :type="testResult.success ? 'success' : 'danger'">{{ testResult.success ? '成功' : '失败' }}</el-tag></el-descriptions-item>
              <el-descriptions-item label="耗时">{{ testResult.durationMilliseconds }} ms</el-descriptions-item>
              <el-descriptions-item label="选中输出">{{ testResult.score }}</el-descriptions-item>
              <el-descriptions-item label="全部输出">{{ testResult.outputs?.join(', ') || '-' }}</el-descriptions-item>
              <el-descriptions-item v-if="testResult.errorMessage" label="错误" :span="2">{{ testResult.errorMessage }}</el-descriptions-item>
            </el-descriptions>
          </el-tab-pane>

          <el-tab-pane label="运行状态" name="runtime">
            <el-descriptions :column="2" border>
              <el-descriptions-item label="执行总数">{{ runtime.totalRuns }}</el-descriptions-item>
              <el-descriptions-item label="成功/失败">{{ runtime.successfulRuns }} / {{ runtime.failedRuns }}</el-descriptions-item>
              <el-descriptions-item label="平均耗时">{{ averageDuration }} ms</el-descriptions-item>
              <el-descriptions-item label="最后执行">{{ runtime.lastRunUtc ? formatDateTime(runtime.lastRunUtc) : '-' }}</el-descriptions-item>
              <el-descriptions-item label="最后错误" :span="2">{{ runtime.lastError || '-' }}</el-descriptions-item>
            </el-descriptions>
          </el-tab-pane>
        </el-tabs>
      </el-col>
    </el-row>
  </el-card>

  <el-dialog v-model="modelDialog" title="模型信息" width="520px" align-center>
    <el-form label-width="90px">
      <el-form-item label="模型名称"><el-input v-model="modelForm.name" /></el-form-item>
      <el-form-item label="用途"><el-select v-model="modelForm.purpose"><el-option label="设备异常" value="DeviceAnomaly" /><el-option label="质量预测" value="QualityPrediction" /><el-option label="通用推理" value="General" /></el-select></el-form-item>
      <el-form-item label="说明"><el-input v-model="modelForm.description" type="textarea" :rows="3" /></el-form-item>
    </el-form>
    <template #footer><el-button @click="modelDialog=false">取消</el-button><el-button type="primary" :loading="saving" @click="persistModel">保存</el-button></template>
  </el-dialog>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { ProjectConfig, TagValueSnapshot } from '../../api'
import { deleteModelVersion, loadModelRuntime, loadModels, publishModelVersion, saveModel, testModel, uploadModelVersion, type OnnxModelDefinition, type OnnxModelRuntimeStats, type OnnxModelTestResult, type OnnxModelVersion } from '../../modelApi'
import { formatDateTime } from '../../utils/format'
import { PERMISSIONS, usePermissions } from '../../utils/permissions'

const props = defineProps<{ project: ProjectConfig | null; runtimeTags: TagValueSnapshot[] }>()
const { hasPermission } = usePermissions()
const loading = ref(false), uploading = ref(false), testing = ref(false), saving = ref(false)
const models = ref<OnnxModelDefinition[]>([])
const selected = ref<OnnxModelDefinition | null>(null)
const activeTab = ref('versions')
const runtime = ref<OnnxModelRuntimeStats>({ totalRuns: 0, successfulRuns: 0, failedRuns: 0, totalDurationMilliseconds: 0, lastError: '' })
const modelDialog = ref(false)
const modelForm = ref({ id: '', name: '', purpose: 'DeviceAnomaly', description: '' })
const uploadFile = ref<File | null>(null), uploadNotes = ref(''), fileInput = ref<HTMLInputElement>()
const testVersion = ref(0), outputIndex = ref(0), timeoutMs = ref(1000)
const features = ref<Array<{ value: number; tagPath: string }>>([{ value: 0, tagPath: '' }])
const testResult = ref<OnnxModelTestResult | null>(null)

const canEdit = computed(() => hasPermission(PERMISSIONS.modelsEdit))
const canUpload = computed(() => hasPermission(PERMISSIONS.modelsUpload))
const canPublish = computed(() => hasPermission(PERMISSIONS.modelsPublish))
const canTest = computed(() => hasPermission(PERMISSIONS.modelsTest))
const averageDuration = computed(() => runtime.value.totalRuns ? Math.round(runtime.value.totalDurationMilliseconds / runtime.value.totalRuns) : 0)
const tagOptions = computed(() => {
  const channelNames = new Map((props.project?.channels ?? []).map(item => [item.id, item.name || item.id]))
  const result: Array<{ value: string; label: string }> = []
  for (const device of props.project?.devices ?? []) {
    for (const tag of device.tags ?? []) result.push({ value: `${device.channelId}/${device.id}//${tag.id}`, label: `${channelNames.get(device.channelId) || device.channelId}-${device.name}-【设备直属】-${tag.name}` })
    for (const group of device.groups ?? []) for (const tag of group.tags ?? []) result.push({ value: `${device.channelId}/${device.id}/${group.id}/${tag.id}`, label: `${channelNames.get(device.channelId) || device.channelId}-${device.name}-【${group.name}】-${tag.name}` })
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
    if (selected.value) selected.value = models.value.find(item => item.id === selected.value?.id) ?? null
  } catch (error) { ElMessage.error(error instanceof Error ? error.message : '模型中心加载失败') }
  finally { loading.value = false }
}
function selectModel(model: OnnxModelDefinition | null) { selected.value = model; if (model) { testVersion.value = model.publishedVersion || model.versions[0]?.version || 0; resetFeatures() } }
function openModelDialog(model?: OnnxModelDefinition) { modelForm.value = model ? { id: model.id, name: model.name, purpose: model.purpose, description: model.description } : { id: '', name: '', purpose: 'DeviceAnomaly', description: '' }; modelDialog.value = true }
async function persistModel() { if (!modelForm.value.name.trim()) return ElMessage.warning('请输入模型名称'); saving.value = true; try { await saveModel(modelForm.value); modelDialog.value = false; await load(); ElMessage.success('模型信息已保存') } catch (e) { ElMessage.error(e instanceof Error ? e.message : '保存失败') } finally { saving.value = false } }
function pickFile(event: Event) { uploadFile.value = (event.target as HTMLInputElement).files?.[0] ?? null }
async function uploadVersion() { if (!selected.value || !uploadFile.value) return; uploading.value = true; try { await uploadModelVersion(selected.value.id, uploadFile.value, uploadNotes.value); uploadFile.value = null; uploadNotes.value = ''; if (fileInput.value) fileInput.value.value = ''; await load(); ElMessage.success('模型已上传并通过结构检查') } catch (e) { ElMessage.error(e instanceof Error ? e.message : '上传失败') } finally { uploading.value = false } }
async function publishVersion(version: OnnxModelVersion) { if (!selected.value) return; await ElMessageBox.confirm(`确认发布 v${version.version}？发布后该版本不可删除或覆盖。`, '发布模型'); try { await publishModelVersion(selected.value.id, version.version); await load(); ElMessage.success('模型版本已发布') } catch (e) { ElMessage.error(e instanceof Error ? e.message : '发布失败') } }
async function removeVersion(version: OnnxModelVersion) { if (!selected.value) return; await ElMessageBox.confirm(`删除草稿 v${version.version}？`, '删除模型版本', { type: 'warning' }); try { await deleteModelVersion(selected.value.id, version.version); await load() } catch (e) { ElMessage.error(e instanceof Error ? e.message : '删除失败') } }
function prepareTest(version: OnnxModelVersion) { testVersion.value = version.version; activeTab.value = 'test'; resetFeatures() }
function resetFeatures() { const version = selected.value?.versions.find(item => item.version === testVersion.value); const dims = version?.inputs[0]?.dimensions ?? [1]; let count = dims.length > 1 ? dims.slice(1).reduce((a, b) => a * Math.max(1, b), 1) : Math.max(1, dims[0] || 1); count = Math.min(256, Math.max(1, count)); features.value = Array.from({ length: count }, () => ({ value: 0, tagPath: '' })); testResult.value = null }
function fillLiveValue(row: { value: number; tagPath: string }) { if (!row.tagPath) return; const [channelId, deviceId, groupId, tagId] = row.tagPath.split('/'); const snapshot = props.runtimeTags.find(item => item.channelId === channelId && item.deviceId === deviceId && (item.groupId || '') === groupId && item.tagId === tagId); const value = Number(snapshot?.valueText); if (Number.isFinite(value)) row.value = value; else ElMessage.warning('该标签当前没有可用数值') }
async function runTest() { if (!selected.value || !testVersion.value || !features.value.length) return; testing.value = true; try { const result = await testModel(selected.value.id, { version: testVersion.value, outputIndex: outputIndex.value, features: features.value.map(item => Number(item.value)), timeoutMilliseconds: timeoutMs.value }); testResult.value = result.data; await loadModelRuntime().then(item => runtime.value = item.data) } catch (e) { ElMessage.error(e instanceof Error ? e.message : '测试失败') } finally { testing.value = false } }
function tensorSummary(version: OnnxModelVersion) { return `${version.inputs.map(i => `${i.name}[${i.dimensions.join('×')}]`).join(', ')} → ${version.outputs.map(i => i.name).join(', ')}` }
function formatBytes(value: number) { return value >= 1024 * 1024 ? `${(value / 1024 / 1024).toFixed(1)} MB` : `${Math.ceil(value / 1024)} KB` }
function purposeLabel(value: string) { return ({ DeviceAnomaly: '设备异常', QualityPrediction: '质量预测', General: '通用推理' } as Record<string, string>)[value] || value }
</script>

<style scoped>
.model-center { min-height: calc(100vh - 124px); }
.header-row,.header-actions,.test-toolbar,.test-actions,.upload-row { display:flex; align-items:center; gap:12px; }
.header-row { justify-content:space-between; }
.header-row p,.model-summary p { margin:5px 0 0; color:var(--el-text-color-secondary); font-size:13px; }
.model-summary { margin-bottom:14px; }
.model-summary div { display:flex; gap:12px; align-items:center; }
.model-summary span { color:var(--el-text-color-secondary); }
.upload-row { margin-top:16px; padding:14px; border:1px dashed var(--el-border-color); border-radius:8px; }
.upload-row input { width:220px; }
.test-toolbar { margin:14px 0; }
.test-toolbar .el-select { width:180px; }
.test-actions { justify-content:flex-end; margin-top:14px; }
.test-result { margin-top:16px; }
</style>
