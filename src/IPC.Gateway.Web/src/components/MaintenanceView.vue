<template>
  <section class="view-stack maintenance-view">
    <section class="maintenance-hero">
      <div>
        <p class="eyebrow">Maintenance</p>
        <h3>{{ tx("安装升级") }}</h3>
        <span>{{ tx("安装包、升级包、离线升级和版本回滚集中管理") }}</span>
      </div>
      <div class="hero-actions">
        <a-tag
          :class="[
            'maintenance-status-tag',
            status?.enabled
              ? 'maintenance-status-tag--enabled'
              : 'maintenance-status-tag--disabled',
          ]"
          :color="tagColor(status?.enabled ? 'success' : 'info')"
        >
          {{ status?.enabled ? tx("已启用") : tx("未启用") }}
        </a-tag>
        <a-button :loading="snapshotLoading" @click="downloadSupportSnapshot">
          <template #icon>
            <span class="ui-icon"><component :is="Download" /></span>
          </template>
          {{ tx("售后快照") }}
        </a-button>
        <a-button :loading="loading" type="primary" @click="refresh">
          <template #icon>
            <span class="ui-icon"><component :is="Refresh" /></span>
          </template>
          {{ tx("刷新") }}
        </a-button>
      </div>
    </section>
    <section class="maintenance-metrics">
      <a-card class="metric-panel" size="small">
        <div class="metric-panel__icon">
          <span class="ui-icon"><Box /></span>
        </div>
        <div>
          <small>{{ tx("当前版本") }}</small>
          <strong>{{ status?.currentVersion || "-" }}</strong>
          <span>{{ status?.productId || "IPC.Gateway" }}</span>
        </div>
      </a-card>
      <a-card class="metric-panel" size="small">
        <div class="metric-panel__icon">
          <span class="ui-icon"><FolderChecked /></span>
        </div>
        <div>
          <small>{{ tx("升级包") }}</small>
          <strong>{{ status?.packages.length ?? 0 }}</strong>
          <span>{{ formatBytes(totalPackageBytes) }}</span>
        </div>
      </a-card>
      <a-card class="metric-panel" size="small">
        <div class="metric-panel__icon">
          <span class="ui-icon"><RefreshLeft /></span>
        </div>
        <div>
          <small>{{ tx("回滚点") }}</small>
          <strong>{{ status?.rollbackPoints.length ?? 0 }}</strong>
          <span>{{ tx(latestRollbackLabel) }}</span>
        </div>
      </a-card>
      <a-card class="metric-panel" size="small">
        <div class="metric-panel__icon">
          <span class="ui-icon"><Clock /></span>
        </div>
        <div>
          <small>{{ tx("待执行动作") }}</small>
          <strong>{{
            unfinishedAction
              ? tx(
                  unfinishedAction.actionType === "Rollback"
                    ? "版本回滚"
                    : "程序升级",
                )
              : tx("无")
          }}</strong>
          <span>{{
            tx(statusText(unfinishedAction?.status || "Ready"))
          }}</span>
        </div>
      </a-card>
    </section>
    <a-alert
      v-if="unfinishedAction"
      type="warning"
      show-icon
      :closable="false"
      class="pending-alert"
      :message="tx('已有待执行的离线动作')"
    >
      <template #description>
        <div class="pending-body">
          <span>{{ tx(pendingText) }}</span>
          <a-button
            v-if="status?.pendingAction?.status === 'Pending'"
            type="primary"
            :disabled="!canExecute || !status?.supportsApply"
            :loading="applying"
            @click="executeUpdate"
          >
            <template #icon><Refresh /></template>
            {{
              tx(
                status?.pendingAction?.actionType === "Rollback"
                  ? "执行回滚并重启"
                  : "执行升级并重启",
              )
            }}
          </a-button>
          <a-button size="small" @click="copyScriptPath">
            <template #icon>
              <span class="ui-icon"><component :is="DocumentCopy" /></span>
            </template>
            {{ tx("复制脚本路径") }}
          </a-button>
        </div>
        <p v-if="status?.pendingAction?.errorMessage" class="update-error">
          {{ tx(status.pendingAction.errorMessage) }}
        </p>
        <p v-if="!status?.supportsApply">
          {{ tx("自动执行需要网关以 Windows 服务方式运行。") }}
        </p>
        <p v-if="status?.pendingAction?.executionLogPath">
          {{ tx("执行日志") }}：{{ status.pendingAction.executionLogPath }}
        </p>
      </template>
    </a-alert>
    <a-card class="panel-card watchdog-panel" size="small">
      <template #title>
        <div class="card-header">
          <div class="detail-title">
            <span>{{ tx("看门狗与自恢复") }}</span>
            <small>{{
              tx(
                "监控网关运行时、采集调度、MQTT、历史库、规则引擎和 OPC UA Server",
              )
            }}</small>
          </div>
          <div class="card-actions">
            <a-tag :color="tagColor(watchdogStateType(watchdog?.state))">
              {{ tx(statusText(watchdogStateText(watchdog?.state))) }}
            </a-tag>
          </div>
        </div>
      </template>
      <section class="watchdog-summary">
        <div>
          <span>{{ tx("检查次数") }}</span>
          <strong>{{ watchdog?.checkCount ?? 0 }}</strong>
        </div>
        <div>
          <span>{{ tx("恢复成功") }}</span>
          <strong>{{ watchdog?.recoverySuccessCount ?? 0 }}</strong>
        </div>
        <div>
          <span>{{ tx("恢复失败") }}</span>
          <strong>{{ watchdog?.recoveryFailureCount ?? 0 }}</strong>
        </div>
        <div>
          <span>{{ tx("保护拦截") }}</span>
          <strong>{{ watchdog?.blockedRecoveryCount ?? 0 }}</strong>
        </div>
        <div>
          <span>{{ tx("最近检查") }}</span>
          <strong>{{
            watchdog?.lastCheckTime
              ? formatDateTime(watchdog.lastCheckTime)
              : "-"
          }}</strong>
        </div>
        <div>
          <span>{{ tx("最近恢复") }}</span>
          <strong>{{
            watchdog?.lastRecoveryTime
              ? formatDateTime(watchdog.lastRecoveryTime)
              : "-"
          }}</strong>
        </div>
      </section>
      <a-alert
        v-if="watchdog?.lastIssue"
        class="watchdog-issue"
        type="warning"
        show-icon
        :closable="false"
        :message="tx(watchdog.lastIssue)"
      />
      <GatewayTable
        resize-key="components.MaintenanceView.vue.0"
        :data-source="watchdog?.checks ?? []"
        :row-key="(_row: any, index: any) => index"
        size="small"
        :pagination="false"
        :columns="
          localizeColumns([
            { key: 'column-0', title: tx('状态'), width: '110' },
            {
              key: 'column-1',
              title: tx('检查项'),
              dataIndex: 'name',
              width: '140',
            },
            {
              key: 'column-2',
              title: tx('说明'),
              dataIndex: 'message',
              width: '260',
              ellipsis: true,
            },
            { key: 'column-3', title: tx('建议恢复'), width: '100' },
            { key: 'column-4', title: tx('时间'), width: '170' },
          ])
        "
        :scroll="{ x: 780, y: 220 }"
      >
        <template #bodyCell="tableCell">
          <template v-if="tableCell.column.key === 'column-1'">{{ tx(watchdogCheckLabel(tableCell.record.name)) }}</template>
          <template v-if="tableCell.column.key === 'column-2'">
            <span :title="tx(tableCell.record.message)">{{ tx(tableCell.record.message) }}</span>
          </template>
          <template v-if="tableCell.column.key === 'column-0'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              <a-tag :color="tagColor(watchdogStateType(row.state))">
                {{ tx(statusText(watchdogStateText(row.state))) }}
              </a-tag>
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-3'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              {{ row.recoveryRecommended ? tx("是") : tx("否") }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-4'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              {{ row.observedTime ? formatDateTime(row.observedTime) : "-" }}
            </template>
          </template>
        </template>
        <template #emptyText
          ><a-empty :description="tx(tx('暂无看门狗检查数据'))"
        /></template>
      </GatewayTable>
      <section class="restart-protection">
        <a-tag
          :color="
            tagColor(
              watchdog?.restartProtection?.recoveryBlocked
                ? 'danger'
                : 'success',
            )
          "
        >
          {{ tx("恢复保护：")
          }}{{
            watchdog?.restartProtection?.recoveryBlocked
              ? tx("拦截中")
              : tx("正常")
          }}
        </a-tag>
        <a-tag
          :color="
            tagColor(
              watchdog?.restartProtection?.hostRestartBlocked
                ? 'danger'
                : 'success',
            )
          "
        >
          {{ tx("宿主重启保护：")
          }}{{
            watchdog?.restartProtection?.hostRestartBlocked
              ? tx("拦截中")
              : tx("正常")
          }}
        </a-tag>
        <span
          >{{ tx("窗口内恢复") }}
          {{ watchdog?.restartProtection?.recentRecoveryCount ?? 0 }}
          {{ tx("次") }}</span
        >
        <span>
          {{ tx("宿主重启请求") }}
          {{ watchdog?.restartProtection?.recentHostRestartRequestCount ?? 0 }}
          {{ tx("次") }}
        </span>
      </section>
    </a-card>
    <WatchdogConfigPanel
      :config="watchdogConfig"
      :saving="savingWatchdogConfig"
      :can-save="canEditWatchdog"
      @save-watchdog-config="persistWatchdogConfig"
    />
    <section class="maintenance-grid backup-grid">
      <a-card class="panel-card" size="small">
        <template #title>
          <div class="card-header">
            <div class="detail-title">
              <span>{{ tx("项目备份恢复") }}</span>
              <small>{{ tx("导出或恢复项目、通信和历史配置") }}</small>
            </div>
            <div class="card-actions">
              <a-button :loading="backupLoading" @click="downloadProjectBackup">
                <template #icon>
                  <span class="ui-icon"><component :is="Download" /></span>
                </template>
                {{ tx("导出备份") }}
              </a-button>
            </div>
          </div>
        </template>
        <gateway-file-upload
          drag
          accept=".json"
          :show-upload-list="false"
          :disabled="restoreLoading"
          :on-change="handleRestoreBackup"
        >
          <span class="upload-icon ui-icon"><UploadFilled /></span>
          <div class="el-upload__text">
            {{ tx("拖入项目备份 JSON，或点击选择") }}
          </div>
        </gateway-file-upload>
      </a-card>
    </section>
    <section class="maintenance-grid">
      <a-card class="panel-card" size="small">
        <template #title>
          <div class="card-header">
            <div class="detail-title">
              <span>{{ tx("版本兼容矩阵") }}</span>
              <small>{{ tx("网关、配置 schema、插件和协议能力范围") }}</small>
            </div>
          </div>
        </template>
        <GatewayTable
          resize-key="components.MaintenanceView.vue.1"
          :data-source="compatibility?.items ?? []"
          :row-key="(_row: any, index: any) => index"
          size="small"
          :pagination="false"
          :columns="
            localizeColumns([
              {
                key: 'column-0',
                title: tx('能力'),
                dataIndex: 'capability',
                width: '180',
                ellipsis: true,
              },
              {
                key: 'column-1',
                title: tx('当前版本'),
                dataIndex: 'currentVersion',
                width: '130',
              },
              {
                key: 'column-2',
                title: tx('兼容范围'),
                dataIndex: 'compatibleRange',
                width: '150',
                ellipsis: true,
              },
              { key: 'column-3', title: tx('状态'), width: '110' },
            ])
          "
          :scroll="{ x: 570, y: 240 }"
        >
          <template #bodyCell="tableCell">
            <template v-if="tableCell.column.key === 'column-3'">
              <template
                v-for="{ row } in [
                  { row: tableCell.record, $index: tableCell.index },
                ]"
                :key="tableCell.index"
              >
                <a-tag
                  :color="
                    tagColor(row.status === 'Compatible' ? 'success' : 'danger')
                  "
                >
                  {{ tx(statusText(row.status)) }}
                </a-tag>
              </template>
            </template>
          </template>
          <template #emptyText
            ><a-empty :description="tx(tx('暂无兼容矩阵'))"
          /></template>
        </GatewayTable>
      </a-card>
      <a-card class="panel-card" size="small">
        <template #title>
          <div class="card-header">
            <div class="detail-title">
              <span>{{ tx("协议驱动签名") }}</span>
              <small>{{
                tx("内置和外部协议驱动的版本、摘要与签名状态")
              }}</small>
            </div>
          </div>
        </template>
        <GatewayTable
          resize-key="components.MaintenanceView.vue.2"
          :data-source="drivers"
          :row-key="(_row: any, index: any) => index"
          size="small"
          :pagination="false"
          :columns="
            localizeColumns([
              {
                key: 'column-0',
                title: tx('驱动'),
                dataIndex: 'driverId',
                width: '160',
                ellipsis: true,
              },
              {
                key: 'column-1',
                title: tx('协议'),
                dataIndex: 'protocol',
                width: '110',
              },
              {
                key: 'column-2',
                title: tx('能力'),
                width: '190',
                ellipsis: true,
              },
              { key: 'column-3', title: tx('签名'), width: '120' },
              {
                key: 'column-4',
                title: tx('签名方'),
                dataIndex: 'signer',
                width: '130',
                ellipsis: true,
              },
              { key: 'driver-actions', title: tx('操作'), width: 120, fixed: 'right' },
            ])
          "
          :scroll="{ x: 830, y: 240 }"
        >
          <template #bodyCell="tableCell">
            <template v-if="tableCell.column.key === 'driver-actions'">
              <a-button v-if="!tableCell.record.builtIn && canSignDriver && !['trusted', 'signed'].includes((tableCell.record.signatureStatus || '').toLowerCase())" type="link" size="small" @click="openDriverSigning(tableCell.record)">{{ tx('签名') }}</a-button>
              <span v-else>-</span>
            </template>
            <template v-if="tableCell.column.key === 'column-2'">
              <template
                v-for="{ row } in [
                  { row: tableCell.record, $index: tableCell.index },
                ]"
                :key="tableCell.index"
              >
                {{ tx(driverCapabilityText(row)) }}
              </template>
            </template>
            <template v-if="tableCell.column.key === 'column-3'">
              <template
                v-for="{ row } in [
                  { row: tableCell.record, $index: tableCell.index },
                ]"
                :key="tableCell.index"
              >
                <a-tag
                  :color="tagColor(driverSignatureType(row.signatureStatus))"
                >
                  {{ tx(driverSignatureText(row.signatureStatus)) }}
                </a-tag>
              </template>
            </template>
          </template>
          <template #emptyText
            ><a-empty :description="tx(tx('暂无协议驱动'))"
          /></template>
        </GatewayTable>
      </a-card>
    </section>
    <section class="maintenance-layout">
      <a-card
        class="panel-card upload-panel"
        size="small"
        @dragover.prevent
        @drop.prevent
      >
        <template #title>
          <div class="card-header">
            <div class="detail-title">
              <span>{{ tx("上传升级包") }}</span>
              <small>{{
                tx("支持安装包和升级包 zip，上传后先校验清单和 payload")
              }}</small>
            </div>
          </div>
        </template>
        <gateway-file-upload
          drag
          accept=".zip"
          :show-upload-list="false"
          :disabled="!canUpload || uploading"
          :on-change="handleUploadFile"
        >
          <span class="upload-icon ui-icon"><UploadFilled /></span>
          <div class="el-upload__text">{{ tx("拖入升级包，或点击选择") }}</div>
          <template #tip>
            <div class="el-upload__tip">
              {{
                formatMessage("文件上限 {0} MB", [
                  status?.maxPackageMegabytes || 1024,
                ])
              }}
              ·
              {{
                canUpload
                  ? tx(
                      "包内需要包含清单、payload 目录和文件摘要；生产环境建议启用发布签名",
                    )
                  : tx("当前用户没有上传升级包权限")
              }}
            </div>
          </template>
        </gateway-file-upload>
      </a-card>
      <a-card class="panel-card path-panel" size="small">
        <template #title>
          <div class="card-header">
            <div class="detail-title">
              <span>{{ tx("目录信息") }}</span>
              <small>{{ tx("离线升级脚本会在维护窗口使用这些路径") }}</small>
            </div>
          </div>
        </template>
        <a-descriptions :column="1" bordered size="small">
          <a-descriptions-item :label="tx('安装目录')">{{
            status?.installDirectory || "-"
          }}</a-descriptions-item>
          <a-descriptions-item :label="tx('升级目录')">{{
            status?.updateDirectory || "-"
          }}</a-descriptions-item>
          <a-descriptions-item :label="tx('离线脚本')">{{
            status?.offlineScriptPath || "-"
          }}</a-descriptions-item>
          <a-descriptions-item :label="tx('摘要策略')">
            {{
              status?.requirePackageFileDigests ? tx("必需") : tx("兼容旧包")
            }}
          </a-descriptions-item>
          <a-descriptions-item :label="tx('签名策略')">
            <span class="policy-tags">
              <a-tag
                :color="
                  tagColor(
                    status?.requirePackageSignature ? 'danger' : 'warning',
                  )
                "
              >
                {{ status?.requirePackageSignature ? tx("必需") : tx("可选") }}
              </a-tag>
              <a-tag
                :color="
                  tagColor(
                    status?.trustedPackagePublicKeyConfigured
                      ? 'success'
                      : 'info',
                  )
                "
              >
                {{
                  status?.trustedPackagePublicKeyConfigured
                    ? tx("公钥已配置")
                    : tx("公钥未配置")
                }}
              </a-tag>
            </span>
          </a-descriptions-item>
        </a-descriptions>
      </a-card>
    </section>
    <a-card class="panel-card" size="small">
      <template #title>
        <div class="card-header">
          <div class="detail-title">
            <span>{{ tx("升级包列表") }}</span>
            <small>{{
              tx("仅保留最新 1 个升级包，上传新包后自动清理旧包")
            }}</small>
          </div>
        </div>
      </template>
      <GatewayTable
        resize-key="components.MaintenanceView.vue.3"
        :loading="loading"
        :data-source="(status?.packages ?? []).slice(0, 1)"
        :row-key="(_row: any, index: any) => index"
        size="small"
        :pagination="false"
        :columns="
          localizeColumns([
            {
              key: 'column-0',
              title: tx('包编号'),
              dataIndex: 'packageId',
              width: '180',
              ellipsis: true,
            },
            {
              key: 'column-1',
              title: tx('类型'),
              dataIndex: 'packageType',
              width: '100',
            },
            {
              key: 'column-2',
              title: tx('版本'),
              dataIndex: 'version',
              width: '130',
            },
            { key: 'column-3', title: tx('可信状态'), width: '170' },
            { key: 'column-4', title: tx('大小'), width: '110' },
            { key: 'column-5', title: tx('上传时间'), width: '170' },
            {
              key: 'column-6',
              title: 'SHA256',
              dataIndex: 'sha256',
              width: '220',
              ellipsis: true,
            },
            {
              key: 'column-7',
              title: tx('操作'),
              width: '150',
              fixed: 'right',
            },
          ])
        "
        :scroll="{ x: 1230, y: 300 }"
      >
        <template #bodyCell="tableCell">
          <template v-if="tableCell.column.key === 'column-1'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              <a-tag
                :color="
                  tagColor(
                    row.packageType === 'Install' ? 'success' : 'primary',
                  )
                "
              >
                {{
                  row.packageType === "Install" ? tx("安装包") : tx("升级包")
                }}
              </a-tag>
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-3'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              <a-tooltip :title="tx(trustStatusDetail(row))" placement="top">
                <div class="trust-cell">
                  <a-tag :color="tagColor(trustStatusType(row.trustStatus))">
                    {{ tx(trustStatusText(row.trustStatus)) }}
                  </a-tag>
                  <small>{{ trustSubtext(row) }}</small>
                </div>
              </a-tooltip>
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-4'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              {{ formatBytes(row.sizeBytes) }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-5'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              {{ formatDateTime(row.uploadedTime) }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-7'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              <div class="row-actions">
                <a-tag v-if="isCurrentPackage(row)" color="success">
                  {{ tx("已升级") }}
                </a-tag>
                <a-button
                  v-else
                  size="small"
                  type="primary"
                  :disabled="!canPrepare || !canPrepareTrustedPackage(row)"
                  :loading="preparingId === row.packageId"
                  @click="preparePackage(row.packageId)"
                >
                  <template #icon>
                    <span class="ui-icon"><component :is="Switch" /></span>
                  </template>
                  {{ tx("准备升级") }}
                </a-button>
              </div>
            </template>
          </template>
        </template>
        <template #emptyText
          ><a-empty :description="tx(tx('暂无升级包'))"
        /></template>
      </GatewayTable>
    </a-card>
    <a-card class="panel-card" size="small">
      <template #title>
        <div class="card-header">
          <div class="detail-title">
            <span>{{ tx("版本回滚") }}</span>
            <small>{{ tx("升级前自动创建程序快照，仅保留最新 1 个回滚点") }}</small>
          </div>
        </div>
      </template>
      <GatewayTable
        resize-key="components.MaintenanceView.vue.4"
        :loading="loading"
        :data-source="(status?.rollbackPoints ?? []).slice(0, 2)"
        :row-key="(_row: any, index: any) => index"
        size="small"
        :pagination="false"
        :columns="
          localizeColumns([
            {
              key: 'column-0',
              title: tx('回滚点'),
              dataIndex: 'rollbackId',
              width: '190',
              ellipsis: true,
            },
            {
              key: 'column-1',
              title: tx('版本'),
              dataIndex: 'version',
              width: '130',
            },
            { key: 'column-2', title: tx('文件数'), width: '100' },
            { key: 'column-3', title: tx('大小'), width: '110' },
            { key: 'column-4', title: tx('创建时间'), width: '170' },
            {
              key: 'column-5',
              title: tx('快照目录'),
              dataIndex: 'directory',
              width: '260',
              ellipsis: true,
            },
            {
              key: 'column-6',
              title: tx('操作'),
              width: '140',
              fixed: 'right',
            },
          ])
        "
        :scroll="{ x: 1100, y: 280 }"
      >
        <template #bodyCell="tableCell">
          <template v-if="tableCell.column.key === 'column-2'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              {{ row.fileCount }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-3'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              {{ formatBytes(row.sizeBytes) }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-4'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              {{ formatDateTime(row.createdTime) }}
            </template>
          </template>
          <template v-if="tableCell.column.key === 'column-6'">
            <template
              v-for="{ row } in [
                { row: tableCell.record, $index: tableCell.index },
              ]"
              :key="tableCell.index"
            >
              <div class="row-actions">
                <a-button
                  size="small"
                  type="warning"
                  :disabled="!canRollback"
                  :loading="preparingId === row.rollbackId"
                  @click="prepareRollback(row.rollbackId)"
                >
                  <template #icon>
                    <span class="ui-icon"><component :is="RefreshLeft" /></span>
                  </template>
                  {{ tx("准备回滚") }}
                </a-button>
              </div>
            </template>
          </template>
        </template>
        <template #emptyText
          ><a-empty :description="tx(tx('暂无回滚点'))"
        /></template>
      </GatewayTable>
    </a-card>
    <a-modal
      :open="restartDialog"
      :title="tx('升级与服务重启')"
      centered
      :closable="!applying"
      :mask-closable="false"
      :keyboard="!applying"
      :footer="null"
      @cancel="restartDialog = false"
    >
      <a-spin :spinning="applying">
        <a-alert
          :type="restartError ? 'error' : applying ? 'info' : 'success'"
          :message="tx(restartMessage)"
          show-icon
        />
      </a-spin>
      <p class="update-dialog-tip">
        {{ tx("服务重启期间采集和推送会短暂暂停，页面会自动检查恢复状态。") }}
      </p>
      <a-button
        v-if="!applying && !restartError"
        type="primary"
        @click="reloadApplication"
        >{{ tx("刷新进入新版本") }}</a-button
      >
      <a-button v-if="!applying && restartError" @click="checkRestartAgain">{{
        tx("重新检查恢复状态")
      }}</a-button>
    </a-modal>
  </section>
  <DriverSigningModal :open="signingOpen" :driver="signingDriver" @close="signingOpen = false" @signed="refreshSignedDrivers" />
