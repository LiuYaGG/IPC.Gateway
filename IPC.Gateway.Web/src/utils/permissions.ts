import { computed, inject, type ComputedRef, type InjectionKey, type Ref } from 'vue'

export const PERMISSIONS = {
  bigScreenView: 'bigScreen.view',
  topologyView: 'topology.view',
  dashboardView: 'dashboard.view',
  dashboardStorageHealthEdit: 'dashboard.storageHealth.edit',
  devicesView: 'devices.view',
  devicesCreate: 'devices.create',
  devicesEdit: 'devices.edit',
  devicesDelete: 'devices.delete',
  groupsCreate: 'groups.create',
  groupsEdit: 'groups.edit',
  groupsDelete: 'groups.delete',
  tagsCreate: 'tags.create',
  tagsEdit: 'tags.edit',
  tagsDelete: 'tags.delete',
  tagsWrite: 'tags.write',
  rulesView: 'rules.view',
  rulesCreate: 'rules.create',
  rulesEdit: 'rules.edit',
  rulesDelete: 'rules.delete',
  rulesDebug: 'rules.debug',
  flowRulesView: 'flowRules.view',
  flowRulesCreate: 'flowRules.create',
  flowRulesEdit: 'flowRules.edit',
  flowRulesDelete: 'flowRules.delete',
  flowRulesDebug: 'flowRules.debug',
  mqttView: 'mqtt.view',
  mqttEdit: 'mqtt.edit',
  opcUaView: 'opcUa.view',
  opcUaEdit: 'opcUa.edit',
  projectView: 'project.view',
  projectEdit: 'project.edit',
  historyView: 'history.view',
  historyEdit: 'history.edit',
  auditView: 'audit.view',
  auditExport: 'audit.export',
  securityView: 'security.view',
  securityCertificatesManage: 'security.certificates.manage',
  maintenanceView: 'maintenance.view',
  maintenancePackagesUpload: 'maintenance.packages.upload',
  maintenanceUpdatePrepare: 'maintenance.update.prepare',
  maintenanceRollbackPrepare: 'maintenance.rollback.prepare',
  maintenanceWatchdogEdit: 'maintenance.watchdog.edit',
  usersView: 'users.view',
  usersCreate: 'users.create',
  usersEdit: 'users.edit',
  usersPasswordReset: 'users.password.reset',
  usersDelete: 'users.delete',
  rolesView: 'roles.view',
  rolesCreate: 'roles.create',
  rolesEdit: 'roles.edit',
  rolesDelete: 'roles.delete',
  permissionsView: 'permissions.view',
  permissionsEdit: 'permissions.edit'
} as const

export type PermissionKey = typeof PERMISSIONS[keyof typeof PERMISSIONS]

export interface PermissionContext {
  permissions: Ref<string[]>
  permissionSet: ComputedRef<Set<string>>
  hasPermission: (permission: string) => boolean
  hasAnyPermission: (permissions: string[]) => boolean
}

export const PermissionContextKey: InjectionKey<PermissionContext> = Symbol('PermissionContext')

export function normalizePermission(permission: string) {
  return permission.trim().toLowerCase()
}

export function createPermissionSet(permissions: string[]) {
  return new Set(permissions.map(normalizePermission).filter(Boolean))
}

export function usePermissions() {
  const context = inject(PermissionContextKey)
  if (context) return context

  const empty = computed(() => new Set<string>())
  return {
    permissions: computed<string[]>(() => []),
    permissionSet: empty,
    hasPermission: () => false,
    hasAnyPermission: () => false
  }
}
