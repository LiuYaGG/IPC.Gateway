<template>
  <section class="view-stack security-view">
    <section class="security-header">
      <div>
        <h3>{{ tx('工业控制系统安全') }}</h3>
        <p>{{ tx('账号策略、接口鉴权、TLS 和证书状态按 ISA/IEC 62443 思路集中展示。') }}</p>
      </div>
      <a-button :loading="loading" type="primary" @click="load">
        <template #icon>
          <span class="ui-icon"><component :is="Refresh" /></span>
        </template>
        {{ tx('刷新') }}
      </a-button>
    </section>
    <section class="security-metrics">
      <a-card class="security-card" size="small">
        <div class="security-card__title">
          <span class="ui-icon"><Lock /></span>
          <span>{{ tx('账号策略') }}</span>
        </div>
        <strong>{{ summary?.passwordPolicy.enabled ? tx('强密码已启用') : tx('未启用强密码') }}</strong>
        <small>
          {{ tx('最小') }} {{ summary?.passwordPolicy.minLength ?? '-' }} {{ tx('位，最大') }}
          {{ summary?.passwordPolicy.maxLength ?? '-' }} {{ tx('位') }}
        </small>
        <div class="security-tags">
          <a-tag v-for="item in passwordRules" :key="item.label" :color="tagColor(item.enabled ? 'success' : 'info')">
            {{ tx(item.label) }}
          </a-tag>
        </div>
      </a-card>
      <a-card class="security-card" size="small">
        <div class="security-card__title">
          <span class="ui-icon"><Warning /></span>
          <span>{{ tx('登录锁定') }}</span>
        </div>
        <strong>{{ summary?.accountLockout.enabled ? tx('已启用') : tx('未启用') }}</strong>
        <small>
          {{ tx('失败') }} {{ summary?.accountLockout.maxFailedAttempts ?? '-' }} {{ tx('次后锁定') }}
          {{ summary?.accountLockout.lockoutMinutes ?? '-' }} {{ tx('分钟') }}
        </small>
        <div class="security-tags">
          <a-tag :color="tagColor(summary?.accountLockout.resetFailedCountOnSuccess ? 'success' : 'info')">
            {{ tx('成功登录后清零失败次数') }}
          </a-tag>
        </div>
      </a-card>
      <a-card class="security-card" size="small">
        <div class="security-card__title">
          <span class="ui-icon"><Key /></span>
          <span>TLS</span>
        </div>
        <strong>{{ tx(tlsState) }}</strong>
        <small>
          {{ summary?.tls.minimumProtocol || 'Tls12' }}{{ tx('，端口') }} {{ summary?.tls.httpsPort || tx('默认') }}
        </small>
        <div class="security-tags">
          <a-tag :color="tagColor(summary?.tls.requireHttps ? 'success' : 'info')">{{ tx('HTTPS 强制') }}</a-tag>
          <a-tag :color="tagColor(summary?.tls.enableHttpsRedirection ? 'success' : 'info')">
            {{ tx('自动跳转') }}
          </a-tag>
          <a-tag :color="tagColor(summary?.tls.enableHsts ? 'success' : 'info')">HSTS</a-tag>
        </div>
      </a-card>
      <a-card class="security-card" size="small">
        <div class="security-card__title">
          <span class="ui-icon"><Operation /></span>
          <span>{{ tx('接口鉴权与审计') }}</span>
        </div>
        <strong>{{ summary ? tx('已接入') : '-' }}</strong>
        <small>{{ tx('配置写入记录请求指纹，未授权和拒绝访问进入安全审计。') }}</small>
        <div class="security-tags">
          <a-tag :color="tagColor(summary?.api.auditConfigurationRequestHash ? 'success' : 'info')">
            {{ tx('配置哈希审计') }}
          </a-tag>
          <a-tag :color="tagColor(summary?.api.auditUnauthorizedRequests ? 'success' : 'info')">
            {{ tx('未授权审计') }}
          </a-tag>
          <a-tag :color="tagColor(summary?.api.auditForbiddenRequests ? 'success' : 'info')">
            {{ tx('拒绝访问审计') }}
          </a-tag>
        </div>
      </a-card>
      <a-card class="security-card" size="small">
        <div class="security-card__title">
          <span class="ui-icon"><Tickets /></span>
          <span>API Token</span>
        </div>
        <strong>{{ summary?.apiTokens.enabled ? tx('已启用') : tx('未启用') }}</strong>
        <small>
          {{ tx('启用') }} {{ summary?.apiTokens.enabledTokenCount ?? 0 }} {{ tx('/ 配置') }}
          {{ summary?.apiTokens.configuredTokenCount ?? 0 }} {{ tx('个 Token') }}
        </small>
        <div class="security-tags">
          <a-tag :color="tagColor('info')">{{ summary?.apiTokens.headerName || 'X-API-Token' }}</a-tag>
          <a-tag :color="tagColor(summary?.apiTokens.requireHttps ? 'success' : 'info')">HTTPS Token</a-tag>
        </div>
      </a-card>
      <a-card class="security-card" size="small">
        <div class="security-card__title">
          <span class="ui-icon"><Key /></span>
          <span>{{ tx('密钥加密存储') }}</span>
        </div>
        <strong>{{ summary?.secretStorage.enabled ? tx('已启用') : tx('未启用') }}</strong>
        <small>
          {{ summary?.secretStorage.masterKeyConfigured ? tx('已配置主密钥') : tx('使用环境变量或本机派生密钥') }}
        </small>
        <div class="security-tags">
          <a-tag :color="tagColor('info')">
            {{ summary?.secretStorage.environmentVariableName || 'IPC_GATEWAY_SECRET_KEY' }}
          </a-tag>
        </div>
      </a-card>
    </section>
    <a-card class="panel-card security-certificate-card" size="small">
      <template #title>
        <div class="card-header">
          <div class="detail-title">
            <span>{{ tx('证书状态') }}</span>
            <small>{{ tx('TLS 与 OPC UA 证书有效期、指纹和私钥状态') }}</small>
          </div>
          <div class="card-actions">
            <a-tag :color="tagColor('success')">{{ tx('健康') }} {{ inventory?.healthyCount ?? 0 }}</a-tag>
            <a-tag :color="tagColor('warning')">{{ tx('即将过期') }} {{ inventory?.expiringSoonCount ?? 0 }}</a-tag>
            <a-tag :color="tagColor('danger')">{{ tx('已过期') }} {{ inventory?.expiredCount ?? 0 }}</a-tag>
          </div>
        </div>
      </template>
      <GatewayTable
        resize-key="components.SecurityView.vue.0"
        :loading="loading"
        :data-source="inventory?.certificates ?? []"
        :row-key="(_row: any, index: any) => index"
        size="small"
        :pagination="false"
        :columns="
          localizeColumns([
            { key: 'column-0', title: tx('状态'), width: '110' },
            { key: 'column-1', title: tx('来源'), dataIndex: 'source', width: '110' },
            { key: 'column-2', title: tx('主体'), dataIndex: 'subject', width: '240', ellipsis: true },
            { key: 'column-3', title: tx('到期时间'), width: '170' },
            { key: 'column-4', title: tx('剩余天数'), width: '100' },
            { key: 'column-5', title: tx('私钥'), width: '90' },
            { key: 'column-6', title: tx('指纹'), dataIndex: 'thumbprint', width: '220', ellipsis: true },
            { key: 'column-7', title: tx('路径'), dataIndex: 'path', width: '260', ellipsis: true },
            { key: 'column-8', title: tx('说明'), dataIndex: 'errorMessage', width: '220', ellipsis: true }
          ])
        "
        :scroll="{ x: 1520, y: NaN }"
      >
        <template #bodyCell="tableCell">
          <template v-if="tableCell.column.key === 'column-0'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <a-tag :color="tagColor(certificateStateType(row.state))">
                {{ tx(statusText(certificateStateText(row.state))) }}
              </a-tag>
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-3'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              {{ row.notAfter ? formatDateTime(row.notAfter) : '-' }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-4'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              {{ tx(statusText(row.state === 'Missing' || row.state === 'Error' ? '-' : row.daysRemaining)) }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-5'">
            <template v-for="{ row } in [{ row: tableCell.record, $index: tableCell.index }]" :key="tableCell.index">
              <a-tag :color="tagColor(row.hasPrivateKey ? 'success' : 'info')">
                {{ row.hasPrivateKey ? tx('有') : tx('无') }}
              </a-tag>
            </template>
          </template>
        </template>
        <template #emptyText><a-empty :description="tx(tx('暂无证书信息'))" /></template>
      </GatewayTable>
    </a-card>
  </section>
</template>

<script setup lang="ts">
import { notify } from '../utils/ui'
import { tx, localizeColumns } from '../i18n'
import { statusText } from '../utils/display'
import { tagColor } from '../utils/ui'
import { computed, onMounted, ref } from 'vue'

import {
  KeyOutlined as Key,
  LockOutlined as Lock,
  AppstoreOutlined as Operation,
  ReloadOutlined as Refresh,
  AuditOutlined as Tickets,
  WarningOutlined as Warning
} from '@ant-design/icons-vue'
import {
  loadSecurityCertificates,
  loadSecuritySummary,
  type GatewayCertificateInventory,
  type GatewaySecuritySummary
} from '../api'
import { formatDateTime } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

const { hasPermission } = usePermissions()
const loading = ref(false)
const summary = ref<GatewaySecuritySummary | null>(null)
const inventory = ref<GatewayCertificateInventory | null>(null)

const passwordRules = computed(() => {
  const policy = summary.value?.passwordPolicy
  return [
    { label: '大写字母', enabled: !!policy?.requireUppercase },
    { label: '小写字母', enabled: !!policy?.requireLowercase },
    { label: '数字', enabled: !!policy?.requireDigit },
    { label: '特殊符号', enabled: !!policy?.requireSymbol },
    { label: '禁止包含账号', enabled: !!policy?.rejectUsernameInPassword }
  ]
})

const tlsState = computed(() => {
  const tls = summary.value?.tls
  if (!tls) return '-'
  if (tls.requireHttps && tls.certificateConfigured) return '强制 HTTPS'
  if (tls.certificateConfigured) return '证书已配置'
  return '未配置证书'
})

onMounted(() => load())

async function load() {
  loading.value = true
  try {
    summary.value = await loadSecuritySummary()
    if (hasPermission(PERMISSIONS.securityCertificatesManage)) {
      inventory.value = await loadSecurityCertificates()
    } else {
      inventory.value = {
        expiringSoonDays: summary.value.certificates.expiringSoonDays,
        totalCount: 0,
        healthyCount: 0,
        expiringSoonCount: 0,
        expiredCount: 0,
        certificates: []
      }
    }
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '工业安全信息加载失败')
  } finally {
    loading.value = false
  }
}