</template>

<script setup lang="ts">
import { watchdogCheckLabel } from "../i18n/runtime";
import { formatMessage } from "../i18n";
import { notify } from "../utils/ui";
import { tx, localizeColumns } from "../i18n";
import { statusText, driverSignatureText } from "../utils/display";
import { tagColor, confirmAction, type FileSelection } from "../utils/ui";
import { computed, onMounted, onBeforeUnmount, ref } from "vue";

import {
  ContainerOutlined as Box,
  ClockCircleOutlined as Clock,
  SnippetsOutlined as DocumentCopy,
  DownloadOutlined as Download,
  FolderOpenOutlined as FolderChecked,
  ReloadOutlined as Refresh,
  UndoOutlined as RefreshLeft,
  SwapOutlined as Switch,
  CloudUploadOutlined as UploadFilled,
} from "@ant-design/icons-vue";
import {
  exportProjectBackup,
  loadCompatibilityMatrix,
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
  applyPendingUpdate,
  type GatewayCompatibilityMatrix,
  type GatewayProtocolDriverInfo,
  type GatewayUpdatePackageRecord,
  type GatewaySupportSnapshot,
  type GatewayWatchdogConfig,
  type GatewayWatchdogStatus,
  type GatewayUpdateStatus,
} from "../api";
import DriverSigningModal from "./DriverSigningModal.vue";
import WatchdogConfigPanel from "./watchdog/WatchdogConfigPanel.vue";
import { formatDateTime } from "../utils/format";
import { PERMISSIONS, usePermissions } from "../utils/permissions";
import { isSamePackageVersion } from "../utils/packageVersion";
import { beginUpgradeUpload, updateUpgradeUpload, markUpgradeTransferred, finishUpgradeUpload } from '../utils/upgradeUploadState';

