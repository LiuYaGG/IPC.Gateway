<template>
  <section class="view-stack maintenance-view">
    <section class="maintenance-hero">
      <div>
        <p class="eyebrow">Maintenance</p>
        <h3>安装升级</h3>
        <span>安装包、升级包、离线升级和版本回滚集中管理</span>
      </div>
      <div class="hero-actions">
        <el-tag :class="['maintenance-status-tag', status?.enabled ? 'maintenance-status-tag--enabled' : 'maintenance-status-tag--disabled']" :type="status?.enabled ? 'success' : 'info'" effect="light">
          {{ status?.enabled ? '已启用' : '未启用' }}
        </el-tag>
        <el-button :icon="Download" :loading="snapshotLoading" @click="downloadSupportSnapshot">售后快照</el-button>
        <el-button :icon="Refresh" :loading="loading" type="primary" @click="refresh">刷新</el-button>
      </div>
    </section>

    <section class="maintenance-metrics">
      <el-card shadow="never" class="metric-panel">
        <div class="metric-panel__icon"><el-icon><Box /></el-icon></div>
        <div>
          <small>当前版本</small>
          <strong>{{ status?.currentVersion || '-' }}</strong>
          <span>{{ status?.productId || 'IPC.Gateway' }}</span>
        </div>
      </el-card>
      <el-card shadow="never" class="metric-panel">
        <div class="metric-panel__icon"><el-icon><FolderChecked /></el-icon></div>
        <div>
          <small>升级包</small>
          <strong>{{ status?.packages.length ?? 0 }}</strong>
          <span>{{ formatBytes(totalPackageBytes) }}</span>
        </div>
      </el-card>
      <el-card shadow="never" class="metric-panel">
        <div class="metric-panel__icon"><el-icon><RefreshLeft /></el-icon></div>
        <div>
          <small>回滚点</small>
          <strong>{{ status?.rollbackPoints.length ?? 0 }}</strong>
          <span>{{ latestRollbackLabel }}</span>
        </div>
      </el-card>
      <el-card shadow="never" class="metric-panel">
        <div class="metric-panel__icon"><el-icon><Clock /></el-icon></div>
        <div>
          <small>待执行动作</small>
          <strong>{{ status?.pendingAction ? status.pendingAction.actionType : '无' }}</strong>
          <span>{{ status?.pendingAction?.status || 'Ready' }}</span>
        </div>
      </el-card>
    </section>

    <el-alert
      v-if="status?.pendingAction"
      type="warning"
      show-icon
      :closable="false"
      class="pending-alert"
      title="已有待执行的离线动作"
    >
      <template #default>
        <div class="pending-body">
          <span>{{ pendingText }}</span>
          <el-button size="small" :icon="DocumentCopy" @click="copyScriptPath">复制脚本路径</el-button>
        </div>
      </template>
    </el-alert>

    <el-card shadow="never" class="panel-card watchdog-panel">
      <template #header>
        <div class="card-header">
          <div class="detail-title">
            <span>看门狗与自恢复</span>
            <small>监控网关运行时、采集调度、MQTT、历史库、规则引擎和 OPC UA Server</small>
          </div>
          <div class="card-actions">
            <el-tag :type="watchdogStateType(watchdog?.state)" effect="light">
              {{ watchdogStateText(watchdog?.state) }}
            </el-tag>
          </div>
        </div>
      </template>

      <section class="watchdog-summary">
        <div>
          <span>检查次数</span>
          <strong>{{ watchdog?.checkCount ?? 0 }}</strong>
        </div>
        <div>
          <span>恢复成功</span>
          <strong>{{ watchdog?.recoverySuccessCount ?? 0 }}</strong>
        </div>
        <div>
          <span>恢复失败</span>
          <strong>{{ watchdog?.recoveryFailureCount ?? 0 }}</strong>
        </div>
        <div>
          <span>保护拦截</span>
          <strong>{{ watchdog?.blockedRecoveryCount ?? 0 }}</strong>
        </div>
        <div>
          <span>最近检查</span>
          <strong>{{ watchdog?.lastCheckTime ? formatDateTime(watchdog.lastCheckTime) : '-' }}</strong>
        </div>
        <div>
          <span>最近恢复</span>
          <strong>{{ watchdog?.lastRecoveryTime ? formatDateTime(watchdog.lastRecoveryTime) : '-' }}</strong>
        </div>
      </section>

      <el-alert
        v-if="watchdog?.lastIssue"
        class="watchdog-issue"
        type="warning"
        show-icon
        :closable="false"
        :title="watchdog.lastIssue"
      />

      <el-table :data="watchdog?.checks ?? []" empty-text="暂无看门狗检查数据" height="220">
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="watchdogStateType(row.state)" effect="light">{{ watchdogStateText(row.state) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="name" label="检查项" width="140" />
        <el-table-column prop="message" label="说明" min-width="260" show-overflow-tooltip />
        <el-table-column label="建议恢复" width="100">
          <template #default="{ row }">{{ row.recoveryRecommended ? '是' : '否' }}</template>
        </el-table-column>
        <el-table-column label="时间" width="170">
          <template #default="{ row }">{{ row.observedTime ? formatDateTime(row.observedTime) : '-' }}</template>
        </el-table-column>
      </el-table>

      <section class="restart-protection">
        <el-tag :type="watchdog?.restartProtection?.recoveryBlocked ? 'danger' : 'success'" effect="plain">
          恢复保护：{{ watchdog?.restartProtection?.recoveryBlocked ? '拦截中' : '正常' }}
        </el-tag>
        <el-tag :type="watchdog?.restartProtection?.hostRestartBlocked ? 'danger' : 'success'" effect="plain">
          宿主重启保护：{{ watchdog?.restartProtection?.hostRestartBlocked ? '拦截中' : '正常' }}
        </el-tag>
        <span>窗口内恢复 {{ watchdog?.restartProtection?.recentRecoveryCount ?? 0 }} 次</span>
        <span>宿主重启请求 {{ watchdog?.restartProtection?.recentHostRestartRequestCount ?? 0 }} 次</span>
      </section>
    </el-card>

    <WatchdogConfigPanel
      :config="watchdogConfig"
      :saving="savingWatchdogConfig"
      :can-save="canEditWatchdog"
      @save-watchdog-config="persistWatchdogConfig"
    />

    <section class="commercial-grid">
      <el-card shadow="never" class="panel-card">
        <template #header>
          <div class="card-header">
            <div class="detail-title">
              <span>商业授权</span>
              <small>许可证状态、版本授权和现场限制</small>
            </div>
          </div>
        </template>
        <el-descriptions :column="1" border size="small">
          <el-descriptions-item label="状态">
            <el-tag :type="license?.operational ? 'success' : 'danger'" effect="light">{{ license?.status || '-' }}</el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="版本">{{ license?.edition || '-' }}</el-descriptions-item>
          <el-descriptions-item label="客户">{{ license?.customerName || '-' }}</el-descriptions-item>
          <el-descriptions-item label="到期">{{ license?.expiresUtc ? formatDateTime(license.expiresUtc) : '-' }}</el-descriptions-item>
          <el-descriptions-item label="限制">{{ licenseLimitText }}</el-descriptions-item>
        </el-descriptions>
      </el-card>

      <el-card shadow="never" class="panel-card">
        <template #header>
          <div class="card-header">
            <div class="detail-title">
              <span>项目备份恢复</span>
              <small>导出或恢复项目、通信和历史配置</small>
            </div>
            <div class="card-actions">
              <el-button :icon="Download" :loading="backupLoading" @click="downloadProjectBackup">导出备份</el-button>
            </div>
          </div>
        </template>
        <el-upload
          drag
          accept=".json"
          :auto-upload="false"
          :show-file-list="false"
          :disabled="restoreLoading"
          :on-change="handleRestoreBackup"
        >
          <el-icon class="upload-icon"><UploadFilled /></el-icon>
          <div class="el-upload__text">拖入项目备份 JSON，或点击选择</div>
        </el-upload>
      </el-card>
    </section>

    <section class="commercial-grid">
      <el-card shadow="never" class="panel-card">
        <template #header>
          <div class="card-header">
            <div class="detail-title">
              <span>版本兼容矩阵</span>
              <small>网关、配置 schema、插件和协议能力范围</small>
            </div>
          </div>
        </template>
        <el-table :data="compatibility?.items ?? []" empty-text="暂无兼容矩阵" height="240">
          <el-table-column prop="capability" label="能力" min-width="180" show-overflow-tooltip />
          <el-table-column prop="currentVersion" label="当前版本" width="130" />
          <el-table-column prop="compatibleRange" label="兼容范围" width="150" show-overflow-tooltip />
          <el-table-column label="状态" width="110">
            <template #default="{ row }">
              <el-tag :type="row.status === 'Compatible' ? 'success' : 'danger'" effect="light">{{ row.status }}</el-tag>
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-card shadow="never" class="panel-card">
        <template #header>
          <div class="card-header">
            <div class="detail-title">
              <span>协议驱动签名</span>
              <small>内置和外部协议驱动的版本、摘要与签名状态</small>
            </div>
          </div>
        </template>
        <el-table :data="drivers" empty-text="暂无协议驱动" height="240">
          <el-table-column prop="driverId" label="驱动" min-width="160" show-overflow-tooltip />
          <el-table-column prop="protocol" label="协议" width="110" />
          <el-table-column label="签名" width="120">
            <template #default="{ row }">
              <el-tag :type="driverSignatureType(row.signatureStatus)" effect="light">{{ row.signatureStatus || '-' }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="signer" label="签名方" min-width="130" show-overflow-tooltip />
        </el-table>
      </el-card>
    </section>

    <section class="maintenance-layout">
      <el-card shadow="never" class="panel-card upload-panel">
        <template #header>
          <div class="card-header">
            <div class="detail-title">
              <span>上传升级包</span>
              <small>支持安装包和升级包 zip，上传后先校验清单和 payload</small>
            </div>
          </div>
        </template>

        <el-upload
          drag
          accept=".zip"
          :auto-upload="false"
          :show-file-list="false"
          :disabled="!canUpload || uploading"
          :on-change="handleUploadFile"
        >
          <el-icon class="upload-icon"><UploadFilled /></el-icon>
          <div class="el-upload__text">拖入升级包，或点击选择</div>
          <template #tip>
            <div class="el-upload__tip">
              {{ canUpload ? '包内需要包含清单、payload 目录和文件摘要；生产环境建议启用发布签名' : '当前用户没有上传升级包权限' }}
            </div>
          </template>
        </el-upload>
      </el-card>

      <el-card shadow="never" class="panel-card path-panel">
        <template #header>
          <div class="card-header">
            <div class="detail-title">
              <span>目录信息</span>
              <small>离线升级脚本会在维护窗口使用这些路径</small>
            </div>
          </div>
        </template>
        <el-descriptions :column="1" border size="small">
          <el-descriptions-item label="安装目录">{{ status?.installDirectory || '-' }}</el-descriptions-item>
          <el-descriptions-item label="升级目录">{{ status?.updateDirectory || '-' }}</el-descriptions-item>
          <el-descriptions-item label="离线脚本">{{ status?.offlineScriptPath || '-' }}</el-descriptions-item>
          <el-descriptions-item label="摘要策略">{{ status?.requirePackageFileDigests ? '必需' : '兼容旧包' }}</el-descriptions-item>
          <el-descriptions-item label="签名策略">
            <span class="policy-tags">
              <el-tag :type="status?.requirePackageSignature ? 'danger' : 'warning'" effect="light">
                {{ status?.requirePackageSignature ? '必需' : '可选' }}
              </el-tag>
              <el-tag :type="status?.trustedPackagePublicKeyConfigured ? 'success' : 'info'" effect="plain">
                {{ status?.trustedPackagePublicKeyConfigured ? '公钥已配置' : '公钥未配置' }}
              </el-tag>
            </span>
          </el-descriptions-item>
        </el-descriptions>
      </el-card>
    </section>

    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <div class="detail-title">
            <span>升级包列表</span>
            <small>选择包后准备离线升级，系统会自动创建回滚点</small>
          </div>
        </div>
      </template>
      <el-table v-loading="loading" :data="status?.packages ?? []" empty-text="暂无升级包" height="300">
        <el-table-column prop="packageId" label="包编号" min-width="180" show-overflow-tooltip />
        <el-table-column prop="packageType" label="类型" width="100">
          <template #default="{ row }">
            <el-tag :type="row.packageType === 'Install' ? 'success' : 'primary'" effect="light">
              {{ row.packageType === 'Install' ? '安装包' : '升级包' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="version" label="版本" width="130" />
        <el-table-column label="可信状态" width="170">
          <template #default="{ row }">
            <el-tooltip :content="trustStatusDetail(row)" placement="top">
              <div class="trust-cell">
                <el-tag :type="trustStatusType(row.trustStatus)" effect="light">
                  {{ trustStatusText(row.trustStatus) }}
                </el-tag>
                <small>{{ trustSubtext(row) }}</small>
              </div>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column label="大小" width="110">
          <template #default="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
        </el-table-column>
        <el-table-column label="上传时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.uploadedTime) }}</template>
        </el-table-column>
        <el-table-column prop="sha256" label="SHA256" min-width="220" show-overflow-tooltip />
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-button
                size="small"
                type="primary"
                :icon="Switch"
                :disabled="!canPrepare || !canPrepareTrustedPackage(row)"
                :loading="preparingId === row.packageId"
                @click="preparePackage(row.packageId)"
              >
                准备升级
              </el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <div class="detail-title">
            <span>版本回滚</span>
            <small>回滚点来自每次准备升级前的安装目录快照</small>
          </div>
        </div>
      </template>
      <el-table v-loading="loading" :data="status?.rollbackPoints ?? []" empty-text="暂无回滚点" height="280">
        <el-table-column prop="rollbackId" label="回滚点" min-width="190" show-overflow-tooltip />
        <el-table-column prop="version" label="版本" width="130" />
        <el-table-column label="文件数" width="100">
          <template #default="{ row }">{{ row.fileCount }}</template>
        </el-table-column>
        <el-table-column label="大小" width="110">
          <template #default="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
        </el-table-column>
        <el-table-column label="创建时间" width="170">
          <template #default="{ row }">{{ formatDateTime(row.createdTime) }}</template>
        </el-table-column>
        <el-table-column prop="directory" label="快照目录" min-width="260" show-overflow-tooltip />
        <el-table-column label="操作" width="140" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-button
                size="small"
                type="warning"
                :icon="RefreshLeft"
                :disabled="!canRollback"
                :loading="preparingId === row.rollbackId"
                @click="prepareRollback(row.rollbackId)"
              >
                准备回滚
              </el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox, type UploadFile } from 'element-plus'
import { Box, Clock, DocumentCopy, Download, FolderChecked, Refresh, RefreshLeft, Switch, UploadFilled } from '@element-plus/icons-vue'
import {
  exportProjectBackup,
  loadCompatibilityMatrix,
  loadLicenseStatus,
  loadProtocolDrivers,
  loadSupportSnapshot,
  loadWatchdogConfig,
  loadWatchdogStatus,
  loadUpdateStatus,
  prepareUpdatePackage,
  prepareUpdateRollback,
  restoreProjectBackup,
  saveWatchdogConfig,
  uploadUpdatePackage,
  type GatewayCompatibilityMatrix,
  type GatewayLicenseStatus,
  type GatewayProtocolDriverInfo,
  type GatewayUpdatePackageRecord,
  type GatewaySupportSnapshot,
  type GatewayWatchdogConfig,
  type GatewayWatchdogStatus,
  type GatewayUpdateStatus
} from '../api'
import WatchdogConfigPanel from './watchdog/WatchdogConfigPanel.vue'
import { formatDateTime } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

const { hasPermission } = usePermissions()
const loading = ref(false)
const uploading = ref(false)
const snapshotLoading = ref(false)
const backupLoading = ref(false)
const restoreLoading = ref(false)
const savingWatchdogConfig = ref(false)
const preparingId = ref('')
const status = ref<GatewayUpdateStatus | null>(null)
const watchdog = ref<GatewayWatchdogStatus | null>(null)
const watchdogConfig = ref<GatewayWatchdogConfig | null>(null)
const license = ref<GatewayLicenseStatus | null>(null)
const compatibility = ref<GatewayCompatibilityMatrix | null>(null)
const drivers = ref<GatewayProtocolDriverInfo[]>([])

const canUpload = computed(() => hasPermission(PERMISSIONS.maintenancePackagesUpload))
const canPrepare = computed(() => hasPermission(PERMISSIONS.maintenanceUpdatePrepare))
const canRollback = computed(() => hasPermission(PERMISSIONS.maintenanceRollbackPrepare))
const canEditWatchdog = computed(() => hasPermission(PERMISSIONS.maintenanceWatchdogEdit))
const totalPackageBytes = computed(() => (status.value?.packages ?? []).reduce((sum, item) => sum + (item.sizeBytes || 0), 0))
const latestRollbackLabel = computed(() => {
  const latest = status.value?.rollbackPoints?.[0]
  return latest ? formatDateTime(latest.createdTime) : '无快照'
})
const licenseLimitText = computed(() => {
  if (!license.value) return '-'
  const devices = license.value.maxDevices > 0 ? `${license.value.maxDevices} 设备` : '设备不限'
  const tags = license.value.maxTags > 0 ? `${license.value.maxTags} 点位` : '点位不限'
  return `${devices} / ${tags}`
})
const pendingText = computed(() => {
  const action = status.value?.pendingAction
  if (!action) return ''
  const type = action.actionType === 'Rollback' ? '版本回滚' : '离线升级'
  return `${type}已准备：${action.version || action.packageId || action.rollbackId}，脚本：${action.scriptPath}`
})

function canPrepareTrustedPackage(row: GatewayUpdatePackageRecord) {
  if (status.value?.requirePackageFileDigests && !row.contentHashValid) return false
  if (status.value?.requirePackageSignature && !row.signatureValid) return false
  return true
}

function trustStatusText(value: string | null | undefined) {
  switch ((value || '').toLowerCase()) {
    case 'trusted':
      return '签名可信'
    case 'contentverified':
      return '摘要通过'
    case 'unverified':
      return '未验证'
    case 'untrusted':
      return '不可信'
    default:
      return value || '-'
  }
}

function trustStatusType(value: string | null | undefined) {
  switch ((value || '').toLowerCase()) {
    case 'trusted':
      return 'success'
    case 'contentverified':
      return 'warning'
    case 'untrusted':
      return 'danger'
    default:
      return 'info'
  }
}

function trustSubtext(row: GatewayUpdatePackageRecord) {
  if (row.signatureValid) return row.signer || '已签名'
  if (row.contentHashValid) return `${row.fileCount || 0} 个文件`
  return '缺少摘要'
}

function trustStatusDetail(row: GatewayUpdatePackageRecord) {
  const detail = [row.trustMessage || trustStatusText(row.trustStatus)]
  if (row.fileCount) detail.push(`文件：${row.fileCount}`)
  if (row.signer) detail.push(`发布者：${row.signer}`)
  if (row.signedTime) detail.push(`签名时间：${formatDateTime(row.signedTime)}`)
  return detail.join('；')
}

onMounted(() => refresh())

async function refresh() {
  loading.value = true
  try {
    const [updateData, watchdogData, watchdogConfigData, licenseData, compatibilityData, driverData] = await Promise.all([
      loadUpdateStatus(),
      loadWatchdogStatus().catch(error => {
        ElMessage.warning(error instanceof Error ? error.message : '看门狗状态加载失败')
        return null
      }),
      loadWatchdogConfig().catch(error => {
        ElMessage.warning(error instanceof Error ? error.message : '看门狗配置加载失败')
        return null
      }),
      loadLicenseStatus().catch(error => {
        ElMessage.warning(error instanceof Error ? error.message : '许可证状态加载失败')
        return null
      }),
      loadCompatibilityMatrix().catch(error => {
        ElMessage.warning(error instanceof Error ? error.message : '兼容矩阵加载失败')
        return null
      }),
      loadProtocolDrivers().catch(error => {
        ElMessage.warning(error instanceof Error ? error.message : '协议驱动状态加载失败')
        return []
      })
    ])
    status.value = updateData
    watchdog.value = watchdogData
    watchdogConfig.value = watchdogConfigData
    license.value = licenseData
    compatibility.value = compatibilityData
    drivers.value = driverData
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '安装升级状态加载失败')
  } finally {
    loading.value = false
  }
}

async function persistWatchdogConfig(config: GatewayWatchdogConfig) {
  if (!canEditWatchdog.value) {
    ElMessage.warning('当前用户没有保存看门狗配置权限')
    return
  }
  savingWatchdogConfig.value = true
  try {
    watchdogConfig.value = await saveWatchdogConfig(config)
    ElMessage.success('看门狗配置已保存')
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '看门狗配置保存失败')
  } finally {
    savingWatchdogConfig.value = false
  }
}

async function downloadSupportSnapshot() {
  snapshotLoading.value = true
  try {
    const snapshot = await loadSupportSnapshot()
    downloadSnapshotJson(snapshot)
    ElMessage.success('售后快照已生成')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '售后快照生成失败')
  } finally {
    snapshotLoading.value = false
  }
}

async function downloadProjectBackup() {
  backupLoading.value = true
  try {
    const blob = await exportProjectBackup()
    downloadBlob(blob, `ipc-gateway-project-${snapshotTimestamp(new Date().toISOString())}.json`)
    ElMessage.success('项目备份已生成')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '项目备份导出失败')
  } finally {
    backupLoading.value = false
  }
}

