<template>
  <section class="view-stack permissions-view">
    <a-card class="panel-card permission-card" size="small">
      <template #title>
        <div class="card-header">
          <div class="detail-title">
            <span>{{ tx("权限分配") }}</span>
            <small>{{
              tx("选择角色后查看人员，并按菜单和按钮分配权限")
            }}</small>
          </div>
          <div class="card-actions">
            <a-button :loading="loading" @click="load">
              <template #icon>
                <span class="ui-icon"><component :is="Refresh" /></span>
              </template>
              {{ tx("刷新") }}
            </a-button>
            <a-button
              v-if="canEditSelectedRole"
              type="primary"
              :loading="saving"
              :disabled="!dirty"
              @click="save"
            >
              <template #icon>
                <span class="ui-icon"><component :is="Check" /></span>
              </template>
              {{ tx("保存权限") }}
            </a-button>
          </div>
        </div>
      </template>
      <div class="permission-layout">
        <aside class="permission-column permission-role-tree">
          <div class="permission-column__head">
            <div>
              <strong>{{ tx("角色树") }}</strong>
              <small>{{ roles.length }} {{ tx("个角色") }}</small>
            </div>
          </div>
          <a-tree
            v-if="roleTreeData.length"
            :tree-data="roleTreeData"
            :field-names="{ key: 'id', title: 'label', children: 'children' }"
            default-expand-all
            block-node
            :selected-keys="[selectedRoleName]"
            class="permission-tree"
            @select="selectRoleNode"
          >
            <template #title="data">
              <div
                :class="['permission-tree-node', { 'is-group': !data.role }]"
              >
                <span>{{ tx(data.label) }}</span>
                <a-tag v-if="data.role"
                  >{{ data.role.userCount }} {{ tx("人") }}</a-tag
                >
              </div>
            </template>
          </a-tree>
        </aside>
        <section class="permission-column permission-members auto-height-table">
          <div class="permission-column__head">
            <div>
              <strong>{{
                selectedRole?.displayName ||
                selectedRole?.name ||
                tx("角色人员")
              }}</strong>
              <small>
                {{
                  selectedRole
                    ? formatMessage("{0} / {1} 人", [
                        selectedRoleUsers.length,
                        selectedRole.userCount,
                      ])
                    : tx("请选择角色")
                }}
              </small>
            </div>
            <a-tag
              v-if="selectedRole"
              :color="tagColor(selectedRole.isSystem ? 'warning' : 'primary')"
            >
              {{ selectedRole.isSystem ? tx("系统角色") : tx("自定义角色") }}
            </a-tag>
          </div>
          <a-alert
            v-if="usersLoadError"
            :message="tx(usersLoadError)"
            type="warning"
            :closable="false"
            class="permission-alert"
          />
          <GatewayTable
            resize-key="components.PermissionsView.vue.0"
            v-if="selectedRole"
            :loading="usersLoading"
            :data-source="selectedRoleUsers"
            row-key="username"
            :row-class-name="
              (row: GatewayUserInfo) =>
                row.username === selectedUsername
                  ? 'permission-user-selected'
                  : ''
            "
            :custom-row="
              (row: GatewayUserInfo) => ({
                onClick: () => {
                  selectedUsername = row.username;
                },
              })
            "
            size="small"
            :pagination="false"
            :columns="
              localizeColumns([
                {
                  key: 'column-0',
                  title: tx('账号'),
                  dataIndex: 'username',
                  width: '130',
                },
                {
                  key: 'column-1',
                  title: tx('姓名'),
                  dataIndex: 'displayName',
                  width: '130',
                },
                { key: 'column-2', title: tx('状态'), width: '86' },
              ])
            "
            :scroll="{ x: 346, y: NaN }"
          >
            <template #bodyCell="tableCell">
              <template v-if="tableCell.column.key === 'column-2'">
                <template
                  v-for="{ row } in [
                    { row: tableCell.record, $index: tableCell.index },
                  ]"
                  :key="tableCell.index"
                >
                  <a-tag :color="tagColor(row.enabled ? 'success' : 'info')">
                    {{ row.enabled ? tx("启用") : tx("停用") }}
                  </a-tag>
                </template>
              </template>
            </template>
            <template #emptyText
              ><a-empty :description="tx(tx('当前角色暂无人员'))"
            /></template>
          </GatewayTable>
          <a-empty v-else :description="tx('请选择左侧角色')" />
        </section>
        <section class="permission-column permission-rights">
          <div class="permission-column__head">
            <div>
              <strong>{{ tx("角色权限") }}</strong>
              <small>
                {{
                  selectedRole
                    ? formatMessage("已选 {0} 项", [permissionsForSave.length])
                    : tx("请选择角色")
                }}
              </small>
            </div>
            <div class="permission-actions">
              <a-button :disabled="!canEditSelectedRole" @click="selectAll">{{
                tx("全选")
              }}</a-button>
              <a-button :disabled="!canEditSelectedRole" @click="clearAll">{{
                tx("清空")
              }}</a-button>
            </div>
          </div>
          <a-alert
            v-if="isAdminRole"
            :message="tx('管理员默认拥有全部权限，运行时不会限制管理员账号。')"
            type="info"
            :closable="false"
            class="permission-alert"
          />
          <a-alert
            v-else-if="selectedRole && !canEditPermissions"
            :message="tx('当前账号没有编辑权限分配的权限。')"
            type="warning"
            :closable="false"
            class="permission-alert"
          />
          <gateway-input
            v-if="selectedRole"
            v-model="keyword"
            allow-clear
            :placeholder="tx('搜索菜单或按钮')"
            class="permission-search"
            :prefix-icon="Search"
          />
          <div
            v-if="selectedRole"
            class="permission-scroll"
            style="overflow: auto"
          >
            <a-tree
              v-if="permissionTreeData.length"
              :tree-data="filteredPermissionTree"
              :field-names="{ key: 'id', title: 'label', children: 'children' }"
              checkable
              check-strictly
              default-expand-all
              block-node
              :checked-keys="displayedPermissions"
              class="permission-tree permission-tree--check"
              @check="checkPermissions"
            >
              <template #title="data">
                <div :class="['permission-tree-node', 'is-' + data.kind]">
                  <span>{{ tx(data.label) }}</span>
                </div>
              </template>
            </a-tree>
          </div>
          <a-empty v-else :description="tx('请选择左侧角色')" />
        </section>
      </div>
    </a-card>
  </section>