const { hasPermission } = usePermissions();
const loading = ref(false);
const uploading = ref(false);
const snapshotLoading = ref(false);
const backupLoading = ref(false);
const restoreLoading = ref(false);
const savingWatchdogConfig = ref(false);
const preparingId = ref("");
const status = ref<GatewayUpdateStatus | null>(null);
const unfinishedAction = computed(() => {
  const action = status.value?.pendingAction;
  return action && !['applied', 'completed'].includes(action.status.toLowerCase()) ? action : null;
});
const watchdog = ref<GatewayWatchdogStatus | null>(null);
const watchdogConfig = ref<GatewayWatchdogConfig | null>(null);
const compatibility = ref<GatewayCompatibilityMatrix | null>(null);
const drivers = ref<GatewayProtocolDriverInfo[]>([]);
const signingDriver = ref<GatewayProtocolDriverInfo | null>(null);
const signingOpen = ref(false);
const canSignDriver = computed(() => hasPermission(PERMISSIONS.securityCertificatesManage));
function openDriverSigning(driver: GatewayProtocolDriverInfo) { signingDriver.value = driver; signingOpen.value = true; }
async function refreshSignedDrivers() { drivers.value = await loadProtocolDrivers(); }
const restartDialog = ref(false);
const applying = ref(false);
const restartError = ref(false);
const restartMessage = ref("正在安排升级，请稍候…");
let restartTimer: ReturnType<typeof setTimeout> | undefined;
let restartController: AbortController | undefined;
let restartActionId = "";
let restartDeadline = 0;
let restartRequestError = "";
let disposed = false;
onBeforeUnmount(() => {
  disposed = true;
  clearTimeout(restartTimer);
  restartController?.abort();
});

