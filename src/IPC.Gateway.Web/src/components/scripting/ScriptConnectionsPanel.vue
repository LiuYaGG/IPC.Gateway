<template>
  <section>
    <div class="script-toolbar">
      <a-alert :message="tx('连接串会由网关密钥加密保存；页面重新加载后只显示掩码。')" type="info" :closable="false" />
      <a-button type="primary" :disabled="!canManage" @click="openCreate">{{ tx('新增连接') }}</a-button>
    </div>
    <GatewayTable
      resize-key="components.scripting.ScriptConnectionsPanel.vue.0"
      :data-source="connections"
      :row-key="(_row: any, index: any) => index"
      size="small"
      :pagination="false"
      :columns="
        localizeColumns([
          { key: 'column-0', title: tx('名称'), dataIndex: 'name', width: '160' },
          { key: 'column-1', title: tx('数据库'), dataIndex: 'provider', customRender: ({ text }: any) => tx(databaseProviderLabel(text)), width: '125' },
          { key: 'column-2', title: tx('状态'), width: '100' },
          { key: 'column-3', title: tx('超时(秒)'), dataIndex: 'connectionTimeoutSeconds', width: '105' },
          { key: 'column-4', title: tx('连接串'), dataIndex: 'connectionString', width: '230', ellipsis: true },
          { key: 'column-5', title: tx('操作'), width: '230', fixed: 'right' }
        ])
      "
      :scroll="{ x: 950 }"
    >
      <template #bodyCell="tableCell">
        <template v-if="tableCell.column.key === 'column-2'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <a-tag :color="tagColor(scope.row.enabled ? 'success' : 'info')">
              {{ scope.row.enabled ? tx('启用') : tx('停用') }}
            </a-tag>
          </template>
        </template>
        <template v-if="tableCell.column.key === 'column-5'">
          <template v-for="scope in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
            <a-button :disabled="!canManage" @click="test(scope.row)" type="link">{{ tx('测试') }}</a-button>
            <a-button :disabled="!canManage" @click="openEdit(scope.row)" type="link">{{ tx('编辑') }}</a-button>
            <a-button :disabled="!canManage" @click="remove(scope.row)" type="link" danger>{{ tx('删除') }}</a-button>
          </template>
        </template>
      </template>
      <template #emptyText><a-empty :description="tx(tx('尚未配置数据库连接'))" /></template>
    </GatewayTable>
    <a-modal
      centered
      v-model:open="dialogVisible"
      :title="tx('数据库连接')"
      width="680px"
      destroy-on-close
    >
      <a-form
        :model="form"
        :label-col="{ style: { width: '110px' } }"
        :wrapper-col="{ style: { flex: 1 } }"
        label-align="left"
      >
        <a-form-item :label="tx('名称')"><gateway-input v-model="form.name" /></a-form-item>
        <a-form-item :label="tx('数据库类型')">
          <gateway-select v-model="form.provider" style="width: 100%">
            <a-select-option label="SQL Server" value="SqlServer">{{ 'SQL Server' }}</a-select-option>
            <a-select-option label="PostgreSQL" value="PostgreSql">{{ 'PostgreSQL' }}</a-select-option>
            <a-select-option label="MySQL / MariaDB" value="MySql">{{ 'MySQL / MariaDB' }}</a-select-option>
            <a-select-option label="SQLite" value="Sqlite">{{ 'SQLite' }}</a-select-option>
            <a-select-option label="Oracle" value="Oracle">{{ 'Oracle' }}</a-select-option>
            <a-select-option :label="tx('达梦')" value="Dameng">{{ tx('达梦') }}</a-select-option>
            <a-select-option :label="tx('人大金仓 KingbaseES')" value="KingbaseEs">
              {{ tx('人大金仓 KingbaseES') }}
            </a-select-option>
            <a-select-option label="ClickHouse" value="ClickHouse">{{ 'ClickHouse' }}</a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('连接字符串')">
          <gateway-input
            v-model="form.connectionString"
            type="textarea"
            :rows="5"
            :placeholder="tx(connectionPlaceholder)"
          />
          <span class="connection-hint">{{ tx(providerHint) }}</span>
        </a-form-item>
        <a-form-item :label="tx('连接超时')">
          <gateway-number-input v-model="form.connectionTimeoutSeconds" :min="1" :max="120" />
          {{ tx('秒') }}
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
import { tagColor, confirmAction } from '../../utils/ui'
import { computed, reactive, ref } from 'vue'

import {
  deleteScriptConnection,
  saveScriptConnection,
  testScriptConnection,
  type ScriptDatabaseConnection
} from '../../scriptingApi'
import { cloneValue, createConnection, databaseProviderLabel } from './scriptingModel'

defineProps<{ connections: ScriptDatabaseConnection[]; canManage: boolean }>()
const emit = defineEmits<{ changed: [] }>()
const dialogVisible = ref(false)
const saving = ref(false)
const form = reactive<ScriptDatabaseConnection>(createConnection())
const connectionExamples: Record<ScriptDatabaseConnection['provider'], string> = {
  SqlServer: 'Server=127.0.0.1;Database=Gateway;User Id=sa;Password=密码;TrustServerCertificate=True',
  PostgreSql: 'Host=127.0.0.1;Port=5432;Database=gateway;Username=postgres;Password=密码',
  MySql: 'Server=127.0.0.1;Port=3306;Database=gateway;User ID=root;Password=密码',
  Sqlite: 'Data Source=D:\\data\\gateway.db',
  Oracle: 'User Id=system;Password=密码;Data Source=127.0.0.1:1521/ORCL',
  Dameng: 'Server=127.0.0.1;Port=5236;User Id=SYSDBA;Password=密码',
  KingbaseEs: 'Server=127.0.0.1;Port=54321;Database=TEST;User Id=SYSTEM;Password=密码',
  ClickHouse: 'Host=127.0.0.1;Port=8123;Database=default;Username=default;Password=密码'
}
const connectionPlaceholder = computed(() => connectionExamples[form.provider])
const providerHint = computed(() =>
  form.provider === 'ClickHouse'
    ? 'ClickHouse 使用 HTTP/HTTPS 端口（通常为 8123/8443）；UPDATE 需要服务器和表引擎支持轻量更新。'
    : '请填写该数据库驱动支持的完整连接字符串。'
)

function openCreate() {
  Object.assign(form, createConnection())
  dialogVisible.value = true
}

function openEdit(connection: ScriptDatabaseConnection) {
  Object.assign(form, cloneValue(connection))
  dialogVisible.value = true
}

async function save() {
  if (!form.name.trim() || !form.connectionString.trim()) return notify.warning('请填写名称和连接字符串')
  saving.value = true
  try {
    await saveScriptConnection(cloneValue(form))
    notify.success('数据库连接已保存')
    dialogVisible.value = false
    emit('changed')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function test(connection: ScriptDatabaseConnection) {
  try {
    const response = await testScriptConnection(connection.id)
    notify.success(response.data.message || '连接成功')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '连接失败')
  }
}

async function remove(connection: ScriptDatabaseConnection) {
  try {
    await confirmAction(formatMessage('确定删除连接“{0}”吗？', [connection.name]), '删除确认', { type: 'warning' })
    await deleteScriptConnection(connection.id)
    notify.success('连接已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') notify.error(error instanceof Error ? error.message : '删除失败')
  }
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
.connection-hint {
  display: block;
  margin-top: 6px;
  line-height: 1.5;
}
</style>
