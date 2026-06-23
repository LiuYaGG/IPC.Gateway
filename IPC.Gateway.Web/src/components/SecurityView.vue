<template>
  <section class="view-stack security-view">
    <section class="security-header">
      <div>
        <h3>工业控制系统安全</h3>
        <p>账号策略、接口鉴权、TLS 和证书状态按 ISA/IEC 62443 思路集中展示。</p>
      </div>
      <el-button :icon="Refresh" :loading="loading" type="primary" @click="load">刷新</el-button>
    </section>

    <section class="security-metrics">
      <el-card shadow="never" class="security-card">
        <div class="security-card__title">
          <el-icon><Lock /></el-icon>
          <span>账号策略</span>
        </div>
        <strong>{{ summary?.passwordPolicy.enabled ? '强密码已启用' : '未启用强密码' }}</strong>
        <small>最小 {{ summary?.passwordPolicy.minLength ?? '-' }} 位，最大 {{ summary?.passwordPolicy.maxLength ?? '-' }} 位</small>
        <div class="security-tags">
          <el-tag v-for="item in passwordRules" :key="item.label" size="small" :type="item.enabled ? 'success' : 'info'" effect="light">
            {{ item.label }}
          </el-tag>
        </div>
      </el-card>

      <el-card shadow="never" class="security-card">
        <div class="security-card__title">
          <el-icon><Warning /></el-icon>
          <span>登录锁定</span>
        </div>
        <strong>{{ summary?.accountLockout.enabled ? '已启用' : '未启用' }}</strong>
        <small>失败 {{ summary?.accountLockout.maxFailedAttempts ?? '-' }} 次后锁定 {{ summary?.accountLockout.lockoutMinutes ?? '-' }} 分钟</small>
        <div class="security-tags">
          <el-tag size="small" :type="summary?.accountLockout.resetFailedCountOnSuccess ? 'success' : 'info'" effect="light">
            成功登录后清零失败次数
          </el-tag>
        </div>
      </el-card>

      <el-card shadow="never" class="security-card">
        <div class="security-card__title">
          <el-icon><Key /></el-icon>
          <span>TLS</span>
        </div>
        <strong>{{ tlsState }}</strong>
        <small>{{ summary?.tls.minimumProtocol || 'Tls12' }}，端口 {{ summary?.tls.httpsPort || '默认' }}</small>
        <div class="security-tags">
          <el-tag size="small" :type="summary?.tls.requireHttps ? 'success' : 'info'" effect="light">HTTPS 强制</el-tag>
          <el-tag size="small" :type="summary?.tls.enableHttpsRedirection ? 'success' : 'info'" effect="light">自动跳转</el-tag>
          <el-tag size="small" :type="summary?.tls.enableHsts ? 'success' : 'info'" effect="light">HSTS</el-tag>
        </div>
      </el-card>

      <el-card shadow="never" class="security-card">
        <div class="security-card__title">
          <el-icon><Operation /></el-icon>
          <span>接口鉴权与审计</span>
        </div>
        <strong>{{ summary ? '已接入' : '-' }}</strong>
        <small>配置写入记录请求指纹，未授权和拒绝访问进入安全审计。</small>
        <div class="security-tags">
          <el-tag size="small" :type="summary?.api.auditConfigurationRequestHash ? 'success' : 'info'" effect="light">配置哈希审计</el-tag>
          <el-tag size="small" :type="summary?.api.auditUnauthorizedRequests ? 'success' : 'info'" effect="light">未授权审计</el-tag>
          <el-tag size="small" :type="summary?.api.auditForbiddenRequests ? 'success' : 'info'" effect="light">拒绝访问审计</el-tag>
        </div>
      </el-card>

      <el-card shadow="never" class="security-card">
        <div class="security-card__title">
          <el-icon><Tickets /></el-icon>
          <span>API Token</span>
        </div>
        <strong>{{ summary?.apiTokens.enabled ? '已启用' : '未启用' }}</strong>
        <small>启用 {{ summary?.apiTokens.enabledTokenCount ?? 0 }} / 配置 {{ summary?.apiTokens.configuredTokenCount ?? 0 }} 个 Token</small>
        <div class="security-tags">
          <el-tag size="small" type="info" effect="light">{{ summary?.apiTokens.headerName || 'X-API-Token' }}</el-tag>
          <el-tag size="small" :type="summary?.apiTokens.requireHttps ? 'success' : 'info'" effect="light">HTTPS Token</el-tag>
        </div>
      </el-card>

      <el-card shadow="never" class="security-card">
        <div class="security-card__title">
          <el-icon><Key /></el-icon>
          <span>密钥加密存储</span>
        </div>
        <strong>{{ summary?.secretStorage.enabled ? '已启用' : '未启用' }}</strong>
        <small>{{ summary?.secretStorage.masterKeyConfigured ? '已配置主密钥' : '使用环境变量或本机派生密钥' }}</small>
        <div class="security-tags">
          <el-tag size="small" type="info" effect="light">{{ summary?.secretStorage.environmentVariableName || 'IPC_GATEWAY_SECRET_KEY' }}</el-tag>
        </div>
      </el-card>
    </section>

    <el-card shadow="never" class="panel-card">
      <template #header>
        <div class="card-header">
          <div class="detail-title">
            <span>证书状态</span>
            <small>TLS 与 OPC UA 证书有效期、指纹和私钥状态</small>
          </div>
          <div class="card-actions">
            <el-tag type="success">健康 {{ inventory?.healthyCount ?? 0 }}</el-tag>
            <el-tag type="warning">即将过期 {{ inventory?.expiringSoonCount ?? 0 }}</el-tag>
            <el-tag type="danger">已过期 {{ inventory?.expiredCount ?? 0 }}</el-tag>
          </div>
        </div>
      </template>

      <el-table v-loading="loading" :data="inventory?.certificates ?? []" height="calc(100vh - 470px)" empty-text="暂无证书信息">
        <el-table-column label="状态" width="110">
          <template #default="{ row }">
            <el-tag :type="certificateStateType(row.state)" effect="light">{{ certificateStateText(row.state) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="source" label="来源" width="110" />
        <el-table-column prop="subject" label="主体" min-width="240" show-overflow-tooltip />
        <el-table-column label="到期时间" width="170">
          <template #default="{ row }">{{ row.notAfter ? formatDateTime(row.notAfter) : '-' }}</template>
        </el-table-column>
        <el-table-column label="剩余天数" width="100">
          <template #default="{ row }">{{ row.state === 'Missing' || row.state === 'Error' ? '-' : row.daysRemaining }}</template>
        </el-table-column>
        <el-table-column label="私钥" width="90">
          <template #default="{ row }">
            <el-tag size="small" :type="row.hasPrivateKey ? 'success' : 'info'" effect="plain">
              {{ row.hasPrivateKey ? '有' : '无' }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="thumbprint" label="指纹" min-width="220" show-overflow-tooltip />
        <el-table-column prop="path" label="路径" min-width="260" show-overflow-tooltip />
        <el-table-column prop="errorMessage" label="说明" min-width="220" show-overflow-tooltip />
      </el-table>
    </el-card>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Key, Lock, Operation, Refresh, Tickets, Warning } from '@element-plus/icons-vue'
import { loadSecurityCertificates, loadSecuritySummary, type GatewayCertificateInventory, type GatewaySecuritySummary } from '../api'
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
    ElMessage.error(error instanceof Error ? error.message : '工业安全信息加载失败')
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

.security-card :deep(.el-card__body) {
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
  font-size: 18px;
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