function driverCapabilityText(driver: GatewayProtocolDriverInfo) {
  const capabilities = driver.capabilities;
  if (!capabilities) return "-";
  const features = [capabilities.supportsWrite ? "读写" : "只读"];
  if (capabilities.supportsSubscription) features.push("订阅");
  if (capabilities.supportsBatchRead)
    features.push(formatMessage("批读 {0}", [capabilities.maxBatchItems]));
  if (capabilities.supportsAddressValidation) features.push("地址校验");
  return `${capabilities.preferredReadMode} · ${features.join(" · ")}`;
}

const canUpload = computed(() =>
  hasPermission(PERMISSIONS.maintenancePackagesUpload),
);
const canPrepare = computed(() =>
  hasPermission(PERMISSIONS.maintenanceUpdatePrepare),
);
const canRollback = computed(() =>
  hasPermission(PERMISSIONS.maintenanceRollbackPrepare),
);
const canExecute = computed(() =>
  status.value?.pendingAction?.actionType === "Rollback"
    ? hasPermission(PERMISSIONS.maintenanceRollbackApply)
    : hasPermission(PERMISSIONS.maintenanceUpdateApply),
);
const canEditWatchdog = computed(() =>
  hasPermission(PERMISSIONS.maintenanceWatchdogEdit),
);
const totalPackageBytes = computed(() =>
  (status.value?.packages ?? []).reduce(
    (sum, item) => sum + (item.sizeBytes || 0),
    0,
  ),
);
const latestRollbackLabel = computed(() => {
  const latest = status.value?.rollbackPoints?.[0];
  return latest ? formatDateTime(latest.createdTime) : "无快照";
});
const pendingText = computed(() => {
  const action = status.value?.pendingAction;
  if (!action) return "";
  const type = action.actionType === "Rollback" ? "版本回滚" : "离线升级";
  return formatMessage("动作：{0} · 版本：{1} · 状态：{2}", [
    type,
    action.version || action.packageId || action.rollbackId,
    tx(statusText(action.status)),
  ]);
});