</template>

<script setup lang="ts">
import { formatMessage } from "../i18n";
import { notify } from "../utils/ui";
import { tx, localizeColumns } from "../i18n";
import { permissionLabel, permissionPageLabel } from "../utils/display";
import { tagColor } from "../utils/ui";
import { computed, nextTick, onMounted, ref, watch } from "vue";

import {
  CheckOutlined as Check,
  ReloadOutlined as Refresh,
  SearchOutlined as Search,
} from "@ant-design/icons-vue";
import {
  loadRolePermissions,
  loadRoles,
  loadUsers,
  loadCurrentUser,
  updateRolePermissions,
  type GatewayPermissionInfo,
  type GatewayRoleInfo,
  type GatewayUserInfo,
} from "../api";
import { PERMISSIONS, usePermissions } from "../utils/permissions";

interface RoleTreeNode {
  id: string;
  label: string;
  role?: GatewayRoleInfo;
  children?: RoleTreeNode[];
}

interface PermissionTreeNode {
  id: string;
  label: string;
  kind: "menu" | "button";
  permissionKey: string;
  disabled: boolean;
  children?: PermissionTreeNode[];
}

const { hasPermission } = usePermissions();
const roles = ref<GatewayRoleInfo[]>([]);
const users = ref<GatewayUserInfo[]>([]);
const permissions = ref<GatewayPermissionInfo[]>([]);
const loading = ref(false);
const usersLoading = ref(false);
const saving = ref(false);
const selectedRoleName = ref("");
const selectedUsername = ref("");
const currentAccount = ref<GatewayUserInfo | null>(null);
const draftPermissions = ref<string[]>([]);
const originalPermissions = ref<string[]>([]);
const keyword = ref("");
const usersLoadError = ref("");

