<template>
  <section class="device-workspace" @click="closeContextMenu">
    <a-card size="small" :bordered="false" class="device-command-panel">
      <div class="device-metrics">
        <div>
          <span class="ui-icon"><Connection /></span>
          <span>
            {{ tx('在线设备') }}
            <strong>
              {{ workspaceMetrics.online }}
              <small>/ {{ devices.length }}</small>
            </strong>
          </span>
        </div>
        <div>
          <span class="ui-icon"><CollectionTag /></span>
          <span>
            {{ tx('采集标签') }}
            <strong>{{ workspaceMetrics.tags }}</strong>
          </span>
        </div>
        <div>
          <span class="ui-icon"><DataLine /></span>
          <span>
            {{ tx('点位健康率') }}
            <strong>{{ workspaceMetrics.health }}</strong>
          </span>
        </div>
        <div>
          <span class="ui-icon"><Timer /></span>
          <span>
            {{ tx('采集周期') }}
            <strong>
              {{ workspaceScanRate }}
              <small>ms</small>
            </strong>
          </span>
        </div>
      </div>
      <div class="device-toolbar">
        <a-input
          v-model:value="deviceFilterInput"
          allow-clear
          :placeholder="tx('按设备或通道名称筛选')"
          :aria-label="tx('筛选设备')"
        >
          <template #prefix>
            <span class="ui-icon"><Search /></span>
          </template>
        </a-input>
        <span class="device-toolbar__count">
          {{ filteredDevices.length }} {{ tx('台设备 ·') }} {{ filteredChannels.length }} {{ tx('个通道') }}
        </span>
        <div class="device-toolbar__actions">
          <a-button @click="emit('changed')">
            <template #icon>
              <span class="ui-icon"><Refresh /></span>
            </template>
            {{ tx('刷新') }}
          </a-button>
          <a-button v-if="canCreateDevice" @click="openCreateChannel">
            <template #icon>
              <span class="ui-icon"><SetUp /></span>
            </template>
            {{ tx('新增通道') }}
          </a-button>
          <a-button v-if="canCreateDevice" @click="openTemplateDrawer">
            <template #icon>
              <span class="ui-icon"><Collection /></span>
            </template>
            {{ tx('设备模板') }}
          </a-button>
          <a-button v-if="canCreateDevice" type="primary" @click="openCreate()">
            <template #icon>
              <span class="ui-icon"><Plus /></span>
            </template>
            {{ tx('新增设备') }}
          </a-button>
        </div>
      </div>
    </a-card>
    <div :class="['device-manager', { 'device-manager--with-value': valueDetailsVisible }]">
      <a-card size="small" :bordered="false" class="panel-card device-tree-panel">
        <template #title>
          <div class="card-header">
            <span>{{ tx('设备树') }}</span>
            <a-tag>{{ totalGroupCount }} {{ tx('组') }}</a-tag>
          </div>
        </template>
        <div ref="treeScroller" class="device-tree device-tree-virtual" @scroll="onTreeScroll">
          <div class="device-tree-virtual__spacer" :style="{ height: `${virtualTreeHeight}px` }">
            <div
              v-for="row in visibleTreeRows"
              :key="row.node.key"
              :class="['device-tree-row', { 'is-current': row.node.key === selectedNodeKey }]"
              :style="{ transform: `translateY(${row.top}px)` }"
              @click.stop="selectTreeNode(row.node)"
              @contextmenu.prevent.stop="openContextMenu($event, row.node)"
            >
              <button
                type="button"
                class="device-tree-row__toggle"
                :style="{ marginLeft: `${row.depth * 14}px` }"
                :disabled="!row.expandable"
                :aria-label="
                  formatMessage('{0} {1}', [
                    tx(expandedTreeKeys.has(row.node.key) ? '折叠' : '展开'),
                    row.node.type === 'root' ? tx(row.node.label) : row.node.label
                  ])
                "
                @click.stop="toggleTreeNode(row.node)"
              >
                <component
                  v-if="row.expandable"
                  :is="expandedTreeKeys.has(row.node.key) ? CaretDownOutlined : CaretRightOutlined"
                />
              </button>
              <span class="ui-icon"><component :is="treeNodeIcon(row.node)" /></span>
              <span class="device-tree-node__label">
                {{ row.node.type === 'root' ? tx(row.node.label) : row.node.label }}
              </span>
              <span class="device-tree-node__badges">
                <a-tag :color="tagTone(treeStatusType(row.node))" :title="treeCountTitle(row.node)">
                  {{ treeNodeMetric(row.node).count }}
                </a-tag>
              </span>
            </div>
          </div>
        </div>
      </a-card>
      <a-card size="small" :bordered="false" class="panel-card device-detail-panel">
        <template #title>
          <div class="card-header">
            <div class="detail-title">
              <span>{{ detailTitle }}</span>
              <small>{{ tx(detailSubtitle) }}</small>
            </div>
            <div class="card-actions">
              <a-button v-if="selectedNode.type === 'root' && canCreateDevice" type="primary" @click="openCreate()">
                {{ tx('新增设备') }}
              </a-button>
              <a-button
                v-if="selectedNode.type === 'channel' && selectedChannel && canCreateDevice"
                type="primary"
                @click="openCreate(selectedChannel)"
              >
                {{ tx('新增设备') }}
              </a-button>
              <a-button
                v-if="selectedDevice && canCreateTag"
                type="primary"
                @click="openCreateTag(selectedDevice, selectedGroup)"
              >
                <template #icon>
                  <span class="ui-icon"><Plus /></span>
                </template>
                {{ tx('新增标签') }}
              </a-button>
              <a-button v-if="selectedDevice" @click="exportSelectedTags">
                <template #icon>
                  <span class="ui-icon"><Download /></span>
                </template>
                {{ tx('导出点位') }}
              </a-button>
              <gateway-file-upload
                v-if="selectedDevice && canImportTags"
                accept=".csv"
                :show-upload-list="false"
                :on-change="handleImportTags"
              >
                <a-button>
                  <template #icon>
                    <span class="ui-icon"><Upload /></span>
                  </template>
                  {{ tx('导入点位') }}
                </a-button>
              </gateway-file-upload>
              <a-dropdown v-if="hasSelectionActions" :trigger="['click']">
                <a-button>{{ tx('更多操作') }}</a-button>
                <template #overlay>
                  <a-menu>
                    <a-menu-item
                      v-if="selectedChannel && selectedNode.type === 'channel' && canEditDevice"
                      key="edit-channel"
                      @click="openEditChannel(selectedChannel)"
                    >
                      {{ tx('编辑通道') }}
                    </a-menu-item>
                    <a-menu-item
                      v-if="selectedChannel && selectedNode.type === 'channel' && canDeleteDevice"
                      key="delete-channel"
                      danger
                      @click="removeChannel(selectedChannel)"
                    >
                      {{ tx('删除通道') }}
                    </a-menu-item>
                    <a-menu-item
                      v-if="selectedNode.type === 'device' && selectedDevice && canCreateGroup"
                      key="create-group"
                      @click="openCreateGroup(selectedDevice)"
                    >
                      {{ tx('新增分组') }}
                    </a-menu-item>
                    <a-menu-item
                      v-if="selectedGroup && canEditGroup"
                      key="edit-group"
                      @click="openEditGroup(selectedGroup)"
                    >
                      {{ tx('编辑分组') }}
                    </a-menu-item>
                    <a-menu-item
                      v-if="selectedGroup && canDeleteGroup"
                      key="delete-group"
                      danger
                      @click="removeGroup(selectedGroup)"
                    >
                      {{ tx('删除分组') }}
                    </a-menu-item>
                    <a-menu-item
                      v-if="selectedDevice && canEditDevice"
                      key="edit-device"
                      @click="openEdit(selectedDevice)"
                    >
                      {{ tx('编辑设备') }}
                    </a-menu-item>
                    <a-menu-item
                      v-if="selectedDevice && canDeleteDevice"
                      key="delete-device"
                      danger
                      @click="removeDevice(selectedDevice)"
                    >
                      {{ tx('删除设备') }}
                    </a-menu-item>
                  </a-menu>
                </template>
              </a-dropdown>
            </div>
          </div>
        </template>
        <div v-if="selectedNode.type === 'root'" class="device-overview">
          <AutoHeightTable
            :data-source="filteredDevices"
            :columns="localizeColumns(overviewColumns)"
            row-key="id"
            :scroll-x="1170"
          >
            <template #bodyCell="{ column, record }">
              <template v-if="column.key === 'channel'">{{ channelName(record.channelId) }}</template>
              <template v-else-if="column.key === 'protocol'">{{ tx(protocolLabel(record.protocol)) }}</template>
              <template v-else-if="column.key === 'connection'">{{ connectionSummary(record) }}</template>
              <template v-else-if="column.key === 'status'">
                <a-tag :color="tagTone(deviceStatusType(record))">
                  {{ tx(statusText(deviceRuntime(record)?.status || (record.enabled ? tx('未连接') : tx('停用')))) }}
                </a-tag>
              </template>
              <template v-else-if="column.key === 'tags'">{{ countDeviceTags(record) }}</template>
              <template v-else-if="column.key === 'health'">
                <a-tag :color="tagTone(deviceTagHealth(record).type)">{{ deviceTagHealth(record).label }}</a-tag>
              </template>
              <template v-else-if="column.key === 'actions'">
                <div class="table-actions">
                  <a-button v-if="canEditDevice" type="link" @click="openEdit(record)">{{ tx('编辑') }}</a-button>
                  <a-button v-if="canDeleteDevice" type="link" danger @click="removeDevice(record)">
                    {{ tx('删除') }}
                  </a-button>
                </div>
              </template>
            </template>
            <template #emptyText>
              <a-empty :description="tx(deviceEmptyText)">
                <a-button v-if="canCreateDevice" type="primary" @click="openCreate()">{{ tx('新增设备') }}</a-button>
              </a-empty>
            </template>
          </AutoHeightTable>
        </div>
        <div v-else-if="selectedNode.type === 'channel' && selectedChannel" class="channel-detail">
          <div class="device-summary channel-summary">
            <div>
              <span>{{ tx('协议驱动') }}</span>
              <strong>{{ tx(protocolLabel(selectedChannel.protocol)) }}</strong>
            </div>
            <div>
              <span>{{ tx('状态') }}</span>
              <a-tag :color="tagTone(selectedChannel.enabled ? 'success' : 'info')">
                {{ selectedChannel.enabled ? tx('启用') : tx('停用') }}
              </a-tag>
            </div>
            <div>
              <span>{{ tx('设备数量') }}</span>
              <strong>{{ selectedChannelDevices.length }}</strong>
            </div>
            <div>
              <span>{{ tx('并发上限') }}</span>
              <strong>{{ selectedChannel.maxConcurrentDevicePolls }}</strong>
            </div>
          </div>
          <AutoHeightTable
            :data-source="selectedChannelDevices"
            :columns="localizeColumns(channelColumns)"
            row-key="id"
            :scroll-x="820"
          >
            <template #bodyCell="{ column, record }">
              <template v-if="column.key === 'connection'">{{ connectionSummary(record) }}</template>
              <template v-else-if="column.key === 'status'">
                <a-tag :color="tagTone(deviceStatusType(record))">
                  {{ tx(statusText(deviceRuntime(record)?.status || (record.enabled ? tx('未连接') : tx('停用')))) }}
                </a-tag>
              </template>
              <template v-else-if="column.key === 'actions'">
                <a-button type="link" @click="selectTreeNode(findDeviceNode(record))">{{ tx('查看') }}</a-button>
              </template>
            </template>
            <template #emptyText><a-empty :description="tx('当前通道还没有设备')" /></template>
          </AutoHeightTable>
        </div>
        <div v-else class="device-detail">
          <div v-if="selectedDevice" class="device-tag-options">
            <span>
              {{ selectedTags.length }} {{ tx('个标签') }}
              <span v-if="selectedGroup">· {{ selectedDevice.name }} / {{ selectedGroup.name }}</span>
            </span>
            <a-radio-group v-model:value="showAllColumns" size="small" button-style="solid">
              <a-radio-button :value="false">{{ tx('常用列') }}</a-radio-button>
              <a-radio-button :value="true">{{ tx('全部列') }}</a-radio-button>
            </a-radio-group>
          </div>
          <div v-if="selectedDevice" class="device-summary">
            <div>
              <span>{{ tx('状态') }}</span>
              <a-tag :color="tagTone(deviceStatusType(selectedDevice))">
                {{
                  tx(
                    statusText(
                      deviceRuntime(selectedDevice)?.status || (selectedDevice.enabled ? tx('未连接') : tx('停用'))
                    )
                  )
                }}
              </a-tag>
            </div>
            <div>
              <span>{{ tx('点位健康率') }}</span>
              <strong>{{ deviceTagHealth(selectedDevice).label }}</strong>
            </div>
            <div>
              <span>{{ tx('通道状态') }}</span>
              <strong>{{ tx(statusText(deviceRuntime(selectedDevice)?.channelStatus || 'Unknown')) }}</strong>
            </div>
            <div>
              <span>{{ tx('隔离/恢复') }}</span>
              <strong>{{ tx(deviceRecoveryText(selectedDevice)) }}</strong>
            </div>
          </div>
          <div
            v-if="selectedTags.length"
            ref="tagScroller"
            :class="['device-tag-table', { 'device-tag-table--all': showAllColumns }]"
            :style="tagColumnStyle"
            @scroll="onTagScroll"
          >
            <div class="device-tag-table__head">
              <span v-for="column in tagColumnDefinitions" :key="column.key" :class="column.className">
                {{ tx(column.label) }}
                <ColumnResizeHandle
                  v-if="column.key !== 'actions'"
                  :label="tx(column.label)"
                  :width="tagColumnWidths[column.key]"
                  @resize="resizeTagColumn(column.key, $event)"
                />
              </span>
            </div>
            <div class="device-tag-table__body" :style="{ height: `${virtualTagHeight}px` }">
              <div
                v-for="row in visibleTagRows"
                :key="row.tag.id || `${row.index}-${row.tag.name}`"
                :class="[
                  'device-tag-table__row',
                  { 'is-value-active': valueDetailsVisible && valueTarget?.tag.id === row.tag.id }
                ]"
                :style="{ transform: `translateY(${row.top}px)` }"
              >
                <span class="strong-cell" :title="row.tag.name">{{ row.tag.name }}</span>
                <span :title="row.tag.address">{{ row.tag.address || '-' }}</span>
                <span class="tag-col-raw">
                  <button
                    class="tag-value-preview"
                    :title="formatMessage('{0}\n点击查看完整原始值', [tagRawValue(row.tag)])"
                    @click.stop="openValueDetails(row.tag, 'raw')"
                  >
                    {{ tagRawValue(row.tag) }}
                  </button>
                </span>
                <span>
                  <button
                    class="tag-value-preview"
                    :title="formatMessage('{0}\n点击查看完整当前值', [tagCurrentValue(row.tag)])"
                    @click.stop="openValueDetails(row.tag, 'current')"
                  >
                    {{ tagCurrentValue(row.tag) }}
                  </button>
                </span>
                <span class="tag-col-extra" :title="tx(tagCleaningText(row.tag))">
                  {{ tx(tagCleaningText(row.tag)) }}
                </span>
                <span>
                  <a-tag :color="tagTone(tagQualityType(row.tag))">{{ tx(tagQualityText(row.tag)) }}</a-tag>
                </span>
                <span :title="tx(tagUpdatedTime(row.tag))">{{ tagUpdatedTime(row.tag) }}</span>
                <span :title="tagLastError(row.tag)">{{ tagLastError(row.tag) }}</span>
                <span class="tag-col-type">{{ tx(dataTypeDisplayName(row.tag.dataType)) }}</span>
                <span class="tag-col-extra" :title="tx(tagScalingText(row.tag))">
                  {{ tx(tagScalingText(row.tag)) }}
                </span>
                <span class="tag-col-extra" :title="tx(row.tag.meterAddress)">{{ row.tag.meterAddress || '-' }}</span>
                <span class="tag-col-extra" :title="tx(row.tag.meterDataIdentifier)">
                  {{ row.tag.meterDataIdentifier || '-' }}
                </span>
                <span class="tag-col-extra">{{ row.tag.meterType || '-' }}</span>
                <span class="tag-col-extra">{{ row.tag.scanRateMs || selectedDevice?.defaultScanRateMs || 1000 }}</span>
                <span class="device-tag-table__actions table-actions">
                  <a-button
                    v-if="hasTagWritePermission"
                    size="small"
                    :disabled="!canWriteTag(row.tag)"
                    @click.stop="openWriteTag(row.tag)"
                    type="link"
                  >
                    {{ tx('写入') }}
                  </a-button>
                  <a-button v-if="canEditTag" size="small" @click.stop="openEditTag(row.tag)" type="link">
                    {{ tx('编辑') }}
                  </a-button>
                  <a-button v-if="canDeleteTag" size="small" danger @click.stop="removeTag(row.tag)" type="link">
                    {{ tx('删除') }}
                  </a-button>
                </span>
              </div>
            </div>
          </div>
          <a-empty v-else :description="tx(tagEmptyText)" class="device-tag-empty">
            <a-button
              v-if="selectedDevice && canCreateTag"
              type="primary"
              @click="openCreateTag(selectedDevice, selectedGroup)"
            >
              <template #icon>
                <span class="ui-icon"><Plus /></span>
              </template>
              {{ tx('新增标签') }}
            </a-button>
          </a-empty>
          <TablePager
            :total="selectedTags.length"
            :current="tagPage"
            :page-size="tagPageSize"
            @change="changeTagPage"
          />
        </div>
      </a-card>
      <TagValueDetailsDrawer
        v-model="valueDetailsVisible"
        :tag="valueTarget?.tag"
        :snapshot="valueDetailsSnapshot"
        :initial-kind="valueTarget?.kind"
      />
    </div>
    <a-modal
      centered
      destroy-on-close
      :mask-closable="false"
      v-model:open="templateDrawerVisible"
      :title="tx('设备模板')"
      width="520px"
    >
      <a-form
        class="device-form"
        :label-col="{ style: { width: '120px' } }"
        :wrapper-col="{ style: { flex: 1 } }"
        label-align="left"
      >
        <a-form-item :label="tx('模板')">
          <gateway-select v-model="templateForm.templateId" searchable :loading="templateLoading">
            <a-select-option
              v-for="item in deviceTemplates"
              :key="item.templateId"
              :label="`${item.name} (${item.protocol})`"
              :value="item.templateId"
            >
              {{ `${item.name} (${item.protocol})` }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('设备名称')" required>
          <gateway-input v-model="templateForm.deviceName" :placeholder="tx('例如：Line1 PLC')" />
        </a-form-item>
        <div class="form-grid">
          <a-form-item :label="tx('主机')">
            <gateway-input v-model="templateForm.host" placeholder="IP / endpoint" />
          </a-form-item>
          <a-form-item :label="tx('端口')">
            <gateway-number-input v-model="templateForm.port" :min="0" :max="65535" />
          </a-form-item>
        </div>
        <div class="form-grid">
          <a-form-item :label="tx('分组名称')">
            <gateway-input v-model="templateForm.groupName" placeholder="Process" />
          </a-form-item>
          <a-form-item :label="tx('采集周期(ms)')">
            <gateway-number-input v-model="templateForm.defaultScanRateMs" :min="100" :max="3600000" />
          </a-form-item>
        </div>
        <a-alert
          v-if="selectedTemplate"
          type="info"
          :closable="false"
          :message="
            formatMessage('{0}，{1} 组 / {2} 点位', [
              selectedTemplate.description,
              selectedTemplate.groupCount,
              selectedTemplate.tagCount
            ])
          "
        />
      </a-form>
      <template #footer>
        <div class="drawer-footer">
          <a-button @click="templateDrawerVisible = false">{{ tx('取消') }}</a-button>
          <a-button type="primary" :loading="applyingTemplate" @click="submitTemplate">{{ tx('应用模板') }}</a-button>
        </div>
      </template>
    </a-modal>
    <a-modal
      centered
      destroy-on-close
      :mask-closable="false"
      v-model:open="channelDrawerVisible"
      :title="tx(editingChannelId ? tx('编辑通道') : tx('新增通道'))"
      width="620px"
    >
      <ChannelForm
        v-if="channelForm"
        :model="channelForm"
        :driver-options="channelDriverOptions"
        :driver-locked="Boolean(editingChannelId && channelDeviceCount(editingChannelId))"
      />
      <template #footer>
        <div class="drawer-footer">
          <a-button @click="channelDrawerVisible = false">{{ tx('取消') }}</a-button>
          <a-button type="primary" :loading="channelSaving" @click="saveChannel">{{ tx('保存') }}</a-button>
        </div>
      </template>
    </a-modal>
    <a-modal
      centered
      destroy-on-close
      :mask-closable="false"
      class="gateway-wide-form-modal"
      v-model:open="drawerVisible"
      :title="tx(editingId ? tx('编辑设备') : tx('新增设备'))"
      width="1120px"
    >
      <a-form
        v-if="form"
        :model="form"
        class="device-form"
        :label-col="{ style: { width: '130px' } }"
        :wrapper-col="{ style: { flex: 1 } }"
        label-align="left"
      >
        <a-divider orientation="left">{{ tx('基础信息') }}</a-divider>
        <a-form-item :label="tx('设备名称')" required>
          <gateway-input v-model="form.name" :placeholder="tx('例如：锅炉房PLC')" />
        </a-form-item>
        <div class="form-grid">
          <a-form-item :label="tx('通道')" required>
            <gateway-input :model-value="channelName(form.channelId)" disabled />
          </a-form-item>
          <a-form-item :label="tx('协议驱动')">
            <gateway-input :model-value="protocolLabel(form.protocol)" disabled />
          </a-form-item>
          <a-form-item :label="tx('启用')"><a-switch v-model:checked="form.enabled" /></a-form-item>
        </div>
        <a-alert
          v-if="selectedProtocolCapabilityText"
          type="info"
          :closable="false"
          show-icon
          :message="tx(selectedProtocolCapabilityText)"
        />
        <div class="form-grid">
          <a-form-item :label="tx('采集周期(ms)')">
            <gateway-number-input v-model="form.defaultScanRateMs" :min="100" :max="3600000" />
          </a-form-item>
          <a-form-item :label="tx('失败重试(ms)')">
            <gateway-number-input v-model="form.failureRetryDelayMs" :min="100" :max="3600000" />
          </a-form-item>
          <a-form-item :label="tx('最大重试(ms)')">
            <gateway-number-input v-model="form.maxFailureRetryDelayMs" :min="100" :max="3600000" />
          </a-form-item>
        </div>
        <a-divider orientation="left">{{ tx('连接参数') }}</a-divider>
        <DeviceConnectionFields :device="form" :protocol="form.protocol" :parameters="selectedProtocolParameters" />
      </a-form>
      <template #footer>
        <div class="drawer-footer">
          <a-button @click="drawerVisible = false">{{ tx('取消') }}</a-button>
          <a-button v-if="canSaveCurrentDevice" type="primary" :loading="saving" @click="saveDevice">
            {{ tx('保存') }}
          </a-button>
        </div>
      </template>
    </a-modal>
    <a-modal
      centered
      destroy-on-close
      :mask-closable="false"
      v-model:open="groupDrawerVisible"
      :title="tx(editingGroupId ? tx('编辑分组') : tx('新增分组'))"
      width="480px"
    >
      <DeviceGroupForm v-if="groupForm" :model="groupForm" />
      <template #footer>
        <div class="drawer-footer">
          <a-button @click="groupDrawerVisible = false">{{ tx('取消') }}</a-button>
          <a-button v-if="canSaveCurrentGroup" type="primary" :loading="groupSaving" @click="saveGroup">
            {{ tx('保存') }}
          </a-button>
        </div>
      </template>
    </a-modal>
    <a-modal
      centered
      destroy-on-close
      :mask-closable="false"
      class="gateway-wide-form-modal"
      v-model:open="tagDrawerVisible"
      :title="tx(editingTagId ? tx('编辑标签') : tx('新增标签'))"
      width="1120px"
    >
      <DeviceTagForm
        ref="tagFormComponent"
        v-if="tagForm"
        :model="tagForm"
        :device-protocol="tagTargetDevice?.protocol || ''"
        :project="project"
      />
      <template #footer>
        <div class="drawer-footer">
          <a-button @click="tagDrawerVisible = false">{{ tx('取消') }}</a-button>
          <a-button v-if="canSaveCurrentTag" type="primary" :loading="tagSaving" @click="saveTag">
            {{ tx('保存') }}
          </a-button>
        </div>
      </template>
    </a-modal>
    <a-modal
      centered
      destroy-on-close
      :mask-closable="false"
      v-model:open="writeDrawerVisible"
      :title="tx('写入标签值')"
      width="460px"
    >
      <a-form
        v-if="writeTargetTag"
        class="device-form"
        :label-col="{ style: { width: '110px' } }"
        :wrapper-col="{ style: { flex: 1 } }"
        label-align="left"
      >
        <a-form-item :label="tx('设备')">
          <gateway-input :model-value="writeTargetDevice?.name || ''" disabled />
        </a-form-item>
        <a-form-item :label="tx('分组')">
          <gateway-input :model-value="writeTargetGroup?.name || '直属标签'" disabled />
        </a-form-item>
        <a-form-item :label="tx('标签')"><gateway-input :model-value="writeTargetTag.name" disabled /></a-form-item>
        <a-form-item :label="tx('数据类型')">
          <gateway-input :model-value="dataTypeDisplayName(writeTargetTag.dataType)" disabled />
        </a-form-item>
        <a-form-item :label="tx('当前值')">
          <gateway-input :model-value="tagCurrentValue(writeTargetTag)" disabled />
        </a-form-item>
        <a-form-item
          :label="tx('写入值')"
          required
          :help="tx(writeError)"
          :validate-status="writeError ? 'error' : undefined"
        >
          <gateway-input v-model="writeValueText" :placeholder="tx(writePlaceholder(writeTargetTag))" />
        </a-form-item>
      </a-form>
      <template #footer>
        <div class="drawer-footer">
          <a-button @click="writeDrawerVisible = false">{{ tx('取消') }}</a-button>
          <a-button
            v-if="writeTargetTag && canWriteTag(writeTargetTag)"
            type="primary"
            :loading="writeSaving"
            @click="submitWriteTag"
          >
            {{ tx('写入') }}
          </a-button>
        </div>
      </template>
    </a-modal>
    <div
      v-if="contextMenu.visible"
      class="tree-context-menu"
      :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
      @click.stop
      @contextmenu.prevent
    >
      <button v-for="action in contextActions" :key="action.key" type="button" @click="runContextAction(action.key)">
        <span class="ui-icon"><component :is="action.icon" /></span>
        <span>{{ tx(action.label) }}</span>
      </button>
    </div>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from '../i18n'
import { notify } from '../utils/ui'
import { tx, localizeColumns } from '../i18n'
import { statusText } from '../utils/display'
import { showBusy, confirmAction, type FileSelection } from '../utils/ui'
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'

import {
  CaretDownOutlined,
  CaretRightOutlined,
  BookOutlined as Collection,
  TagsOutlined as CollectionTag,
  ApartmentOutlined as Connection,
  LineChartOutlined as DataLine,
  FieldTimeOutlined as Timer,
  DeleteOutlined as Delete,
  DownloadOutlined as Download,
  EditOutlined as Edit,
  FolderOutlined as Folder,
  FolderAddOutlined as FolderAdd,
  PlusOutlined as Plus,
  ReloadOutlined as Refresh,
  SearchOutlined as Search,
  ControlOutlined as SetUp,
  UploadOutlined as Upload
} from '@ant-design/icons-vue'
import {
  applyDeviceTemplate,
  createChannel,
  createDeviceTag,
  createDevice,
  createGroup,
  createGroupTag,
  deleteChannel,
  deleteDevice,
  deleteGroup as deleteGroupRequest,
  deleteTag as deleteTagRequest,
  exportTagsCsv,
  importTagsCsv,
  loadDeviceTemplates,
  loadProtocolCatalog,
  updateChannel,
  updateGroup,
  updateTag,
  updateDevice,
  writeTag,
  type ChannelConfig,
  type DeviceConfig,
  type DeviceRuntimeStatus,
  type GatewayConnectionParameterDefinition,
  type GatewayDeviceTemplateSummary,
  type GatewayProtocolCatalogItem,
  type GroupConfig,
  type ProjectConfig,
  type TagConfig,
  type TagValueSnapshot
} from '../api'
import DeviceConnectionFields from './DeviceConnectionFields.vue'
import ChannelForm from './ChannelForm.vue'
import DeviceGroupForm from './DeviceGroupForm.vue'
import DeviceTagForm from './DeviceTagForm.vue'
import TagValueDetailsDrawer from './TagValueDetailsDrawer.vue'
import AutoHeightTable from './AutoHeightTable.vue'
import { deviceTreeMetric } from '../utils/deviceTreeCounts'
import ColumnResizeHandle from './controls/ColumnResizeHandle.vue'
import TablePager from './controls/TablePager.vue'
import { pageWindow } from '../utils/pagination'
import { columnWidth, readColumnWidths, saveColumnWidths, type ColumnWidths } from '../utils/columnWidths'
import {
  readDeviceViewState,
  saveDeviceViewState,
  resolveDeviceSelection,
  type DeviceViewState
} from '../utils/deviceViewState'
import {
  applyProtocolDefaults,
  cloneDevice,
  createDeviceDraft,
  normalizeDevice,
  protocolLabel
} from '../utils/deviceDefaults'
import {
  cloneGroup,
  cloneTag,
  createGroupDraft,
  createTagDraft,
  normalizeGroup,
  normalizeTag,
  dataTypeDisplayName
} from '../utils/tagDefaults'
import { formatDateTime } from '../utils/format'
import { PERMISSIONS, usePermissions } from '../utils/permissions'

type TreeNodeType = 'root' | 'channel' | 'device' | 'group'

interface DeviceTreeNode {
  key: string
  type: TreeNodeType
  label: string
  channel?: ChannelConfig
  device?: DeviceConfig
  group?: GroupConfig
  children?: DeviceTreeNode[]
}

interface ContextAction {
  key: string
  label: string
  icon: unknown
}

interface DeviceTreeRow {
  node: DeviceTreeNode
  depth: number
  top: number
  expandable: boolean
}

interface TagVirtualRow {
  tag: TagConfig
  index: number
  top: number
}

const props = defineProps<{
  stateScope: string
  project: ProjectConfig | null
  runtimeDevices: DeviceRuntimeStatus[]
  runtimeTags: TagValueSnapshot[]
}>()

const emit = defineEmits<{
  changed: []
  editingState: [editing: boolean]
}>()

const { hasPermission } = usePermissions()
const treeRowHeight = 34
const treeOverscan = 8
const tagRowHeight = 34
const tagHeaderHeight = 36
const tagOverscan = 10
const treeScroller = ref<HTMLElement | null>(null)
const treeViewportHeight = ref(treeRowHeight * 16)
const treeScrollTop = ref(0)
const tagScroller = ref<HTMLElement | null>(null)
const tagViewportHeight = ref(tagHeaderHeight + tagRowHeight * 12)
const tagScrollTop = ref(0)
const expandedTreeKeys = ref(new Set<string>(['root']))
const deviceFilterInput = ref('')
const deviceNameFilter = ref('')
const devicePage = ref(1)
const devicePageSize = ref(20)
const tagPage = ref(1)
const tagPageSize = ref(20)
const selectedNodeKey = ref('root')
let activeStateScope = ''
let restoringView = false
let selectionAncestors = ['root']
let pendingScroll: DeviceViewState | undefined
const showAllColumns = ref(false)
const tagColumnDefinitions = [
  { key: 'name', label: '标签' },
  { key: 'address', label: '地址' },
  { key: 'raw', label: '原始值', className: 'tag-col-raw' },
  { key: 'current', label: '当前值' },
  { key: 'cleaning', label: '清洗', className: 'tag-col-extra' },
  { key: 'quality', label: '质量' },
  { key: 'updated', label: '更新时间' },
  { key: 'error', label: '最近错误' },
  { key: 'type', label: '数据类型', className: 'tag-col-type' },
  { key: 'scaling', label: '缩放', className: 'tag-col-extra' },
  { key: 'meterAddress', label: '表地址', className: 'tag-col-extra' },
  { key: 'identifier', label: '数据标识', className: 'tag-col-extra' },
  { key: 'meterType', label: '表类型', className: 'tag-col-extra' },
  { key: 'interval', label: '周期(ms)', className: 'tag-col-extra' },
  { key: 'actions', label: '操作' }
]
const commonTagColumns = ['name', 'address', 'type', 'raw', 'current', 'quality', 'updated', 'error', 'actions']
const commonTagWidths = [140, 150, 125, 140, 140, 90, 150, 180, 190]
const allTagWidths = [140, 140, 120, 120, 130, 100, 150, 180, 125, 120, 130, 130, 110, 110, 190]
const tagColumnStorageKey = computed(
  () => 'ipc.gateway.tagColumns.' + props.stateScope + '.' + (showAllColumns.value ? 'all' : 'common')
)
const tagColumnWidths = ref<ColumnWidths>({})
watch(
  tagColumnStorageKey,
  (key) => {
    tagColumnWidths.value = readColumnWidths(key)
  },
  { immediate: true }
)
const tagColumnStyle = computed(() => {
  const keys = showAllColumns.value ? tagColumnDefinitions.map((column) => column.key) : commonTagColumns
  const defaults = showAllColumns.value ? allTagWidths : commonTagWidths
  const sizes = keys.map((key, index) => (key === 'actions' ? 190 : (tagColumnWidths.value[key] ?? defaults[index])))
  return {
    '--device-tag-columns': keys
      .map((key, index) =>
        !showAllColumns.value && ['raw', 'current'].includes(key) && tagColumnWidths.value[key] === undefined
          ? 'minmax(' + sizes[index] + 'px, 1fr)'
          : sizes[index] + 'px'
      )
      .join(' '),
    '--device-tag-min-width': sizes.reduce((sum, value) => sum + value, 0) + 'px'
  }
})
function resizeTagColumn(key: string, width: number) {
  if (key === 'actions') return
  tagColumnWidths.value = { ...tagColumnWidths.value, [key]: columnWidth(width) }
  saveColumnWidths(tagColumnStorageKey.value, tagColumnWidths.value)
}

const valueDetailsVisible = ref(false)
const valueTarget = ref<{
  tag: TagConfig
  channelId: string
  deviceId: string
  groupId: string
  kind: 'raw' | 'current'
}>()
const channelDrawerVisible = ref(false)
const channelSaving = ref(false)
const editingChannelId = ref('')
const channelForm = ref<ChannelConfig | null>(null)
const drawerVisible = ref(false)
const saving = ref(false)
const deletingDeviceId = ref('')
const editingId = ref('')
const form = ref<DeviceConfig | null>(null)
const protocolCatalog = ref<GatewayProtocolCatalogItem[]>([])
const protocolCatalogLoading = ref(false)
const templateDrawerVisible = ref(false)
const templateLoading = ref(false)
const applyingTemplate = ref(false)
const deviceTemplates = ref<GatewayDeviceTemplateSummary[]>([])
const templateForm = reactive({
  templateId: '',
  deviceName: '',
  host: '',
  port: 0,
  groupName: '',
  defaultScanRateMs: 1000
})
const groupDrawerVisible = ref(false)
const groupSaving = ref(false)
const editingGroupId = ref('')
const groupTargetDevice = ref<DeviceConfig | null>(null)
const groupForm = ref<GroupConfig | null>(null)
const tagDrawerVisible = ref(false)
const tagSaving = ref(false)
const editingTagId = ref('')
const tagTargetDevice = ref<DeviceConfig | null>(null)
const tagTargetGroup = ref<GroupConfig | null>(null)
const tagForm = ref<TagConfig | null>(null)
const tagFormComponent = ref<InstanceType<typeof DeviceTagForm> | null>(null)
const writeDrawerVisible = ref(false)
const writeSaving = ref(false)
const writeTargetDevice = ref<DeviceConfig | null>(null)
const writeTargetGroup = ref<GroupConfig | null>(null)
const writeTargetTag = ref<TagConfig | null>(null)
const writeValueText = ref('')
const writeError = ref('')
const contextMenu = reactive({
  visible: false,
  x: 0,
  y: 0,
  node: null as DeviceTreeNode | null
})
let filterTimer: number | undefined
let protocolCatalogLoadPromise: Promise<void> | null = null
let viewportObserver: ResizeObserver | undefined

const editingActive = computed(
  () =>
    channelDrawerVisible.value ||
    drawerVisible.value ||
    groupDrawerVisible.value ||
    tagDrawerVisible.value ||
    writeDrawerVisible.value ||
    channelSaving.value ||
    saving.value ||
    Boolean(deletingDeviceId.value) ||
    groupSaving.value ||
    tagSaving.value ||
    writeSaving.value
)

const canCreateDevice = computed(() => hasPermission(PERMISSIONS.devicesCreate))
const canEditDevice = computed(() => hasPermission(PERMISSIONS.devicesEdit))
const canDeleteDevice = computed(() => hasPermission(PERMISSIONS.devicesDelete))
const canCreateGroup = computed(() => hasPermission(PERMISSIONS.groupsCreate))
const canEditGroup = computed(() => hasPermission(PERMISSIONS.groupsEdit))
const canDeleteGroup = computed(() => hasPermission(PERMISSIONS.groupsDelete))
const canCreateTag = computed(() => hasPermission(PERMISSIONS.tagsCreate))
const canEditTag = computed(() => hasPermission(PERMISSIONS.tagsEdit))
const canDeleteTag = computed(() => hasPermission(PERMISSIONS.tagsDelete))
const hasTagWritePermission = computed(() => hasPermission(PERMISSIONS.tagsWrite))
const canSaveCurrentDevice = computed(() => (editingId.value ? canEditDevice.value : canCreateDevice.value))
const canSaveCurrentGroup = computed(() => (editingGroupId.value ? canEditGroup.value : canCreateGroup.value))
const canSaveCurrentTag = computed(() => (editingTagId.value ? canEditTag.value : canCreateTag.value))
const canImportTags = computed(() => canCreateTag.value || canEditTag.value)
const selectedTemplate = computed(
  () => deviceTemplates.value.find((item) => item.templateId === templateForm.templateId) ?? null
)
const availableProtocolOptions = computed(() => {
  return protocolCatalog.value.map((item) => ({
    label: item.displayName || protocolLabel(item.protocol),
    value: protocolSelectionValue(item),
    item
  }))
})
const channelDriverOptions = computed(() =>
  protocolCatalog.value.map((item) => ({
    label: item.displayName || protocolLabel(item.protocol),
    value: `${item.protocol}::${item.builtIn ? '' : item.driverId || ''}`,
    protocol: item.protocol,
    driverId: item.builtIn ? '' : item.driverId || ''
  }))
)
const selectedProtocolItem = computed(() => {
  if (!form.value) return null
  return findProtocolCatalogItem(form.value.protocol, form.value.connection?.driverId)
})
const selectedProtocolParameters = computed(() => selectedProtocolItem.value?.parameters ?? [])
const selectedProtocolCapabilityText = computed(() => {
  const capabilities = selectedProtocolItem.value?.capabilities
  if (!capabilities) return ''

  const features = [capabilities.supportsWrite ? '读写' : '只读']
  if (capabilities.supportsSubscription) features.push('订阅')
  if (capabilities.supportsBatchRead) features.push(formatMessage('批读上限 {0}', [capabilities.maxBatchItems]))
  if (capabilities.supportsAddressValidation) features.push('地址预校验')
  features.push(capabilities.requiresSerializedAccess ? '通道串行' : '可并发请求')
  return formatMessage('采集模式：{0}；{1}', [capabilities.preferredReadMode, features.map(tx).join(' · ')])
})
const selectedProtocolValue = computed(() => {
  if (!form.value) return ''
  const item = selectedProtocolItem.value
  return item ? protocolSelectionValue(item) : form.value.protocol
})

const channels = computed(() => (props.project?.channels ?? []).map((channel) => ({ ...channel })))
const channelMap = computed(() => new Map(channels.value.map((channel) => [normalizeKey(channel.id), channel])))
const devices = computed(() => (props.project?.devices ?? []).map(cloneDevice))
const runtimeMap = computed(() => {
  const map = new Map<string, DeviceRuntimeStatus>()
  for (const item of props.runtimeDevices ?? []) {
    map.set(deviceIdentityKey(item.channelId, item.deviceId), item)
  }
  return map
})

const runtimeTagMap = computed(() => {
  const map = new Map<string, TagValueSnapshot>()
  for (const item of props.runtimeTags ?? []) {
    map.set(tagIdentityKey(item.channelId, item.deviceId, item.groupId, item.tagId), item)
  }
  return map
})
const valueDetailsSnapshot = computed(() => {
  const target = valueTarget.value
  return target
    ? runtimeTagMap.value.get(tagIdentityKey(target.channelId, target.deviceId, target.groupId, target.tag.id))
    : undefined
})

const filteredDevices = computed(() => {
  const keyword = deviceNameFilter.value.trim().toLowerCase()
  if (!keyword) return devices.value
  return devices.value.filter((device) => {
    const channel = channelMap.value.get(normalizeKey(device.channelId))
    return device.name.toLowerCase().includes(keyword) || channel?.name.toLowerCase().includes(keyword)
  })
})

const filteredChannels = computed(() =>
  channels.value.filter((channel) => {
    const keyword = deviceNameFilter.value.trim().toLowerCase()
    if (!keyword) return true
    return (
      channel.name.toLowerCase().includes(keyword) ||
      filteredDevices.value.some((device) => normalizeKey(device.channelId) === normalizeKey(channel.id))
    )
  })
)

const treeData = computed<DeviceTreeNode[]>(() => [
  {
    key: 'root',
    type: 'root',
    label: '全部通道',
    children: filteredChannels.value.map((channel) => ({
      key: channelNodeKey(channel),
      type: 'channel',
      label: channel.name || '未命名通道',
      channel,
      children: filteredDevices.value
        .filter((device) => normalizeKey(device.channelId) === normalizeKey(channel.id))
        .map((device) => createDeviceTreeNode(channel, device))
    }))
  }
])

const totalGroupCount = computed(() =>
  filteredDevices.value.reduce((sum, device) => sum + (device.groups?.length ?? 0), 0)
)
const totalTagCount = computed(() => filteredDevices.value.reduce((sum, device) => sum + countDeviceTags(device), 0))

const selectedNode = computed<DeviceTreeNode>(
  () => findNode(treeData.value[0], selectedNodeKey.value) ?? treeData.value[0]
)
const selectedChannel = computed(() => selectedNode.value.channel)
const selectedDevice = computed(() =>
  selectedNode.value.type === 'device' || selectedNode.value.type === 'group' ? selectedNode.value.device : undefined
)
const selectedGroup = computed(() => (selectedNode.value.type === 'group' ? selectedNode.value.group : undefined))
const hasSelectionActions = computed(() => {
  if (selectedNode.value.type === 'channel') return canEditDevice.value || canDeleteDevice.value
  if (!selectedDevice.value) return false
  return (
    canEditDevice.value ||
    canDeleteDevice.value ||
    (selectedNode.value.type === 'device' && canCreateGroup.value) ||
    (!!selectedGroup.value && (canEditGroup.value || canDeleteGroup.value))
  )
})
const overviewColumns = [
  { title: '设备名称', dataIndex: 'name', key: 'name', width: 160, ellipsis: true },
  { title: '通道', key: 'channel', width: 150, ellipsis: true },
  { title: '协议', key: 'protocol', width: 130 },
  { title: '连接参数', key: 'connection', width: 210, ellipsis: true },
  { title: '状态', key: 'status', width: 100 },
  { title: '周期(ms)', dataIndex: 'defaultScanRateMs', key: 'scan', width: 90 },
  { title: '标签', key: 'tags', width: 70 },
  { title: '点位健康率', key: 'health', width: 150 },
  { title: '操作', key: 'actions', width: 110 }
]
const channelColumns = [
  { title: '设备名称', dataIndex: 'name', key: 'name', width: 180, ellipsis: true },
  { title: '连接参数', key: 'connection', width: 230, ellipsis: true },
  { title: '周期(ms)', dataIndex: 'defaultScanRateMs', key: 'scan', width: 110 },
  { title: '状态', key: 'status', width: 130 },
  { title: '操作', key: 'actions', width: 90 }
]
const workspaceMetrics = computed(() => {
  let readable = 0
  let good = 0
  for (const device of devices.value) {
    const health = deviceTagHealth(device)
    readable += health.total
    good += health.good
  }
  return {
    online: devices.value.filter((device) => deviceRuntime(device)?.isConnected).length,
    tags: devices.value.reduce((sum, device) => sum + countDeviceTags(device), 0),
    health: readable ? ((good / readable) * 100).toFixed(1) + '%' : '—'
  }
})
const workspaceScanRate = computed(() => {
  if (selectedDevice.value) return selectedDevice.value.defaultScanRateMs
  const rates = devices.value.filter((device) => device.enabled).map((device) => device.defaultScanRateMs)
  if (!rates.length) return '—'
  const min = Math.min(...rates),
    max = Math.max(...rates)
  return min === max ? min : min + '–' + max
})
function tagTone(type: string) {
  return (
    ({ success: 'success', warning: 'warning', danger: 'error', info: 'default' } as Record<string, string>)[type] ??
    'default'
  )
}
const selectedTags = computed<TagConfig[]>(() => {
  if (selectedGroup.value) return selectedGroup.value.tags ?? []
  if (selectedDevice.value) return selectedDevice.value.tags ?? []
  return []
})
const flatTreeRows = computed<DeviceTreeRow[]>(() => {
  const rows: Array<Omit<DeviceTreeRow, 'top'>> = []
  pushTreeNode(rows, treeData.value[0], 0)
  return rows.map((row, index) => ({ ...row, top: index * treeRowHeight }))
})
const virtualTreeHeight = computed(() => flatTreeRows.value.length * treeRowHeight)
const visibleTreeRows = computed(() => {
  const start = Math.max(0, Math.floor(treeScrollTop.value / treeRowHeight) - treeOverscan)
  const count = Math.ceil(treeViewportHeight.value / treeRowHeight) + treeOverscan * 2
  return flatTreeRows.value.slice(start, start + count)
})
const tagPageRange = computed(() => pageWindow(selectedTags.value.length, tagPage.value, tagPageSize.value))
const pagedTags = computed(() => selectedTags.value.slice(tagPageRange.value.start, tagPageRange.value.end))
function changeTagPage(page: number, size: number) {
  tagPageSize.value = size
  tagPage.value = page
  tagScrollTop.value = 0
  tagScroller.value?.scrollTo({ top: 0 })
  persistViewState()
}
watch([() => selectedTags.value.length, tagPageSize], () => {
  tagPage.value = tagPageRange.value.current
})
const virtualTagHeight = computed(() => pagedTags.value.length * tagRowHeight)
const visibleTagRows = computed<TagVirtualRow[]>(() => {
  const bodyScrollTop = Math.max(0, tagScrollTop.value - tagHeaderHeight)
  const start = Math.max(0, Math.floor(bodyScrollTop / tagRowHeight) - tagOverscan)
  const count = Math.ceil(Math.max(0, tagViewportHeight.value - tagHeaderHeight) / tagRowHeight) + tagOverscan * 2
  return pagedTags.value.slice(start, start + count).map((tag, offset) => {
    const index = start + offset
    return { tag, index, top: index * tagRowHeight }
  })
})
const pagedFilteredDevices = computed(() => paginate(filteredDevices.value, devicePage.value, devicePageSize.value))
const selectedChannelDevices = computed(() =>
  selectedChannel.value
    ? filteredDevices.value.filter(
        (device) => normalizeKey(device.channelId) === normalizeKey(selectedChannel.value?.id)
      )
    : []
)

const detailTitle = computed(() => {
  if (selectedNode.value.type === 'channel') return selectedChannel.value?.name || '通道详情'
  if (selectedNode.value.type === 'device') return selectedDevice.value?.name || '设备详情'
  if (selectedNode.value.type === 'group') return selectedGroup.value?.name || '分组标签'
  return '设备概览'
})

const detailSubtitle = computed(() => {
  if (selectedNode.value.type === 'channel')
    return formatMessage('{0} 台设备 / {1}', [
      selectedChannelDevices.value.length,
      protocolLabel(selectedChannel.value?.protocol || '')
    ])
  if (selectedNode.value.type === 'device') return '设备直属标签'
  if (selectedNode.value.type === 'group') return formatMessage('{0} / 分组标签', [selectedDevice.value?.name || ''])
  return '全部设备运行与配置摘要'
})

const deviceEmptyText = computed(() => (deviceNameFilter.value.trim() ? '没有匹配的设备' : '还没有设备'))
const tagEmptyText = computed(() => {
  if (selectedNode.value.type === 'group') return '当前分组还没有标签'
  if (selectedNode.value.type === 'device') return '当前设备还没有直属标签'
  return '请选择设备或分组查看标签'
})

watch(editingActive, (value) => emit('editingState', value), { immediate: true })
watch(deviceFilterInput, (value) => {
  if (restoringView) return
  if (filterTimer !== undefined) window.clearTimeout(filterTimer)
  filterTimer = window.setTimeout(() => {
    deviceNameFilter.value = value
    devicePage.value = 1
    treeScrollTop.value = 0
    treeScroller.value?.scrollTo({ top: 0 })
  }, 220)
})
watch(
  () => filteredDevices.value.length,
  () => {
    if (restoringView) return
    devicePage.value = clampPage(devicePage.value, filteredDevices.value.length, devicePageSize.value)
  }
)
watch(selectedNodeKey, (key) => {
  if (restoringView) return
  tagPage.value = 1
  selectionAncestors = (findNodePath(treeData.value[0], key) ?? []).map((node) => node.key)
  tagScrollTop.value = 0
  tagScroller.value?.scrollTo({ top: 0 })
  expandSelectedNodePath(key)
})
watch(
  [treeScroller, tagScroller],
  ([tree, tags], [previousTree, previousTags]) => {
    if (previousTree) viewportObserver?.unobserve(previousTree)
    if (previousTags) viewportObserver?.unobserve(previousTags)
    if (tree) viewportObserver?.observe(tree)
    if (tags) viewportObserver?.observe(tags)
  },
  { flush: 'post' }
)

onMounted(() => {
  window.addEventListener('pagehide', persistViewState)
  viewportObserver = new ResizeObserver((entries) => {
    for (const entry of entries) {
      if (entry.target === treeScroller.value) treeViewportHeight.value = entry.target.clientHeight
      if (entry.target === tagScroller.value) tagViewportHeight.value = entry.target.clientHeight
    }
    restoreScrollPositions()
  })
  if (treeScroller.value) viewportObserver.observe(treeScroller.value)
  if (tagScroller.value) viewportObserver.observe(tagScroller.value)
  void ensureProtocolCatalogLoaded()
})

onBeforeUnmount(() => {
  persistViewState()
  window.removeEventListener('pagehide', persistViewState)
  viewportObserver?.disconnect()
  if (filterTimer !== undefined) window.clearTimeout(filterTimer)
})

watch(treeData, () => {
  if (!restoringView && props.project) {
    selectedNodeKey.value = resolveDeviceSelection(
      selectedNodeKey.value,
      selectionAncestors,
      (key) => !!findNode(treeData.value[0], key)
    )
  }
})
watch(
  () => [props.stateScope, props.project?.projectId] as const,
  async ([scope]) => {
    if (!scope || !props.project || scope === activeStateScope) return
    persistViewState()
    activeStateScope = scope
    restoringView = true
    const saved = readDeviceViewState(scope)
    showAllColumns.value = saved?.allColumns ?? false
    if (filterTimer !== undefined) window.clearTimeout(filterTimer)
    deviceFilterInput.value = deviceNameFilter.value = saved?.filter ?? ''
    expandedTreeKeys.value = new Set(saved?.expanded ?? ['root'])
    devicePageSize.value = saved?.pageSize ?? 20
    tagPageSize.value = saved?.tagPageSize ?? 20
    tagPage.value = saved?.tagPage ?? 1
    devicePage.value = clampPage(saved?.page ?? 1, filteredDevices.value.length, devicePageSize.value)
    selectedNodeKey.value = resolveDeviceSelection(
      saved?.selected ?? 'root',
      saved?.ancestors ?? [],
      (key) => !!findNode(treeData.value[0], key)
    )
    selectionAncestors = (findNodePath(treeData.value[0], selectedNodeKey.value) ?? []).map((node) => node.key)
    expandSelectedNodePath(selectedNodeKey.value)
    pendingScroll = saved
    await nextTick()
    if (activeStateScope !== scope) return
    restoreScrollPositions()
    restoringView = false
  },
  { immediate: true, flush: 'post' }
)

function restoreScrollPositions() {
  if (!pendingScroll || !treeScroller.value?.clientHeight) return
  const saved = pendingScroll
  treeScroller.value.scrollTo({ top: saved.treeTop, left: saved.treeLeft })
  treeScrollTop.value = treeScroller.value.scrollTop
  if (selectedTags.value.length) {
    if (!tagScroller.value?.clientHeight) return
    tagScroller.value.scrollTo({ top: saved.tagTop, left: saved.tagLeft })
    tagScrollTop.value = tagScroller.value.scrollTop
  }
  pendingScroll = undefined
}

function persistViewState() {
  if (!activeStateScope || restoringView || !props.project) return
  saveDeviceViewState(activeStateScope, {
    selected: selectedNodeKey.value,
    ancestors: selectionAncestors,
    expanded: [...expandedTreeKeys.value],
    filter: deviceFilterInput.value,
    page: devicePage.value,
    pageSize: devicePageSize.value,
    treeTop: treeScroller.value?.scrollTop ?? treeScrollTop.value,
    treeLeft: treeScroller.value?.scrollLeft ?? 0,
    tagTop: tagScroller.value?.scrollTop ?? tagScrollTop.value,
    tagLeft: tagScroller.value?.scrollLeft ?? 0,
    allColumns: showAllColumns.value,
    tagPage: tagPageRange.value.current,
    tagPageSize: tagPageSize.value
  })
}

function openValueDetails(tag: TagConfig, kind: 'raw' | 'current') {
  if (!selectedDevice.value) return
  valueTarget.value = {
    tag: { ...tag },
    channelId: selectedDevice.value.channelId,
    deviceId: selectedDevice.value.id,
    groupId: selectedGroup.value?.id ?? '',
    kind
  }
  valueDetailsVisible.value = true
}

const contextActions = computed<ContextAction[]>(() => {
  const node = contextMenu.node
  if (!node || node.type === 'root') {
    return canCreateDevice.value
      ? [
          { key: 'add-channel', label: '新增通道', icon: SetUp },
          { key: 'add-device', label: '新增设备', icon: Plus }
        ]
      : []
  }
  if (node.type === 'channel') {
    return [
      canCreateDevice.value ? { key: 'add-device', label: '新增设备', icon: Plus } : null,
      canEditDevice.value ? { key: 'edit-channel', label: '编辑通道', icon: Edit } : null,
      canDeleteDevice.value ? { key: 'delete-channel', label: '删除通道', icon: Delete } : null
    ].filter(Boolean) as ContextAction[]
  }
  if (node.type === 'device') {
    return [
      canEditDevice.value ? { key: 'edit-device', label: '编辑设备', icon: Edit } : null,
      canDeleteDevice.value ? { key: 'delete-device', label: '删除设备', icon: Delete } : null,
      canCreateGroup.value ? { key: 'add-group', label: '新增分组', icon: FolderAdd } : null,
      canCreateTag.value ? { key: 'add-tag', label: '新增标签', icon: CollectionTag } : null
    ].filter(Boolean) as ContextAction[]
  }
  return [
    canEditGroup.value ? { key: 'edit-group', label: '编辑分组', icon: Edit } : null,
    canDeleteGroup.value ? { key: 'delete-group', label: '删除分组', icon: Delete } : null,
    canCreateTag.value ? { key: 'add-tag', label: '新增标签', icon: CollectionTag } : null
  ].filter(Boolean) as ContextAction[]
})

function selectTreeNode(node: DeviceTreeNode) {
  selectedNodeKey.value = node.key
  expandSelectedNodePath(node.key)
}

function openContextMenu(event: MouseEvent, node: DeviceTreeNode) {
  event.preventDefault()
  event.stopPropagation()
  selectTreeNode(node)
  contextMenu.node = node
  if (contextActions.value.length === 0) {
    contextMenu.visible = false
    return
  }
  contextMenu.x = event.clientX
  contextMenu.y = event.clientY
  contextMenu.visible = true
}

function closeContextMenu() {
  contextMenu.visible = false
}

function runContextAction(action: string) {
  const node = contextMenu.node
  closeContextMenu()
  if (!node) return

  if (action === 'add-channel') void openCreateChannel()
  else if (action === 'add-device') void openCreate(node.channel)
  else if (action === 'edit-channel' && node.channel) void openEditChannel(node.channel)
  else if (action === 'delete-channel' && node.channel) void removeChannel(node.channel)
  else if (action === 'edit-device' && node.device) void openEdit(node.device)
  else if (action === 'delete-device' && node.device) removeDevice(node.device)
  else if (action === 'add-group' && node.device) openCreateGroup(node.device)
  else if (action === 'edit-group' && node.group) openEditGroup(node.group, node.device)
  else if (action === 'delete-group' && node.group) removeGroup(node.group, node.device)
  else if (action === 'add-tag' && node.device)
    openCreateTag(node.device, node.type === 'group' ? node.group : undefined)
}

async function ensureProtocolCatalogLoaded() {
  if (protocolCatalog.value.length) return
  if (!protocolCatalogLoadPromise) {
    protocolCatalogLoading.value = true
    protocolCatalogLoadPromise = loadProtocolCatalog()
      .then((items) => {
        protocolCatalog.value = items
      })
      .catch((error) => {
        notify.warning(error instanceof Error ? error.message : '协议目录加载失败')
      })
      .finally(() => {
        protocolCatalogLoading.value = false
        protocolCatalogLoadPromise = null
      })
  }
  await protocolCatalogLoadPromise
}

function protocolSelectionValue(item: GatewayProtocolCatalogItem) {
  if (!item.builtIn && item.driverId) return `driver:${item.driverId}`
  return item.protocol
}

function findProtocolSelection(value: string) {
  return availableProtocolOptions.value.find((option) => option.value === value)?.item ?? null
}

function findProtocolCatalogItem(protocol: string, driverId = '') {
  const normalizedDriverId = normalizeKey(driverId)
  if (normalizedDriverId) {
    const driverMatch = protocolCatalog.value.find((item) => normalizeKey(item.driverId) === normalizedDriverId)
    if (driverMatch) return driverMatch
  }
  return protocolCatalog.value.find((item) => item.protocol === protocol) ?? null
}

function applyCatalogProtocol(device: DeviceConfig, item: GatewayProtocolCatalogItem) {
  applyProtocolDefaults(device, item.protocol)
  device.connection.protocol = item.protocol
  device.connection.driverId = item.builtIn ? '' : item.driverId
  applyConnectionParameterDefaults(device.connection, item.parameters)
}

function applyConnectionParameterDefaults(
  connection: DeviceConfig['connection'],
  parameters: GatewayConnectionParameterDefinition[]
) {
  for (const parameter of parameters) {
    if (!parameter.key || parameter.defaultValue === '') continue
    applyConnectionParameterDefault(connection, parameter)
  }
}

function applyConnectionParameterDefault(
  connection: DeviceConfig['connection'],
  parameter: GatewayConnectionParameterDefinition
) {
  const value = parseParameterDefault(parameter)
  if (parameter.key.startsWith('driverOptions.')) {
    const optionKey = parameter.key.slice('driverOptions.'.length)
    const options = readDriverOptionsJson(connection.driverOptionsJson)
    if (shouldApplyParameterDefault(options[optionKey], parameter)) {
      options[optionKey] = value
      connection.driverOptionsJson = JSON.stringify(options)
    }
    return
  }

  const connectionRecord = connection as unknown as Record<string, unknown>
  if (shouldApplyParameterDefault(connectionRecord[parameter.key], parameter)) {
    connectionRecord[parameter.key] = value
  }
}

function shouldApplyParameterDefault(currentValue: unknown, parameter: GatewayConnectionParameterDefinition) {
  if (currentValue === undefined || currentValue === null || currentValue === '') return true
  return typeof currentValue === 'number' && currentValue === 0 && parameter.defaultValue !== '0'
}

function parseParameterDefault(parameter: GatewayConnectionParameterDefinition) {
  const type = (parameter.parameterType || '').toLowerCase()
  if (type === 'number') {
    const value = Number(parameter.defaultValue)
    return Number.isFinite(value) ? value : undefined
  }
  if (type === 'switch') {
    const value = parameter.defaultValue.toLowerCase()
    return value === 'true' || value === '1'
  }
  return parameter.defaultValue
}

function readDriverOptionsJson(json: string) {
  try {
    const parsed = JSON.parse(json || '{}')
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

function treeNodeIcon(node: DeviceTreeNode) {
  if (node.type === 'channel') return SetUp
  if (node.type === 'device') return Connection
  if (node.type === 'group') return Folder
  return Collection
}

function pushTreeNode(rows: Array<Omit<DeviceTreeRow, 'top'>>, node: DeviceTreeNode, depth: number) {
  const expandable = (node.children?.length ?? 0) > 0
  rows.push({ node, depth, expandable })
  if (!expandable || !expandedTreeKeys.value.has(node.key)) return
  for (const child of node.children ?? []) pushTreeNode(rows, child, depth + 1)
}

function onTreeScroll() {
  treeScrollTop.value = treeScroller.value?.scrollTop ?? 0
}

function onTagScroll() {
  tagScrollTop.value = tagScroller.value?.scrollTop ?? 0
}

function toggleTreeNode(node: DeviceTreeNode) {
  if (!node.children?.length) return
  const next = new Set(expandedTreeKeys.value)
  if (next.has(node.key)) next.delete(node.key)
  else next.add(node.key)
  next.add('root')
  expandedTreeKeys.value = next
}

function expandSelectedNodePath(key: string) {
  const next = new Set(expandedTreeKeys.value)
  const path = findNodePath(treeData.value[0], key)
  for (const node of path?.slice(0, -1) ?? []) next.add(node.key)
  next.add('root')
  expandedTreeKeys.value = next
}

function paginate<T>(items: T[], page: number, pageSize: number) {
  const size = Math.max(1, pageSize)
  const current = clampPage(page, items.length, size)
  const start = (current - 1) * size
  return items.slice(start, start + size)
}

function clampPage(page: number, total: number, pageSize: number) {
  const maxPage = Math.max(1, Math.ceil(total / Math.max(1, pageSize)))
  return Math.min(Math.max(1, page), maxPage)
}

function treeStatusType(node: DeviceTreeNode) {
  if (node.type === 'root')
    return filteredDevices.value.some((device) => deviceRuntime(device)?.isConnected) ? 'success' : 'info'
  if (node.type === 'channel') {
    if (!node.channel?.enabled) return 'info'
    const devices = node.children?.map((child) => child.device).filter(Boolean) as DeviceConfig[]
    if (devices.some((device) => String(deviceRuntime(device)?.status || '').toLowerCase() === 'degraded'))
      return 'warning'
    return devices.some((device) => deviceRuntime(device)?.isConnected) ? 'success' : 'warning'
  }
  if (node.type === 'group') return node.group?.enabled ? 'success' : 'info'
  return deviceStatusType(node.device)
}

function deviceStatusType(device?: DeviceConfig) {
  const runtime = deviceRuntime(device)
  if (String(runtime?.status || '').toLowerCase() === 'degraded') return 'warning'
  return runtime?.isConnected ? 'success' : device?.enabled ? 'warning' : 'info'
}

function treeStatusText(node: DeviceTreeNode) {
  if (node.type === 'root') return formatMessage('{0} 设备', [filteredDevices.value.length])
  if (node.type === 'channel')
    return node.channel?.enabled ? formatMessage('{0} 设备', [node.children?.length ?? 0]) : '停用'
  if (node.type === 'group') return node.group?.enabled ? '启用' : '停用'
  return deviceRuntime(node.device)?.status || (node.device?.enabled ? '未连接' : '停用')
}

function treeNodeMetric(node: DeviceTreeNode) {
  return deviceTreeMetric(node, channels.value, devices.value)
}
function treeCountTitle(node: DeviceTreeNode) {
  const metric = treeNodeMetric(node)
  return formatMessage(
    metric.kind === 'channels' ? '{0} 个通道' : metric.kind === 'devices' ? '{0} 台设备' : '{0} 个标签',
    [metric.count]
  )
}

function findNode(node: DeviceTreeNode | undefined, key: string): DeviceTreeNode | undefined {
  if (!node) return undefined
  if (node.key === key) return node
  for (const child of node.children ?? []) {
    const found = findNode(child, key)
    if (found) return found
  }
  return undefined
}

function findNodePath(
  node: DeviceTreeNode | undefined,
  key: string,
  path: DeviceTreeNode[] = []
): DeviceTreeNode[] | undefined {
  if (!node) return undefined
  const nextPath = [...path, node]
  if (node.key === key) return nextPath
  for (const child of node.children ?? []) {
    const found = findNodePath(child, key, nextPath)
    if (found) return found
  }
  return undefined
}

function createDeviceTreeNode(channel: ChannelConfig, device: DeviceConfig): DeviceTreeNode {
  return {
    key: deviceNodeKey(device),
    type: 'device',
    label: device.name || '未命名设备',
    channel,
    device,
    children: (device.groups ?? []).map((group) => ({
      key: groupNodeKey(device, group),
      type: 'group',
      label: group.name || '未命名分组',
      channel,
      device,
      group
    }))
  }
}

function channelNodeKey(channel: ChannelConfig) {
  return `channel:${channel.id || channel.name}`
}

function deviceNodeKey(device: DeviceConfig) {
  return `device:${device.channelId}:${device.id}`
}

function findDeviceNode(device: DeviceConfig) {
  return findNode(treeData.value[0], deviceNodeKey(device)) ?? treeData.value[0]
}

function channelName(channelId: string) {
  return channelMap.value.get(normalizeKey(channelId))?.name || '-'
}

function channelDeviceCount(channelId: string) {
  return devices.value.filter((device) => normalizeKey(device.channelId) === normalizeKey(channelId)).length
}

function groupNodeKey(device: DeviceConfig, group: GroupConfig) {
  return `group:${device.channelId}:${device.id}:${group.id}`
}

function deviceRuntime(device?: DeviceConfig) {
  if (!device) return undefined
  return runtimeMap.value.get(deviceIdentityKey(device.channelId, device.id))
}

function deviceRecoveryText(device?: DeviceConfig) {
  const runtime = deviceRuntime(device)
  if (!runtime) return '-'
  if (runtime.isIsolated) return formatMessage('已隔离 · {0}', [tx(statusText(runtime.recoveryState || 'Waiting'))])
  if (runtime.recoveryState && runtime.recoveryState !== 'Idle') return statusText(runtime.recoveryState)
  return '正常'
}

function tagSnapshot(tag: TagConfig) {
  return findTagSnapshot(selectedDevice.value, selectedGroup.value, tag)
}

function findTagSnapshot(device: DeviceConfig | undefined, group: GroupConfig | null | undefined, tag: TagConfig) {
  return runtimeTagMap.value.get(tagIdentityKey(device?.channelId, device?.id, group?.id, tag.id))
}

function deviceTagHealth(device?: DeviceConfig) {
  if (!device || !device.enabled) return { good: 0, total: 0, rate: 0, type: 'info' as const, label: '-' }

  let good = 0
  let total = 0
  forEachReadableDeviceTag(device, (tag, group) => {
    total += 1
    const snapshot = findTagSnapshot(device, group, tag)
    if (normalizeKey(snapshot?.quality) === 'good') good += 1
  })

  const rate = total <= 0 ? 0 : Number(((good / total) * 100).toFixed(1))
  return {
    good,
    total,
    rate,
    type: healthRateType(rate, total),
    label: total <= 0 ? '-' : `${rate.toFixed(1)}% (${good}/${total})`
  }
}

function forEachReadableDeviceTag(device: DeviceConfig, visit: (tag: TagConfig, group?: GroupConfig) => void) {
  for (const tag of device.tags ?? []) {
    if (isReadableEnabledTag(tag)) visit(tag)
  }

  for (const group of device.groups ?? []) {
    if (!group?.enabled) continue
    for (const tag of group.tags ?? []) {
      if (isReadableEnabledTag(tag)) visit(tag, group)
    }
  }
}

function isReadableEnabledTag(tag: TagConfig) {
  return !!tag?.enabled && normalizeKey(tag.accessMode) !== 'writeonly'
}

function healthRateType(rate: number, total: number) {
  if (total <= 0) return 'info'
  if (rate >= 99.95) return 'success'
  if (rate >= 80) return 'warning'
  return 'danger'
}

function tagCurrentValue(tag: TagConfig) {
  const snapshot = tagSnapshot(tag)
  const value = snapshot?.valueText || snapshot?.rawValueText || ''
  if (!value) return '-'
  const unit = snapshot?.unit || tag.unit
  return unit ? `${value} ${unit}` : value
}

function tagRawValue(tag: TagConfig) {
  const snapshot = tagSnapshot(tag)
  return snapshot?.rawValueText || '-'
}

function tagCleaningText(tag: TagConfig) {
  const snapshot = tagSnapshot(tag)
  if (snapshot?.cleaningApplied) {
    return snapshot.cleaningMessage || snapshot.cleaningAction || '已清洗'
  }
  if (tag.cleaning?.enabled) return '已启用'
  return '-'
}

function tagQualityType(tag: TagConfig) {
  const quality = normalizeKey(tagSnapshot(tag)?.quality)
  if (quality === 'good') return 'success'
  if (quality === 'unknown' || !quality) return 'info'
  if (quality === 'filtered') return 'info'
  if (quality === 'outofrange' || quality === 'spike') return 'warning'
  if (quality.includes('connect')) return 'warning'
  return 'danger'
}

function tagQualityText(tag: TagConfig) {
  const snapshot = tagSnapshot(tag)
  if (snapshot?.isStaticValidationError) return '静态校验失败'
  if (snapshot?.isTagIsolated) return '已隔离'
  const quality = snapshot?.quality || 'Unknown'
  const labels: Record<string, string> = {
    good: '正常',
    unknown: '无数据',
    bad: '异常',
    readerror: '读取失败',
    notconnected: '未连接',
    disabled: '停用',
    outofrange: '越界',
    filtered: '已过滤',
    spike: '毛刺'
  }
  return labels[normalizeKey(quality)] ?? quality
}

function tagUpdatedTime(tag: TagConfig) {
  return formatDateTime(tagSnapshot(tag)?.timestamp)
}

function tagLastError(tag: TagConfig) {
  return tagSnapshot(tag)?.errorMessage || '-'
}

function tagScalingText(tag: TagConfig) {
  if (!tag.scaling?.enabled) return '-'
  const multiplier = Number(tag.scaling.multiplier)
  const offset = Number(tag.scaling.offset)
  const multiplierText = Number.isFinite(multiplier) ? `x${multiplier}` : 'x?'
  if (!Number.isFinite(offset) || offset === 0) return multiplierText
  return offset > 0 ? `${multiplierText}+${offset}` : `${multiplierText}${offset}`
}

function canWriteTag(tag: TagConfig) {
  return hasTagWritePermission.value && normalizeKey(tag.accessMode) !== 'readonly'
}

function openWriteTag(tag: TagConfig) {
  if (!selectedDevice.value || !canWriteTag(tag)) return
  writeTargetDevice.value = selectedDevice.value
  writeTargetGroup.value = selectedGroup.value ?? null
  writeTargetTag.value = tag
  writeValueText.value = ''
  writeError.value = ''
  writeDrawerVisible.value = true
}

function writePlaceholder(tag: TagConfig) {
  const type = normalizeKey(tag.dataType)
  if (type.includes('array')) return '多个值用英文逗号分隔'
  if (type === 'bool' || type === 'coil' || type === 'discreteinput') return 'true / false 或 1 / 0'
  if (type === 'string') return '输入字符串'
  return '输入数字'
}

async function submitWriteTag() {
  if (!writeTargetDevice.value || !writeTargetTag.value) return
  const validation = validateWriteValue(writeTargetTag.value, writeValueText.value)
  writeError.value = validation
  if (validation) return

  writeSaving.value = true
  try {
    const result = await writeTag({
      channelId: writeTargetDevice.value.channelId,
      deviceId: writeTargetDevice.value.id,
      groupId: writeTargetGroup.value?.id || '',
      tagId: writeTargetTag.value.id,
      deviceName: writeTargetDevice.value.name,
      groupName: writeTargetGroup.value?.name || '',
      tagName: writeTargetTag.value.name,
      dataType: writeTargetTag.value.dataType,
      valueText: normalizeKey(writeTargetTag.value.dataType) === 'string' ? writeValueText.value : writeValueText.value.trim(),
      timeoutMilliseconds: 10000
    })
    if (!result.success) {
      writeError.value = result.errorMessage || '写入失败'
      notify.error(writeError.value)
      return
    }
    if (normalizeKey(result.quality) === 'readerror' || result.errorMessage) {
      notify.warning('写入已确认，但当前值回读失败，请稍后刷新确认当前值')
    } else {
      notify.success(
        result.currentValueText ? formatMessage('写入成功，当前值 {0}', [result.currentValueText]) : '写入成功'
      )
    }
    writeDrawerVisible.value = false
    emit('changed')
  } catch (error) {
    writeError.value = error instanceof Error ? error.message : '写入失败'
    notify.error(writeError.value)
  } finally {
    writeSaving.value = false
  }
}

function validateWriteValue(tag: TagConfig, valueText: string) {
  const text = valueText.trim()
  if (!canWriteTag(tag)) return '当前标签没有写入权限'

  const type = normalizeKey(tag.dataType)
  if (type === 'string') return ''
  if (!text) return '请输入写入值'
  const values = type.includes('array')
    ? text
        .split(',')
        .map((item) => item.trim())
        .filter(Boolean)
    : [text]

  if (type.includes('array') && values.length === 0) return '请输入至少一个数组值'

  for (const value of values) {
    const error = validateScalarValue(type.replace('array', ''), value)
    if (error) return error
  }
  return ''
}

function validateScalarValue(type: string, value: string) {
  if (type === 'bool' || type === 'coil' || type === 'discreteinput') {
    return /^(true|false|1|0)$/i.test(value) ? '' : '布尔值只能是 true、false、1 或 0'
  }
  if (type === 'string') return ''
  if (type === 'float' || type === 'double') {
    return Number.isFinite(Number(value)) ? '' : '请输入合法数字'
  }

  if (!/^-?\d+$/.test(value)) return '请输入整数'
  if (type === 'int64' || type === 'uint64') {
    const number = BigInt(value)
    const min = type === 'int64' ? -9223372036854775808n : 0n
    const max = type === 'int64' ? 9223372036854775807n : 18446744073709551615n
    return number >= min && number <= max ? '' : formatMessage('数值范围应为 {0} 到 {1}', [min, max])
  }

  const number = Number(value)
  if (!Number.isSafeInteger(number)) return '整数超出安全范围'
  const ranges: Record<string, [number, number]> = {
    byte: [0, 255],
    int8: [-128, 127],
    uint8: [0, 255],
    int16: [-32768, 32767],
    uint16: [0, 65535],
    int32: [-2147483648, 2147483647],
    uint32: [0, 4294967295]
  }
  const range = ranges[type]
  if (range && (number < range[0] || number > range[1]))
    return formatMessage('数值范围应为 {0} 到 {1}', [range[0], range[1]])
  return ''
}

function deviceIdentityKey(channelId: string | null | undefined, deviceId: string | null | undefined) {
  return [channelId, deviceId].map(normalizeKey).join('/')
}

function tagIdentityKey(
  channelId: string | null | undefined,
  deviceId: string | null | undefined,
  groupId: string | null | undefined,
  tagId: string | null | undefined
) {
  return [channelId, deviceId, groupId, tagId].map(normalizeKey).join('/')
}

function normalizeKey(value: string | null | undefined) {
  return (value ?? '').trim().toLowerCase()
}

async function openTemplateDrawer() {
  if (!canCreateDevice.value) {
    notify.warning('当前用户没有新增设备权限')
    return
  }
  templateDrawerVisible.value = true
  if (deviceTemplates.value.length) return

  templateLoading.value = true
  try {
    deviceTemplates.value = await loadDeviceTemplates()
    templateForm.templateId = deviceTemplates.value[0]?.templateId || ''
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '设备模板加载失败')
  } finally {
    templateLoading.value = false
  }
}

async function submitTemplate() {
  if (!templateForm.templateId) {
    notify.warning('请选择设备模板')
    return
  }
  if (!templateForm.deviceName.trim()) {
    notify.warning('请输入设备名称')
    return
  }

  applyingTemplate.value = true
  try {
    const result = await applyDeviceTemplate(templateForm.templateId, {
      deviceName: templateForm.deviceName,
      host: templateForm.host,
      port: templateForm.port,
      groupName: templateForm.groupName,
      defaultScanRateMs: templateForm.defaultScanRateMs
    })
    notify.success(formatMessage('模板已应用：新增 {0} 个点位', [result.addedTagCount]))
    selectedNodeKey.value = deviceNodeKey(result.device)
    templateDrawerVisible.value = false
    emit('changed')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '设备模板应用失败')
  } finally {
    applyingTemplate.value = false
  }
}

async function exportSelectedTags() {
  if (!selectedDevice.value) return
  try {
    const blob = await exportTagsCsv(selectedDevice.value.channelId, selectedDevice.value.id)
    downloadBlob(blob, `ipc-gateway-tags-${selectedDevice.value.name || 'device'}-${snapshotTimestamp()}.csv`)
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '点位导出失败')
  }
}