function isCurrentPackage(row: GatewayUpdatePackageRecord) {
  return isSamePackageVersion(row.version, status.value?.currentVersion);
}

function canPrepareTrustedPackage(row: GatewayUpdatePackageRecord) {
  if (
    ["Scheduled", "Applying"].includes(
      status.value?.pendingAction?.status || "",
    )
  )
    return false;
  if (status.value?.requirePackageFileDigests && !row.contentHashValid)
    return false;
  if (status.value?.requirePackageSignature && !row.signatureValid)
    return false;
  return true;
}

function trustStatusText(value: string | null | undefined) {
  switch ((value || "").toLowerCase()) {
    case "trusted":
      return "签名可信";
    case "contentverified":
      return "摘要通过";
    case "unverified":
      return "未验证";
    case "untrusted":
      return "不可信";
    default:
      return value || "-";
  }
}

function trustStatusType(value: string | null | undefined) {
  switch ((value || "").toLowerCase()) {
    case "trusted":
      return "success";
    case "contentverified":
      return "warning";
    case "untrusted":
      return "danger";
    default:
      return "info";
  }
}

function trustSubtext(row: GatewayUpdatePackageRecord) {
  if (row.signatureValid) return row.signer || "已签名";
  if (row.contentHashValid)
    return formatMessage("{0} 个文件", [row.fileCount || 0]);
  return "缺少摘要";
}