function certificateStateText(state: string) {
  switch ((state || '').toLowerCase()) {
    case 'healthy':
      return '健康'
    case 'expiringsoon':
      return '即将过期'
    case 'expired':
      return '已过期'
    case 'missing':
      return '未配置'
    case 'error':
      return '异常'
    default:
      return state || '-'
  }
}

function certificateStateType(state: string) {
  switch ((state || '').toLowerCase()) {
    case 'healthy':
      return 'success'
    case 'expiringsoon':
      return 'warning'
    case 'expired':
    case 'error':
      return 'danger'
    default:
      return 'info'
  }
}
</script>

<style scoped>
.security-view {
  gap: 16px;
  min-width: 0;
}

/* Use the same flex height chain as the other list cards. Ant Card's clearfix
   pseudo-elements must not become additional rows in a grid layout. */
.security-view > .security-certificate-card {
  display: flex;
  flex-direction: column;
  flex: 1 0 280px;
  min-width: 0;
  min-height: 280px;
}
.security-view > .security-certificate-card :deep(.ant-card-head) {
  flex: none;
}
.security-view > .security-certificate-card :deep(.ant-card-body) {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
  min-height: 0;
  overflow: hidden;
}
.security-certificate-card .card-header {
  flex-wrap: wrap;
}

.security-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.security-header h3 {
  margin: 0 0 6px;
  color: #111827;
}

.security-header p {
  margin: 0;
  color: #64748b;
}

.security-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
}

.security-card {
  border-radius: 8px;
}

.security-card :deep(.ant-card-body) {
  display: grid;
  gap: 10px;
}

.security-card__title {
  display: flex;
  align-items: center;
  gap: 8px;
  color: #475569;
  font-weight: 700;
}

.security-card strong {
  color: #111827;
  font-size: calc(18px + 1pt);
}

.security-card small {
  min-height: 36px;
  color: #64748b;
  line-height: 1.5;
}

.security-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

@media (max-width: 1180px) {
  .security-metrics {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 760px) {
  .security-header {
    align-items: flex-start;
    flex-direction: column;
  }

  .security-metrics {
    grid-template-columns: 1fr;
  }
}
</style>