async function handleImportTags(uploadFile: FileSelection) {
  const raw = uploadFile.raw
  if (!raw || !selectedDevice.value) return
  try {
    const text = await raw.text()
    const result = await importTagsCsv(text, selectedDevice.value.channelId, selectedDevice.value.id)
    notify.success(formatMessage('点位导入完成：新增 {0}，更新 {1}', [result.addedCount, result.updatedCount]))
    if (result.warnings?.length) notify.warning(result.warnings[0])
    emit('changed')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '点位导入失败')
  }
}

async function openCreateChannel() {
  if (!canCreateDevice.value) return
  await ensureProtocolCatalogLoaded()
  const driver =
    channelDriverOptions.value.find((item) => item.protocol === 'ModbusTcp') ?? channelDriverOptions.value[0]
  if (!driver) {
    notify.warning('没有可用的协议驱动')
    return
  }
  editingChannelId.value = ''
  channelForm.value = {
    id: '',
    name: formatMessage('{0} 通道', [protocolLabel(driver.protocol)]),
    enabled: true,
    protocol: driver.protocol,
    driverId: driver.driverId,
    maxConcurrentDevicePolls: 64,
    schedulingWeight: 1
  }
  channelDrawerVisible.value = true
}

async function openEditChannel(channel: ChannelConfig) {
  if (!canEditDevice.value) return
  await ensureProtocolCatalogLoaded()
  editingChannelId.value = channel.id
  channelForm.value = { ...channel }
  channelDrawerVisible.value = true
}

