<template>
  <section>
    <div class="script-toolbar">
      <a-alert
        :message="tx('写入目标决定脚本可访问的表、字段与更新键；未列入白名单的字段会被拒绝。')"
        type="warning"
        :closable="false"
      />
      <a-button type="primary" :disabled="!canManage || connections.length === 0" @click="openCreate">
        {{ tx('新增目标') }}
      </a-button>
    </div>
    <GatewayTable
      resize-key="components.scripting.ScriptTargetsPanel.vue.0"
      :data-source="targets"
      :row-key="(_row: any, index: any) => index"
      size="small"
      :pagination="false"
      :columns="
        localizeColumns([
          { key: 'column-0', title: tx('名称'), dataIndex: 'name', width: '150' },
          { key: 'column-1', title: tx('数据库连接'), width: '145' },
          { key: 'column-2', title: tx('数据表'), width: '150' },
          { key: 'column-3', title: tx('允许操作'), width: '135' },
          { key: 'column-4', title: tx('字段白名单'), width: '210', ellipsis: true },
          { key: 'column-5', title: tx('操作'), width: '155', fixed: 'right' }
        ])
      "
      :scroll="{ x: 945 }"
    >
      <template #bodyCell="tableCell">
        <template v-if="tableCell.column.key === 'column-1'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{ connectionName(scope.row.connectionId) }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-2'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{ [scope.row.schema, scope.row.table].filter(Boolean).join('.') }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-3'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{
              [scope.row.allowInsert ? 'INSERT' : '', scope.row.allowUpdate ? 'UPDATE' : ''].filter(Boolean).join(' / ')
            }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-4'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            {{ scope.row.allowedColumns.join(', ') }}
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-5'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <a-button :disabled="!canManage" @click="openEdit(scope.row)" type="link">{{ tx('编辑') }}</a-button>
            <a-button :disabled="!canManage" @click="remove(scope.row)" type="link" danger>{{ tx('删除') }}</a-button>
          </template>
        </template>
      </template>
      <template #emptyText><a-empty :description="tx(tx('尚未配置写入目标'))" /></template>
    </GatewayTable>
    <a-modal
      centered
      v-model:open="dialogVisible"
      :title="tx('数据库写入目标')"
      width="720px"
      destroy-on-close
    >
      <a-form
        :model="form"
        :label-col="{ style: { width: '120px' } }"
        :wrapper-col="{ style: { flex: 1 } }"
        label-align="left"
      >
        <a-form-item :label="tx('目标名称')"><gateway-input v-model="form.name" /></a-form-item>
        <a-form-item :label="tx('数据库连接')">
          <gateway-select v-model="form.connectionId" style="width: 100%">
            <a-select-option v-for="item in connections" :key="item.id" :label="item.name" :value="item.id">
              {{ item.name }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-row :gutter="12">
          <a-col :span="10">
            <a-form-item :label="tx('架构/Schema')">
              <gateway-input v-model="form.schema" :placeholder="tx('可留空')" />
            </a-form-item>
          </a-col>
          <a-col :span="14">
            <a-form-item :label="tx('表名')"><gateway-input v-model="form.table" /></a-form-item>
          </a-col>
        </a-row>
        <a-form-item :label="tx('允许操作')">
          <a-checkbox v-model:checked="form.allowInsert">INSERT</a-checkbox>
          <a-checkbox v-model:checked="form.allowUpdate">UPDATE</a-checkbox>
        </a-form-item>
        <a-form-item :label="tx('字段白名单')">
          <gateway-input
            v-model="allowedColumnsText"
            type="textarea"
            :rows="3"
            :placeholder="tx('逗号或换行分隔，例如 TagName, Value, CollectedAt')"
          />
        </a-form-item>
        <a-form-item :label="tx('更新键字段')">
          <gateway-input v-model="keyColumnsText" :placeholder="tx('仅 UPDATE 使用，例如 DeviceId, TagName')" />
        </a-form-item>
        <a-form-item :label="tx('最大影响行数')">
          <gateway-number-input v-model="form.maxAffectedRows" :min="1" :max="1000" />
        </a-form-item>
        <a-form-item :label="tx('启用')"><a-switch v-model:checked="form.enabled" /></a-form-item>
      </a-form>
      <template #footer>
        <a-button @click="dialogVisible = false">{{ tx('取消') }}</a-button>
        <a-button type="primary" :loading="saving" @click="save">{{ tx('保存') }}</a-button>
      </template>
    </a-modal>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../../i18n'
import { notify } from '../../utils/ui'
import { tx, localizeColumns } from '../../i18n'
import { confirmAction } from '../../utils/ui'
import { reactive, ref } from 'vue'

import {
  deleteScriptTarget,
  saveScriptTarget,
  type ScriptDatabaseConnection,
  type ScriptDatabaseTarget
} from '../../scriptingApi'
import { cloneValue, createTarget, splitColumns } from './scriptingModel'

const props = defineProps<{
  targets: ScriptDatabaseTarget[]
  connections: ScriptDatabaseConnection[]
  canManage: boolean
}>()
const emit = defineEmits<{ changed: [] }>()
const dialogVisible = ref(false)
const saving = ref(false)
const allowedColumnsText = ref('')
const keyColumnsText = ref('')
const form = reactive<ScriptDatabaseTarget>(createTarget())

function openCreate() {
  Object.assign(form, createTarget(props.connections[0]?.id ?? ''))
  allowedColumnsText.value = ''
  keyColumnsText.value = ''
  dialogVisible.value = true
}

function openEdit(target: ScriptDatabaseTarget) {
  Object.assign(form, cloneValue(target))
  allowedColumnsText.value = target.allowedColumns.join(', ')
  keyColumnsText.value = target.keyColumns.join(', ')
  dialogVisible.value = true
}

async function save() {
  form.allowedColumns = splitColumns(allowedColumnsText.value)
  form.keyColumns = splitColumns(keyColumnsText.value)
  if (!form.name.trim() || !form.connectionId || !form.table.trim() || form.allowedColumns.length === 0)
    return notify.warning('请填写名称、连接、表名和字段白名单')
  if (!form.allowInsert && !form.allowUpdate) return notify.warning('至少允许一种写入操作')
  if (form.allowUpdate && form.keyColumns.length === 0) return notify.warning('允许 UPDATE 时必须配置更新键字段')
  saving.value = true
  try {
    await saveScriptTarget(cloneValue(form))
    notify.success('写入目标已保存')
    dialogVisible.value = false
    emit('changed')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function remove(target: ScriptDatabaseTarget) {
  try {
    await confirmAction(formatMessage('确定删除目标“{0}”吗？', [target.name]), '删除确认', { type: 'warning' })
    await deleteScriptTarget(target.id)
    notify.success('写入目标已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') notify.error(error instanceof Error ? error.message : '删除失败')
  }
}

function connectionName(id: string) {
  return props.connections.find((item) => item.id === id)?.name ?? id
}
</script>

<style scoped>
.script-toolbar {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-bottom: 16px;
}
.script-toolbar .ant-alert {
  flex: 1;
}
</style>