function trustStatusDetail(row: GatewayUpdatePackageRecord) {
  const detail = [row.trustMessage || trustStatusText(row.trustStatus)];
  if (row.fileCount) detail.push(formatMessage("文件：{0}", [row.fileCount]));
  if (row.signer) detail.push(formatMessage("发布者：{0}", [row.signer]));
  if (row.signedTime)
    detail.push(
      formatMessage("签名时间：{0}", [formatDateTime(row.signedTime)]),
    );
  return detail.join("；");
}

onMounted(() => refresh());

async function executeUpdate() {
  const action = status.value?.pendingAction;
  if (!action || applying.value) return;
  try {
    await confirmAction(
      "确认执行当前升级或回滚并重启服务？采集和推送将短暂暂停，请确保已备份数据库。",
      action.actionType === "Rollback" ? "执行回滚并重启" : "执行升级并重启",
      {
        confirmButtonText: "确认执行",
        cancelButtonText: "取消",
        type: "warning",
      },
    );
  } catch {
    return;
  }
  restartActionId = action.actionId;
  restartDialog.value = true;
  applying.value = true;
  restartError.value = false;
  restartMessage.value = "正在安排升级，请稍候…";
  restartRequestError = "";
  try {
    await applyPendingUpdate(action.actionId);
  } catch (error) {
    restartRequestError = error instanceof Error ? error.message : "";
  }
  restartDeadline = Date.now() + 180000;
  restartMessage.value = "正在替换程序并重启服务，请稍候…";
  restartTimer = setTimeout(pollRestart, 1500);
}
async function pollRestart() {
  if (disposed) return;
  const controller = new AbortController();
  restartController = controller;
  const timeout = setTimeout(() => controller.abort(), 5000);
  try {
    const data = await loadUpdateStatus(controller.signal);
    if (disposed) return;
    status.value = data;
    const action = data.pendingAction;
    if (action?.actionId !== restartActionId) {
      finishRestart("待执行动作已变化，请刷新页面检查。", true);
      return;
    }
    if (action.status === "Applied") {
      finishRestart("升级完成，服务已恢复。请刷新进入新版本。", false);
      return;
    }
    if (
      action.status === "Failed" ||
      (action.status === "Pending" && action.errorMessage)
    ) {
      finishRestart(action.errorMessage || "升级失败，请查看执行日志。", true);
      return;
    }
    if (action.status === "Pending") {
      finishRestart(
        restartRequestError || "升级尚未启动，请检查权限和待执行动作后重试。",
        true,
      );
      return;
    }
  } catch {
    /* Temporarily unreachable while the service restarts. */
  } finally {
    clearTimeout(timeout);
  }
  if (disposed) return;
  if (Date.now() >= restartDeadline) {
    finishRestart(
      "尚未确认服务恢复，请查看 Windows 服务状态和执行日志。",
      true,
    );
    return;
  }
  restartTimer = setTimeout(pollRestart, 1500);
}
function finishRestart(message: string, failed: boolean) {
  applying.value = false;
  restartError.value = failed;
  restartMessage.value = message;
}
function checkRestartAgain() {
  applying.value = true;
  restartError.value = false;
  restartDeadline = Date.now() + 180000;
  void pollRestart();
}
function reloadApplication() {
  window.location.reload();
}