const roleTreeProps = { children: "children", label: "label" };
const permissionTreeProps = {
  children: "children",
  label: "label",
  disabled: "disabled",
};

const canEditPermissions = computed(() =>
  hasPermission(PERMISSIONS.permissionsEdit),
);
const canViewUsers = computed(() => hasPermission(PERMISSIONS.usersView));
const selectedRole = computed(
  () =>
    roles.value.find((role) => role.name === selectedRoleName.value) ?? null,
);
const isAdminRole = computed(
  () => selectedRole.value?.name.toLowerCase() === "admin",
);
const allPermissionKeys = computed(() =>
  permissions.value
    .filter((item) => !item.key.startsWith("project."))
    .map((item) => item.key),
);
const displayedPermissions = computed(() =>
  isAdminRole.value ? allPermissionKeys.value : draftPermissions.value,
);
const permissionsForSave = computed(() =>
  collectPermissionsForSave([
    ...draftPermissions.value,
    ...originalPermissions.value.filter((key) => key.startsWith("project.")),
  ]),
);
const canEditSelectedRole = computed(
  () => canEditPermissions.value && !!selectedRole.value && !isAdminRole.value,
);
const dirty = computed(
  () =>
    serialize(permissionsForSave.value) !==
    serialize(originalPermissions.value),
);
const selectedRoleUsers = computed(() => {
  const roleName = selectedRoleName.value.toLowerCase();
  if (!roleName) return [];
  return users.value.filter((user) => user.role.toLowerCase() === roleName);
});
watch(selectedRoleUsers, (items) => {
  if (!items.some((user) => user.username === selectedUsername.value)) {
    selectedUsername.value =
      items.find(
        (user) =>
          user.username.toLowerCase() ===
          currentAccount.value?.username.toLowerCase(),
      )?.username ??
      items[0]?.username ??
      "";
  }
});

const roleTreeData = computed<RoleTreeNode[]>(() => {
  const systemRoles = roles.value.filter((role) => role.isSystem);
  const customRoles = roles.value.filter((role) => !role.isSystem);
  const groups: RoleTreeNode[] = [];
  if (systemRoles.length) {
    groups.push({
      id: "role-group-system",
      label: "系统角色",
      children: systemRoles.map(toRoleTreeNode),
    });
  }
  if (customRoles.length) {
    groups.push({
      id: "role-group-custom",
      label: "自定义角色",
      children: customRoles.map(toRoleTreeNode),
    });
  }
  return groups;
});

const permissionTreeData = computed<PermissionTreeNode[]>(() => {
  const groups = new Map<string, GatewayPermissionInfo[]>();
  for (const permission of permissions.value) {
    if (permission.key.startsWith("project.")) continue;
    const groupName = permissionPageLabel(
      permission.key,
      permission.page || permission.group || "其他权限",
    );
    const items = groups.get(groupName) ?? [];
    items.push(permission);
    groups.set(groupName, items);
  }

  return Array.from(groups.entries())
    .map(([name, items]) => {
      const page = items.find((item) => item.action === "view");
      const children = items
        .filter((item) => item.key !== page?.key)
        .sort((left, right) =>
          permissionLabel(left.key, left.name).localeCompare(
            permissionLabel(right.key, right.name),
            "zh-Hans-CN",
          ),
        )
        .map((item) => toPermissionTreeNode(item, "button"));

      return {
        id: page?.key ?? `permission-group:${name}`,
        label: name,
        kind: "menu" as const,
        permissionKey: page?.key ?? "",
        disabled: !canEditSelectedRole.value || !page,
        children,
      };
    })
    .sort((left, right) => left.label.localeCompare(right.label, "zh-Hans-CN"));
});

watch(selectedRole, () => syncDraftFromRole(), { immediate: true });

onMounted(() => load());

