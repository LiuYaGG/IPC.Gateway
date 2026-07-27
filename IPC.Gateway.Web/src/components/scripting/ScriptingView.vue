<template>
  <el-card class="script-center" shadow="never" v-loading="loading">
    <template #header>
      <div class="script-header">
        <div><strong>脚本中心</strong><p>通过受控 C# 脚本联动点位，并向独立数据库执行 INSERT / UPDATE。</p></div>
        <el-button :loading="loading" @click="load">刷新</el-button>
      </div>
    </template>
    <el-tabs v-model="activeTab">
      <el-tab-pane label="脚本" name="scripts">
        <ScriptDefinitionsPanel :scripts="overview.scripts" :statuses="overview.runtimeStatuses" :tag-options="tagOptions" :can-edit="canEdit" :can-execute="canExecute" @changed="load" />
      </el-tab-pane>
      <el-tab-pane label="数据库连接" name="connections">
        <ScriptConnectionsPanel :connections="overview.connections" :can-manage="canManageDatabases" @changed="load" />
      </el-tab-pane>
      <el-tab-pane label="写入目标" name="targets">
        <ScriptTargetsPanel :targets="overview.targets" :connections="overview.connections" :can-manage="canManageDatabases" @changed="load" />
      </el-tab-pane>
      <el-tab-pane label="运行状态" name="runtime">
        <ScriptRuntimePanel :scripts="overview.scripts" :statuses="overview.runtimeStatuses" :queue="overview.queueStatus" />
      </el-tab-pane>
    </el-tabs>
  </el-card>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import type { ProjectConfig } from '../../api'
import { loadScriptOverview, type ScriptCenterOverview } from '../../scriptingApi'
import { PERMISSIONS, usePermissions } from '../../utils/permissions'
import ScriptConnectionsPanel from './ScriptConnectionsPanel.vue'
import ScriptDefinitionsPanel from './ScriptDefinitionsPanel.vue'
import ScriptRuntimePanel from './ScriptRuntimePanel.vue'
import ScriptTargetsPanel from './ScriptTargetsPanel.vue'
import type { ScriptTagOption } from './scriptingModel'

const props = defineProps<{ project: ProjectConfig | null }>()
const { hasPermission } = usePermissions()
const activeTab = ref('scripts')
const loading = ref(false)
const overview = ref<ScriptCenterOverview>({
  connections: [], targets: [], scripts: [], runtimeStatuses: [],
  queueStatus: { pendingCount: 0, failedCount: 0, succeededCount: 0, retriedCount: 0, lastError: '' }
})

const canEdit = computed(() => hasPermission(PERMISSIONS.scriptsEdit))
const canExecute = computed(() => hasPermission(PERMISSIONS.scriptsExecute))
const canManageDatabases = computed(() => hasPermission(PERMISSIONS.scriptsDatabasesManage))
const tagOptions = computed<ScriptTagOption[]>(() => {
  const options: ScriptTagOption[] = []
  const channelNames = new Map((props.project?.channels ?? []).map(channel => [channel.id, channel.name || channel.id]))
  for (const device of props.project?.devices ?? []) {
    const channelName = channelNames.get(device.channelId) ?? device.channelId
    const deviceName = device.name || device.id
    for (const tag of device.tags ?? []) {
      options.push({
        value: `${device.channelId}/${device.id}//${tag.id}`,
        label: `${channelName}-${deviceName}-【设备直属】-${tag.name || tag.id}`
      })
    }
    for (const group of device.groups ?? []) {
      const groupName = group.name || group.id
      for (const tag of group.tags ?? []) {
        options.push({
          value: `${device.channelId}/${device.id}/${group.id}/${tag.id}`,
          label: `${channelName}-${deviceName}-【${groupName}】-${tag.name || tag.id}`
        })
      }
    }
  }
  return options.sort((left, right) => left.label.localeCompare(right.label, 'zh-CN'))
})

onMounted(load)

async function load() {
  loading.value = true
  try {
    overview.value = (await loadScriptOverview()).data
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '脚本中心加载失败')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.script-center { min-height: calc(100vh - 124px); }
.script-header { display: flex; justify-content: space-between; align-items: center; gap: 16px; }
.script-header p { margin: 5px 0 0; color: var(--el-text-color-secondary); font-size: 13px; }
</style>