async function handleRestoreBackup(uploadFile: UploadFile) {
  const raw = uploadFile.raw
  if (!raw) return
  try {
    await ElMessageBox.confirm('恢复项目备份会覆盖当前项目和通信配置。确认继续？', '恢复项目备份', {
      confirmButtonText: '确认',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch {
    return
  }

  restoreLoading.value = true
  try {
    const text = await raw.text()
    const result = await restoreProjectBackup(text)
    ElMessage.success(`项目已恢复：${result.projectName || result.projectId}`)
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '项目备份恢复失败')
  } finally {
    restoreLoading.value = false
  }
}

async function handleUploadFile(uploadFile: UploadFile) {
  const raw = uploadFile.raw
  if (!raw) return
  uploading.value = true
  try {
    const record = await uploadUpdatePackage(raw)
    ElMessage.success(`升级包已上传：${record.version}`)
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '升级包上传失败')
  } finally {
    uploading.value = false
  }
}

async function preparePackage(packageId: string) {
  try {
    await ElMessageBox.confirm('准备离线升级会创建当前版本快照，并生成待执行脚本。确认继续？', '准备离线升级', {
      confirmButtonText: '确认',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch {
    return
  }
  preparingId.value = packageId
  try {
    const result = await prepareUpdatePackage(packageId)
    ElMessage.success(result.message || '离线升级已准备')
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '离线升级准备失败')
  } finally {
    preparingId.value = ''
  }
}

async function prepareRollback(rollbackId: string) {
  try {
    await ElMessageBox.confirm('准备版本回滚会覆盖当前待执行升级动作。确认继续？', '准备版本回滚', {
      confirmButtonText: '确认',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch {
    return
  }
  preparingId.value = rollbackId
  try {
    const result = await prepareUpdateRollback(rollbackId)
    ElMessage.success(result.message || '版本回滚已准备')
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '版本回滚准备失败')
  } finally {
    preparingId.value = ''
  }
}

async function copyScriptPath() {
  const path = status.value?.pendingAction?.scriptPath || status.value?.offlineScriptPath
  if (!path) return
  await navigator.clipboard.writeText(path)
  ElMessage.success('脚本路径已复制')
}

function downloadSnapshotJson(snapshot: GatewaySupportSnapshot) {
  const payload = JSON.stringify(snapshot, null, 2)
  const blob = new Blob([payload], { type: 'application/json;charset=utf-8' })
  downloadBlob(blob, `ipc-gateway-support-${snapshotTimestamp(snapshot.capturedTimeUtc)}.json`)
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function driverSignatureType(value: string | null | undefined) {
  switch ((value || '').toLowerCase()) {
    case 'builtin':
    case 'trusted':
      return 'success'
    case 'unsigned':
    case 'unverified':
      return 'warning'
    case 'invalid':
    case 'hashmismatch':
    case 'missing':
    case 'untrusted':
      return 'danger'
    default:
      return 'info'
  }
}

function snapshotTimestamp(value: string) {
  const source = value || new Date().toISOString()
  const digits = source.replace(/\D/g, '')
  return digits.slice(0, 14) || 'snapshot'
}

function formatBytes(value: number) {
  if (!value) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let size = value
  let index = 0
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024
    index += 1
  }
  return `${size.toFixed(index === 0 ? 0 : 1)} ${units[index]}`
}

function watchdogStateText(state: string | null | undefined) {
  switch ((state || '').toLowerCase()) {
    case 'healthy':
      return '健康'
    case 'degraded':
      return '降级'
    case 'unhealthy':
      return '异常'
    case 'recovering':
      return '恢复中'
    case 'protected':
      return '保护中'
    case 'disabled':
      return '未启用'
    default:
      return state || '-'
  }
}

function watchdogStateType(state: string | null | undefined) {
  switch ((state || '').toLowerCase()) {
    case 'healthy':
      return 'success'
    case 'degraded':
    case 'recovering':
      return 'warning'
    case 'unhealthy':
    case 'protected':
      return 'danger'
    default:
      return 'info'
  }
}
</script>

<style scoped>
.maintenance-view {
  gap: 18px;
}

.commercial-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 16px;
}

.maintenance-hero {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 20px 24px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  background: linear-gradient(135deg, #0f766e, #1d4ed8);
  color: #fff;
}

.maintenance-hero h3 {
  margin: 3px 0 6px;
  font-size: 24px;
  font-weight: 700;
}

.maintenance-hero span,
.maintenance-hero .eyebrow {
  color: rgba(255, 255, 255, 0.82);
}

.hero-actions,
.row-actions,
.pending-body {
  display: flex;
  align-items: center;
  gap: 10px;
  white-space: nowrap;
}

.maintenance-status-tag {
  height: 30px;
  padding: 0 12px;
  font-weight: 700;
  letter-spacing: 0;
}

.maintenance-status-tag--enabled {
  color: #ecfdf5 !important;
  border-color: rgba(16, 185, 129, 0.88) !important;
  background: linear-gradient(135deg, #047857, #059669) !important;
  box-shadow: 0 8px 18px rgba(4, 120, 87, 0.28);
}

.maintenance-status-tag--disabled {
  color: #334155 !important;
  border-color: rgba(148, 163, 184, 0.55) !important;
  background: rgba(248, 250, 252, 0.9) !important;
}

.maintenance-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
}

.metric-panel :deep(.el-card__body) {
  display: flex;
  align-items: center;
  gap: 14px;
  min-height: 86px;
}

.metric-panel__icon {
  display: grid;
  place-items: center;
  width: 44px;
  height: 44px;
  border-radius: 8px;
  color: #0f766e;
  background: #ccfbf1;
  font-size: 22px;
}

.metric-panel small,
.metric-panel span {
  display: block;
  color: var(--el-text-color-secondary);
}

.metric-panel strong {
  display: block;
  margin: 4px 0;
  font-size: 22px;
  color: var(--el-text-color-primary);
}

.pending-alert {
  border-radius: 8px;
}

.pending-body {
  justify-content: space-between;
  width: 100%;
}

.watchdog-summary {
  display: grid;
  grid-template-columns: repeat(6, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 14px;
}

.watchdog-summary div {
  padding: 12px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  background: var(--el-fill-color-lighter);
}

.watchdog-summary span,
.restart-protection span {
  display: block;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.watchdog-summary strong {
  display: block;
  margin-top: 4px;
  color: var(--el-text-color-primary);
  font-size: 16px;
}

.watchdog-issue {
  margin-bottom: 12px;
}

.restart-protection {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  margin-top: 12px;
}

.maintenance-layout {
  display: grid;
  grid-template-columns: minmax(360px, 0.9fr) minmax(420px, 1.1fr);
  gap: 14px;
}

.upload-icon {
  font-size: 42px;
  color: var(--el-color-primary);
}

.path-panel :deep(.el-descriptions__label) {
  width: 110px;
  color: var(--el-text-color-secondary);
}

.policy-tags,
.trust-cell {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}

.trust-cell {
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  line-height: 1.2;
}

.trust-cell small {
  max-width: 130px;
  color: var(--el-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 1180px) {
  .maintenance-metrics,
  .commercial-grid,
  .maintenance-layout,
  .watchdog-summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 760px) {
  .maintenance-hero,
  .pending-body {
    align-items: flex-start;
    flex-direction: column;
  }

  .maintenance-metrics,
  .commercial-grid,
  .maintenance-layout,
  .watchdog-summary {
    grid-template-columns: 1fr;
  }
}
</style>