async function refresh() {
  loading.value = true;
  try {
    const [
      updateData,
      watchdogData,
      watchdogConfigData,
      compatibilityData,
      driverData,
    ] = await Promise.all([
      loadUpdateStatus(),
      loadWatchdogStatus().catch((error) => {
        notify.warning(
          error instanceof Error ? error.message : "看门狗状态加载失败",
        );
        return null;
      }),
      loadWatchdogConfig().catch((error) => {
        notify.warning(
          error instanceof Error ? error.message : "看门狗配置加载失败",
        );
        return null;
      }),
      loadCompatibilityMatrix().catch((error) => {
        notify.warning(
          error instanceof Error ? error.message : "兼容矩阵加载失败",
        );
        return null;
      }),
      loadProtocolDrivers().catch((error) => {
        notify.warning(
          error instanceof Error ? error.message : "协议驱动状态加载失败",
        );
        return [];
      }),
    ]);
    status.value = updateData;
    if (
      ["Scheduled", "Applying"].includes(
        updateData.pendingAction?.status || "",
      ) &&
      !applying.value
    ) {
      restartActionId = updateData.pendingAction!.actionId;
      restartDialog.value = true;
      applying.value = true;
      restartError.value = false;
      restartMessage.value = "正在替换程序并重启服务，请稍候…";
      restartDeadline = Date.now() + 180000;
      restartTimer = setTimeout(pollRestart, 1500);
    }
    watchdog.value = watchdogData;
    watchdogConfig.value = watchdogConfigData;
    compatibility.value = compatibilityData;
    drivers.value = driverData;
  } catch (error) {
    notify.error(
      error instanceof Error ? error.message : "安装升级状态加载失败",
    );
  } finally {
    loading.value = false;
  }
}

async function persistWatchdogConfig(config: GatewayWatchdogConfig) {
  if (!canEditWatchdog.value) {
    notify.warning("当前用户没有保存看门狗配置权限");
    return;
  }
  savingWatchdogConfig.value = true;
  try {
    watchdogConfig.value = await saveWatchdogConfig(config);
    notify.success("看门狗配置已保存");
    await refresh();
  } catch (error) {
    notify.error(error instanceof Error ? error.message : "看门狗配置保存失败");
  } finally {
    savingWatchdogConfig.value = false;
  }
}

async function downloadSupportSnapshot() {
  snapshotLoading.value = true;
  try {
    const snapshot = await loadSupportSnapshot();
    downloadSnapshotJson(snapshot);
    notify.success("售后快照已生成");
  } catch (error) {
    notify.error(error instanceof Error ? error.message : "售后快照生成失败");
  } finally {
    snapshotLoading.value = false;
  }
}

async function downloadProjectBackup() {
  backupLoading.value = true;
  try {
    const blob = await exportProjectBackup();
    downloadBlob(
      blob,
      `ipc-gateway-project-${snapshotTimestamp(new Date().toISOString())}.json`,
    );
    notify.success("项目备份已生成");
  } catch (error) {
    notify.error(error instanceof Error ? error.message : "项目备份导出失败");
  } finally {
    backupLoading.value = false;
  }
}

async function handleRestoreBackup(uploadFile: FileSelection) {
  const raw = uploadFile.raw;
  if (!raw || uploading.value) return;
  beginUpgradeUpload(raw.name);
  try {
    await confirmAction(
      "恢复项目备份会覆盖当前项目和通信配置。确认继续？",
      "恢复项目备份",
      {
        confirmButtonText: "确认",
        cancelButtonText: "取消",
        type: "warning",
      },
    );
  } catch {
    return;
  }

  restoreLoading.value = true;
  try {
    const text = await raw.text();
    const result = await restoreProjectBackup(text);
    notify.success(
      formatMessage("项目已恢复：{0}", [
        result.projectName || result.projectId,
      ]),
    );
    await refresh();
  } catch (error) {
    notify.error(error instanceof Error ? error.message : "项目备份恢复失败");
  } finally {
    restoreLoading.value = false;
  }
}

async function handleUploadFile(uploadFile: FileSelection) {
  const raw = uploadFile.raw;
  if (!raw || uploading.value) return;
  beginUpgradeUpload(raw.name);
  uploading.value = true;
  try {
    const record = await uploadUpdatePackage(raw, updateUpgradeUpload, markUpgradeTransferred);
    notify.success(formatMessage("升级包已上传：{0}", [record.version]));
    await refresh();
  } catch (error) {
    notify.error(error instanceof Error ? error.message : "升级包上传失败");
  } finally {
    uploading.value = false;
    finishUpgradeUpload();
  }
}

async function preparePackage(packageId: string) {
  try {
    await confirmAction(
      "准备离线升级会创建当前版本快照，并生成待执行脚本。确认继续？",
      "准备离线升级",
      {
        confirmButtonText: "确认",
        cancelButtonText: "取消",
        type: "warning",
      },
    );
  } catch {
    return;
  }
  preparingId.value = packageId;
  try {
    const result = await prepareUpdatePackage(packageId);
    notify.success(result.message || "离线升级已准备");
    await refresh();
  } catch (error) {
    notify.error(error instanceof Error ? error.message : "离线升级准备失败");
  } finally {
    preparingId.value = "";
  }
}

async function prepareRollback(rollbackId: string) {
  try {
    await confirmAction(
      "准备版本回滚会覆盖当前待执行升级动作。确认继续？",
      "准备版本回滚",
      {
        confirmButtonText: "确认",
        cancelButtonText: "取消",
        type: "warning",
      },
    );
  } catch {
    return;
  }
  preparingId.value = rollbackId;
  try {
    const result = await prepareUpdateRollback(rollbackId);
    notify.success(result.message || "版本回滚已准备");
    await refresh();
  } catch (error) {
    notify.error(error instanceof Error ? error.message : "版本回滚准备失败");
  } finally {
    preparingId.value = "";
  }
}