async function saveChannel() {
  if (!channelForm.value) return
  const payload = { ...channelForm.value, name: channelForm.value.name.trim() }
  if (!payload.name) {
    notify.warning('请输入通道名称')
    return
  }

  channelSaving.value = true
  try {
    const saved = editingChannelId.value
      ? await updateChannel(editingChannelId.value, payload)
      : await createChannel(payload)
    selectedNodeKey.value = channelNodeKey(saved)
    channelDrawerVisible.value = false
    notify.success(editingChannelId.value ? '通道已更新' : '通道已新增')
    emit('changed')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '通道保存失败')
  } finally {
    channelSaving.value = false
  }
}

async function removeChannel(channel: ChannelConfig) {
  try {
    await confirmAction(
      formatMessage('确认删除通道“{0}”？该通道下的 {1} 台设备及其分组、标签将一并删除。', [
        channel.name,
        channelDeviceCount(channel.id)
      ]),
      '删除通道',
      { type: 'warning' }
    )
    await deleteChannel(channel.id)
    selectedNodeKey.value = 'root'
    notify.success('通道已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') notify.error(error instanceof Error ? error.message : '通道删除失败')
  }
}

async function openCreate(preferredChannel?: ChannelConfig) {
  if (!canCreateDevice.value) {
    notify.warning('当前用户没有新增设备权限')
    return
  }
  await ensureProtocolCatalogLoaded()
  const channel =
    preferredChannel ?? selectedChannel.value ?? (channels.value.length === 1 ? channels.value[0] : undefined)
  if (!channel) {
    if (channels.value.length) {
      notify.info('请先在设备树中选择所属通道，再新增设备')
      return
    }
    notify.info('请先新增通道')
    await openCreateChannel()
    return
  }
  editingId.value = ''
  form.value = createDeviceDraft()
  applyChannelToDevice(form.value, channel)
  drawerVisible.value = true
}