async function load() {
  loading.value = true;
  usersLoadError.value = "";
  try {
    const [roleItems, permissionItems, session] = await Promise.all([
      loadRoles(),
      loadRolePermissions(),
      loadCurrentUser(),
    ]);
    currentAccount.value = session.user;
    roles.value = roleItems;
    permissions.value = permissionItems;
    if (
      !selectedRoleName.value ||
      !roles.value.some((role) => role.name === selectedRoleName.value)
    ) {
      selectedRoleName.value =
        roles.value.find(
          (role) =>
            role.name.toLowerCase() === session.user?.role.toLowerCase(),
        )?.name ??
        roles.value[0]?.name ??
        "";
    }
    if (!selectedUsername.value)
      selectedUsername.value = session.user?.username ?? "";
    syncDraftFromRole();
    await loadRoleUsers();
  } catch (error) {
    notify.error(error instanceof Error ? error.message : "权限数据加载失败");
  } finally {
    loading.value = false;
  }
}

async function loadRoleUsers() {
  users.value = [];
  usersLoadError.value = "";
  if (!canViewUsers.value) {
    users.value = currentAccount.value ? [currentAccount.value] : [];
    usersLoadError.value = "当前账号没有查看全部人员权限，仅展示本人。";
    return;
  }

  usersLoading.value = true;
  try {
    users.value = await loadUsers();
  } catch (error) {
    usersLoadError.value =
      error instanceof Error ? error.message : "人员列表加载失败";
  } finally {
    usersLoading.value = false;
  }
}

function handleRoleNodeClick(node: RoleTreeNode) {
  if (!node.role) return;
  selectRole(node.role.name);
}

function selectRole(roleName: string) {
  selectedRoleName.value = roleName;
  syncDraftFromRole();
}

function syncDraftFromRole() {
  const role = selectedRole.value;
  const next = role ? normalize(role.permissions) : [];
  draftPermissions.value = isAdminRole.value
    ? [...allPermissionKeys.value]
    : next;
  originalPermissions.value = isAdminRole.value
    ? [...allPermissionKeys.value]
    : next;
  syncPermissionTree();
}

const filteredPermissionTree = computed(() => {
  const text = keyword.value.trim().toLowerCase();
  if (!text) return permissionTreeData.value;
  return permissionTreeData.value
    .filter((node) => filterPermissionNode(text, node))
    .map((node) => ({
      ...node,
      children: node.label.toLowerCase().includes(text)
        ? node.children
        : (node.children ?? []).filter((child) =>
            filterPermissionNode(text, child),
          ),
    }));
});
function syncPermissionTree() {
  /* Checked keys are controlled by displayedPermissions. */
}
function selectRoleNode(_keys: unknown, info: any) {
  handleRoleNodeClick(info.node.dataRef ?? info.node);
}
function checkPermissions(value: any) {
  if (!canEditSelectedRole.value) return;
  const keys: string[] = Array.isArray(value) ? value : value.checked;
  const visible = new Set(
    filteredPermissionTree.value.flatMap((node) => [
      node.id,
      ...(node.children ?? []).map((child) => child.id),
    ]),
  );
  const hidden = keyword.value
    ? draftPermissions.value.filter((key) => !visible.has(key))
    : [];
  draftPermissions.value = normalize([...hidden, ...keys]);
}

function selectAll() {
  draftPermissions.value = [...allPermissionKeys.value];
}

function clearAll() {
  draftPermissions.value = [];
}

async function save() {
  if (!selectedRole.value || !canEditSelectedRole.value) return;

  saving.value = true;
  try {
    const saved = await updateRolePermissions(
      selectedRole.value.name,
      permissionsForSave.value,
    );
    const index = roles.value.findIndex((role) => role.name === saved.name);
    if (index >= 0) roles.value.splice(index, 1, saved);
    syncDraftFromRole();
    notify.success("角色权限已保存");
  } catch (error) {
    notify.error(error instanceof Error ? error.message : "权限分配保存失败");
  } finally {
    saving.value = false;
  }
}

function collectPermissionsForSave(values: string[]) {
  const selected = new Set(normalize(values));
  const menus = permissionTreeData.value;
  for (const menu of menus) {
    const hasCheckedButton = (menu.children ?? []).some((child) =>
      selected.has(child.permissionKey),
    );
    if (hasCheckedButton && menu.permissionKey)
      selected.add(menu.permissionKey);
  }
  return normalize(Array.from(selected));
}