async function copyScriptPath() {
  const path =
    status.value?.pendingAction?.scriptPath || status.value?.offlineScriptPath;
  if (!path) return;
  await navigator.clipboard.writeText(path);
  notify.success("脚本路径已复制");
}

function downloadSnapshotJson(snapshot: GatewaySupportSnapshot) {
  const payload = JSON.stringify(snapshot, null, 2);
  const blob = new Blob([payload], { type: "application/json;charset=utf-8" });
  downloadBlob(
    blob,
    `ipc-gateway-support-${snapshotTimestamp(snapshot.capturedTimeUtc)}.json`,
  );
}

function downloadBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function driverSignatureType(value: string | null | undefined) {
  switch ((value || "").toLowerCase()) {
    case "builtin":
    case "trusted":
      return "success";
    case "unsigned":
    case "unverified":
      return "warning";
    case "invalid":
    case "hashmismatch":
    case "missing":
    case "untrusted":
      return "danger";
    default:
      return "info";
  }
}

function snapshotTimestamp(value: string) {
  const source = value || new Date().toISOString();
  const digits = source.replace(/\D/g, "");
  return digits.slice(0, 14) || "snapshot";
}

function formatBytes(value: number) {
  if (!value) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  let size = value;
  let index = 0;
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024;
    index += 1;
  }
  return `${size.toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
}

function watchdogStateText(state: string | null | undefined) {
  switch ((state || "").toLowerCase()) {
    case "healthy":
      return "健康";
    case "degraded":
      return "降级";
    case "unhealthy":
      return "异常";
    case "recovering":
      return "恢复中";
    case "protected":
      return "保护中";
    case "disabled":
      return "未启用";
    default:
      return state || "-";
  }
}

function watchdogStateType(state: string | null | undefined) {
  switch ((state || "").toLowerCase()) {
    case "healthy":
      return "success";
    case "degraded":
    case "recovering":
      return "warning";
    case "unhealthy":
    case "protected":
      return "danger";
    default:
      return "info";
  }
}
</script>

<style scoped>
.update-dialog-tip {
  margin-block: 18px;
  color: var(--gateway-muted);
}
.update-error {
  color: var(--ui-color-danger);
}
.upload-panel :deep(.ant-upload-drag) {
  min-height: 160px;
}
.maintenance-view {
  gap: 18px;
  min-width: 0;
  grid-template-columns: minmax(0, 1fr);
}

.maintenance-grid.backup-grid {
  grid-template-columns: minmax(0, 1fr);
}

.maintenance-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  gap: 16px;
}

.maintenance-hero {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 20px 24px;
  border: 1px solid var(--ui-border-color-light);
  border-radius: 8px;
  background: linear-gradient(135deg, #0f766e, #1d4ed8);
  color: #fff;
}

.maintenance-hero h3 {
  margin: 3px 0 6px;
  font-size: calc(24px + 1pt);
  font-weight: 700;
}

.maintenance-hero > div:first-child > span,
.maintenance-hero .eyebrow {
  color: rgba(255, 255, 255, 0.82);
}

.row-actions,
.pending-body {
  display: flex;
  align-items: center;
  gap: 10px;
  white-space: nowrap;
}

.hero-actions {
  display: grid;
  grid-auto-flow: column;
  grid-auto-columns: minmax(116px, 1fr);
  align-items: center;
  justify-items: stretch;
  gap: 10px;
  flex-shrink: 0;
  margin-left: auto;
}
.hero-actions > :deep(.ant-btn),
.hero-actions > .maintenance-status-tag {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 32px;
  margin: 0;
  padding-inline: 12px;
  border-radius: 4px;
  font-size: calc(12px + 1pt);
  line-height: 1;
  white-space: nowrap;
}
.hero-actions > :deep(.ant-btn-default) {
  color: var(--gateway-text);
}
.hero-actions > :deep(.ant-btn-default:hover) {
  color: var(--gateway-primary);
}
.hero-actions :deep(.ant-btn .ui-icon),
.hero-actions :deep(.ant-btn .anticon) {
  color: inherit;
}

.maintenance-status-tag {
  font-weight: 500;
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

.metric-panel :deep(.ant-card-body) {
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
  font-size: calc(22px + 1pt);
}

.metric-panel small,
.metric-panel span {
  display: block;
  color: var(--ui-text-color-secondary);
}

.metric-panel strong {
  display: block;
  margin: 4px 0;
  font-size: calc(22px + 1pt);
  color: var(--ui-text-color-primary);
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
  border: 1px solid var(--ui-border-color-light);
  border-radius: 8px;
  background: var(--ui-fill-color-lighter);
}

.watchdog-summary span,
.restart-protection span {
  display: block;
  color: var(--ui-text-color-secondary);
  font-size: calc(12px + 1pt);
}

.watchdog-summary strong {
  display: block;
  margin-top: 4px;
  color: var(--ui-text-color-primary);
  font-size: calc(16px + 1pt);
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
  font-size: calc(42px + 1pt);
  color: var(--ui-color-primary);
}

.path-panel :deep(.ant-descriptions__label) {
  width: 110px;
  color: var(--ui-text-color-secondary);
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
  color: var(--ui-text-color-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 1180px) {
  .maintenance-metrics,
  .maintenance-grid,
  .maintenance-layout,
  .watchdog-summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 760px) {
  .hero-actions {
    width: 100%;
    margin-left: 0;
    grid-auto-flow: row;
    grid-template-columns: repeat(auto-fit, minmax(116px, 1fr));
  }
  .maintenance-hero,
  .pending-body {
    align-items: flex-start;
    flex-direction: column;
  }

  .maintenance-metrics,
  .maintenance-grid,
  .maintenance-layout,
  .watchdog-summary {
    grid-template-columns: 1fr;
  }
}
</style>