async function openEdit(device: DeviceConfig) {
  if (!canEditDevice.value) {
    notify.warning('当前用户没有编辑设备权限')
    return
  }
  await ensureProtocolCatalogLoaded()
  editingId.value = device.id
  form.value = cloneDevice(device)
  const catalogItem = findProtocolCatalogItem(form.value.protocol, form.value.connection?.driverId)
  if (catalogItem) applyConnectionParameterDefaults(form.value.connection, catalogItem.parameters)
  drawerVisible.value = true
}

function changeProtocol(protocol: string) {
  if (!form.value) return
  const catalogItem = findProtocolSelection(protocol)
  if (catalogItem) {
    applyCatalogProtocol(form.value, catalogItem)
    return
  }
  applyProtocolDefaults(form.value, protocol)
}

function applyChannelToDevice(device: DeviceConfig, channel: ChannelConfig) {
  const sameDriver =
    device.protocol === channel.protocol && normalizeKey(device.connection?.driverId) === normalizeKey(channel.driverId)
  if (!sameDriver) {
    const catalogItem = findProtocolCatalogItem(channel.protocol, channel.driverId)
    if (catalogItem) applyCatalogProtocol(device, catalogItem)
    else applyProtocolDefaults(device, channel.protocol)
  }
  device.channelId = channel.id
  device.protocol = channel.protocol
  device.connection.protocol = channel.protocol
  device.connection.driverId = channel.driverId || ''
}