function filterPermissionNode(value: string, data: PermissionTreeNode) {
  const text = value.trim().toLowerCase();
  if (!text) return true;
  return (
    data.label.toLowerCase().includes(text) ||
    (data.children ?? []).some((child) =>
      child.label.toLowerCase().includes(text),
    )
  );
}

function toRoleTreeNode(role: GatewayRoleInfo): RoleTreeNode {
  return {
    id: role.name,
    label: role.displayName || role.name,
    role,
  };
}

function toPermissionTreeNode(
  permission: GatewayPermissionInfo,
  kind: "menu" | "button",
): PermissionTreeNode {
  return {
    id: permission.key,
    label: permissionLabel(permission.key, permission.name),
    kind,
    permissionKey: permission.key,
    disabled: !canEditSelectedRole.value,
  };
}

function normalize(values: string[]) {
  return Array.from(
    new Set(values.map((value) => value.trim()).filter(Boolean)),
  ).sort((left, right) => left.localeCompare(right));
}

function serialize(values: string[]) {
  return normalize(values).join("|");
}
</script>

<style scoped>
.permissions-view,
.permission-card {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
.permission-card :deep(.ant-card-body) {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
  padding: 16px;
}

.permission-layout {
  display: grid;
  grid-template-columns: 260px minmax(320px, 0.85fr) minmax(380px, 1.15fr);
  gap: 16px;
  flex: 1;
  min-height: 0;
  grid-template-rows: minmax(0, 1fr);
}

.permission-column {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
  border: 1px solid var(--ui-border-color-light);
  border-radius: 8px;
  background: var(--ui-bg-color);
  overflow: hidden;
}

.permission-column__head {
  flex-shrink: 0;
  display: flex;
  min-height: 64px;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border-bottom: 1px solid var(--ui-border-color-lighter);
  background: var(--ui-bg-color-page);
}

.permission-column__head > div:first-child {
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: 4px;
}

.permission-column__head small {
  color: var(--ui-text-color-secondary);
  font-size: calc(12px + 1pt);
}

.permission-actions {
  display: flex;
  gap: 8px;
}

.permission-tree {
  padding: 10px;
  --ui-tree-node-hover-bg-color: var(--ui-color-primary-light-9);
}
.permission-role-tree > .permission-tree {
  flex: 1;
  min-height: 0;
  overflow: auto;
}
.permission-members > :deep(.gateway-table-shell) {
  flex: 1;
  min-height: 0;
}
.permission-members :deep(.permission-user-selected > td) {
  background: #e0eaf3;
}

.permission-tree-node {
  display: flex;
  min-width: 0;
  width: 100%;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  overflow: hidden;
}

.permission-tree-node span {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.permission-tree-node.is-group span {
  color: var(--ui-text-color-secondary);
  font-weight: 600;
}

.permission-tree-node.is-menu span {
  font-weight: 600;
}

.permission-tree-node.is-button span {
  color: var(--ui-text-color-regular);
}

.permission-members :deep(.ant-table) {
  border-radius: 0;
}

.permission-rights {
  display: flex;
  flex-direction: column;
}

.permission-alert {
  margin: 12px 12px 0;
}

.permission-search {
  width: calc(100% - 24px);
  margin: 12px;
}

.permission-scroll {
  flex: 1;
  min-height: 0;
}

.permission-tree--check {
  padding-top: 0;
}

.permission-tree--check :deep(.ant-tree-node-content-wrapper) {
  height: 34px;
}

@media (max-width: 1280px) {
  .permission-layout {
    grid-template-columns: 240px minmax(0, 1fr);
    grid-template-rows: minmax(320px, 1fr) minmax(320px, 1fr);
    min-height: 660px;
  }

  .permission-rights {
    grid-column: 1 / -1;
  }
}

@media (max-width: 820px) {
  .permission-layout {
    grid-template-columns: 1fr;
    grid-template-rows: repeat(3, minmax(320px, 1fr));
    min-height: 1000px;
  }

  .permission-rights {
    grid-column: auto;
  }
}
</style>
