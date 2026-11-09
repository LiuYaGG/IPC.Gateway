<template>
  <a-card class="script-center" v-busy="loading" size="small">
    <template #title>
      <div class="script-header">
        <div>
          <strong>{{ tx('脚本中心') }}</strong>
          <p>
            {{ tx('支持数据库写入和点位联动两类受控 C# 脚本；数据库仅允许 INSERT / UPDATE，点位写入仅限白名单目标。') }}
          </p>
        </div>
        <a-button :loading="loading" @click="load">{{ tx('刷新') }}</a-button>
      </div>
    </template>
    <a-tabs v-model:active-key="activeTab">
      <a-tab-pane :tab="tx('脚本')" key="scripts">
        <ScriptDefinitionsPanel
          :scripts="overview.scripts"
          :statuses="overview.runtimeStatuses"
          :tag-options="tagOptions"
          :can-edit="canEdit"
          :can-execute="canExecute"
          @changed="load"
        />
      </a-tab-pane>
      <a-tab-pane :tab="tx('数据库连接')" key="connections">
        <ScriptConnectionsPanel :connections="overview.connections" :can-manage="canManageDatabases" @changed="load" />
      </a-tab-pane>
      <a-tab-pane :tab="tx('写入目标')" key="targets">
        <ScriptTargetsPanel
          :targets="overview.targets"
          :connections="overview.connections"
          :can-manage="canManageDatabases"
          @changed="load"
        />
      </a-tab-pane>
      <a-tab-pane :tab="tx('运行状态')" key="runtime">
        <ScriptRuntimePanel
          :scripts="overview.scripts"
          :statuses="overview.runtimeStatuses"
          :queue="overview.queueStatus"
        />
      </a-tab-pane>
    </a-tabs>
  </a-card>
</template>

<script setup lang="ts">
import { formatMessage } from '../../i18n'
import { notify } from '../../utils/ui'
import { tx } from '../../i18n'
import { computed, onMounted, ref } from 'vue'

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
  connections: [],
  targets: [],
  scripts: [],
  runtimeStatuses: [],
  queueStatus: { pendingCount: 0, failedCount: 0, succeededCount: 0, retriedCount: 0, lastError: '' }
})

const canEdit = computed(() => hasPermission(PERMISSIONS.scriptsEdit))
const canExecute = computed(() => hasPermission(PERMISSIONS.scriptsExecute))
const canManageDatabases = computed(() => hasPermission(PERMISSIONS.scriptsDatabasesManage))
const tagOptions = computed<ScriptTagOption[]>(() => {
  const options: ScriptTagOption[] = []
  const enabledChannels = (props.project?.channels ?? []).filter((channel) => channel.enabled !== false)
  const channelNames = new Map(enabledChannels.map((channel) => [channel.id, channel.name || channel.id]))
  for (const device of props.project?.devices ?? []) {
    if (device.enabled === false || !channelNames.has(device.channelId)) continue
    const channelName = channelNames.get(device.channelId) ?? device.channelId
    const deviceName = device.name || device.id
    for (const tag of device.tags ?? []) {
      if (tag.enabled === false) continue
      options.push({
        value: `${device.channelId}/${device.id}//${tag.id}`,
        label: formatMessage('{0}-{1}-【设备直属】-{2}', [channelName, deviceName, tag.name || tag.id]),
        dataType: tag.dataType,
        canRead: normalizeAccessMode(tag.accessMode) !== 'writeonly',
        canWrite: normalizeAccessMode(tag.accessMode) !== 'readonly'
      })
    }
    for (const group of device.groups ?? []) {
      if (group.enabled === false) continue
      const groupName = group.name || group.id
      for (const tag of group.tags ?? []) {
        if (tag.enabled === false) continue
        options.push({
          value: `${device.channelId}/${device.id}/${group.id}/${tag.id}`,
          label: `${channelName}-${deviceName}-【${groupName}】-${tag.name || tag.id}`,
          dataType: tag.dataType,
          canRead: normalizeAccessMode(tag.accessMode) !== 'writeonly',
          canWrite: normalizeAccessMode(tag.accessMode) !== 'readonly'
        })
      }
    }
  }
  return options.sort((left, right) => left.label.localeCompare(right.label, 'zh-CN'))
})

function normalizeAccessMode(value: string | null | undefined) {
  return (value ?? '').trim().toLowerCase()
}

onMounted(load)

async function load() {
  loading.value = true
  try {
    overview.value = (await loadScriptOverview()).data
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '脚本中心加载失败')
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.script-center {
  min-height: calc(100vh - 124px);
}
.script-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
}
.script-header p {
  margin: 5px 0 0;
  color: var(--ui-text-color-secondary);
  font-size: calc(13px + 1pt);
}
</style>