async function saveDevice() {
  if (!form.value) return
  if (!canSaveCurrentDevice.value) {
    notify.warning('当前用户没有保存设备权限')
    return
  }
  const payload = normalizeDevice(cloneDevice(form.value))
  const channel = channelMap.value.get(normalizeKey(payload.channelId))
  if (!channel) {
    notify.warning('请选择通道')
    return
  }
  applyChannelToDevice(payload, channel)
  payload.connection.protocol = payload.protocol
  if (!payload.name.trim()) {
    notify.warning('请输入设备名称')
    return
  }

  saving.value = true
  try {
    if (editingId.value) {
      await updateDevice(editingId.value, payload)
      notify.success('设备已更新')
      selectedNodeKey.value = deviceNodeKey(payload)
    } else {
      const created = await createDevice(payload)
      notify.success('设备已新增')
      selectedNodeKey.value = deviceNodeKey(created)
    }
    drawerVisible.value = false
    emit('changed')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    saving.value = false
  }
}

async function removeDevice(device: DeviceConfig) {
  if (deletingDeviceId.value) return
  if (!canDeleteDevice.value) {
    notify.warning('当前用户没有删除设备权限')
    return
  }
  try {
    await confirmAction(formatMessage('确认删除设备“{0}”？', [device.name]), '删除设备', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
    deletingDeviceId.value = device.id
    const loading = showBusy({
      lock: true,
      fullscreen: true,
      text: formatMessage('正在删除设备“{0}”，请稍候...', [device.name]),
      background: 'rgba(15, 23, 42, 0.35)'
    })
    try {
      await deleteDevice(device.id)
    } finally {
      loading.close()
      deletingDeviceId.value = ''
    }
    if (selectedDevice.value?.id === device.id) selectedNodeKey.value = 'root'
    notify.success('设备已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      notify.error(error instanceof Error ? error.message : '删除失败')
    }
  }
}

function openCreateGroup(device: DeviceConfig) {
  if (!canCreateGroup.value) {
    notify.warning('当前用户没有新增分组权限')
    return
  }
  selectedNodeKey.value = deviceNodeKey(device)
  editingGroupId.value = ''
  groupTargetDevice.value = device
  groupForm.value = createGroupDraft(device)
  groupDrawerVisible.value = true
}

function openEditGroup(group: GroupConfig, device = selectedDevice.value) {
  if (!canEditGroup.value) {
    notify.warning('当前用户没有编辑分组权限')
    return
  }
  if (!device) return
  selectedNodeKey.value = groupNodeKey(device, group)
  editingGroupId.value = group.id
  groupTargetDevice.value = device
  groupForm.value = cloneGroup(group)
  groupDrawerVisible.value = true
}

async function saveGroup() {
  if (!groupForm.value || !groupTargetDevice.value) return
  if (!canSaveCurrentGroup.value) {
    notify.warning('当前用户没有保存分组权限')
    return
  }
  const payload = normalizeGroup(cloneGroup(groupForm.value))
  if (!payload.name.trim()) {
    notify.warning('请输入分组名称')
    return
  }

  groupSaving.value = true
  try {
    let saved: GroupConfig
    if (editingGroupId.value) {
      saved = await updateGroup(editingGroupId.value, payload)
      notify.success('分组已更新')
    } else {
      saved = await createGroup(groupTargetDevice.value.id, payload)
      notify.success('分组已新增')
    }
    selectedNodeKey.value = groupNodeKey(groupTargetDevice.value, saved)
    groupDrawerVisible.value = false
    emit('changed')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存分组失败')
  } finally {
    groupSaving.value = false
  }
}

async function removeGroup(group: GroupConfig, device = selectedDevice.value) {
  if (!canDeleteGroup.value) {
    notify.warning('当前用户没有删除分组权限')
    return
  }
  try {
    await confirmAction(formatMessage('确认删除分组“{0}”？分组下标签也会被移除。', [group.name]), '删除分组', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
    await deleteGroupRequest(group.id)
    selectedNodeKey.value = device ? deviceNodeKey(device) : 'root'
    notify.success('分组已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      notify.error(error instanceof Error ? error.message : '删除分组失败')
    }
  }
}

function openCreateTag(device: DeviceConfig, group?: GroupConfig) {
  if (!canCreateTag.value) {
    notify.warning('当前用户没有新增标签权限')
    return
  }
  selectedNodeKey.value = group ? groupNodeKey(device, group) : deviceNodeKey(device)
  editingTagId.value = ''
  tagTargetDevice.value = device
  tagTargetGroup.value = group ?? null
  tagForm.value = createTagDraft(device, group)
  tagDrawerVisible.value = true
}

function openEditTag(tag: TagConfig) {
  if (!canEditTag.value) {
    notify.warning('当前用户没有编辑标签权限')
    return
  }
  const device = selectedDevice.value
  if (!device) return
  editingTagId.value = tag.id
  tagTargetDevice.value = device
  tagTargetGroup.value = selectedGroup.value ?? null
  tagForm.value = cloneTag({ ...tag, protocol: tag.protocol || device.protocol })
  tagDrawerVisible.value = true
}

async function saveTag() {
  if (!tagForm.value || !tagTargetDevice.value) return
  if (!canSaveCurrentTag.value) {
    notify.warning('当前用户没有保存标签权限')
    return
  }
  if (!(await tagFormComponent.value?.validate())) {
    notify.warning('请先补全标签必填信息')
    return
  }

  const payload = normalizeTag(cloneTag(tagForm.value))
  payload.protocol = payload.protocol || tagTargetDevice.value.protocol
  if (!payload.name.trim()) {
    notify.warning('请输入标签名称')
    return
  }

  tagSaving.value = true
  try {
    if (editingTagId.value) {
      await updateTag(editingTagId.value, payload)
      notify.success('标签已更新')
    } else if (tagTargetGroup.value) {
      await createGroupTag(tagTargetGroup.value.id, payload)
      notify.success('标签已新增')
    } else {
      await createDeviceTag(tagTargetDevice.value.id, payload)
      notify.success('标签已新增')
    }
    selectedNodeKey.value = tagTargetGroup.value
      ? groupNodeKey(tagTargetDevice.value, tagTargetGroup.value)
      : deviceNodeKey(tagTargetDevice.value)
    tagDrawerVisible.value = false
    emit('changed')
  } catch (error) {
    notify.error(error instanceof Error ? error.message : '保存标签失败')
  } finally {
    tagSaving.value = false
  }
}

async function removeTag(tag: TagConfig) {
  if (!canDeleteTag.value) {
    notify.warning('当前用户没有删除标签权限')
    return
  }
  try {
    await confirmAction(formatMessage('确认删除标签“{0}”？', [tag.name]), '删除标签', {
      type: 'warning',
      confirmButtonText: '删除',
      cancelButtonText: '取消'
    })
    await deleteTagRequest(tag.id)
    notify.success('标签已删除')
    emit('changed')
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      notify.error(error instanceof Error ? error.message : '删除标签失败')
    }
  }
}

function countDeviceTags(device: DeviceConfig) {
  return (
    (device.tags?.length ?? 0) + (device.groups ?? []).reduce((total, group) => total + (group.tags?.length ?? 0), 0)
  )
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

function snapshotTimestamp() {
  const digits = new Date().toISOString().replace(/\D/g, '')
  return digits.slice(0, 14) || 'export'
}

function connectionSummary(device: DeviceConfig) {
  const connection = device.connection
  if (device.protocol === 'VirtualPlc') return connection.host || 'default'
  if (device.protocol === 'OpcDa') return `${connection.host || 'localhost'} / ${connection.opcDaServerProgId || '-'}`
  if (device.protocol === 'Plugin') return `${connection.driverId || '-'} ${connection.host || ''}`.trim()
  if (['ModbusRtu', 'ModbusAscii', 'MitsubishiSerial', 'MitsubishiQlSerial'].includes(device.protocol)) {
    return `${connection.host || 'COM1'} @ ${connection.port || 9600}`
  }
  return `${connection.host || '-'}:${connection.port || 0}`
}
</script>
