import { computed, inject, type ComputedRef, type InjectionKey, type Ref } from 'vue'

export const PERMISSIONS = {
  bigScreenView: 'bigScreen.view',
  topologyView: 'topology.view',
  dashboardView: 'dashboard.view',
  dashboardStorageHealthEdit: 'dashboard.storageHealth.edit',
  devicesView: 'devices.view',
  channelsCreate: 'channels.create',
  channelsEdit: 'channels.edit',
  channelsDelete: 'channels.delete',
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
  tagsImport: 'tags.import',
  tagsExport: 'tags.export',
  flowRulesView: 'flowRules.view',
  flowRulesCreate: 'flowRules.create',
  flowRulesEdit: 'flowRules.edit',
  flowRulesDelete: 'flowRules.delete',
  flowRulesDebug: 'flowRules.debug',
  mqttView: 'mqtt.view',
  protocolDiagnosticsRun: 'protocolDiagnostics.run',
  protocolDiagnosticsWrite: 'protocolDiagnostics.write',
  mqttEdit: 'mqtt.edit',
  opcUaView: 'opcUa.view',
  opcUaEdit: 'opcUa.edit',
  projectView: 'project.view',
  projectEdit: 'project.edit',
  historyView: 'history.view',
  alarmAcknowledge: 'alarm.acknowledge',
  alarmHandle: 'alarm.handle',
  historyEdit: 'history.edit',
  historyExport: 'history.export',
  scriptsView: 'scripts.view',
  scriptsEdit: 'scripts.edit',
  scriptsExecute: 'scripts.execute',
  scriptsDatabasesManage: 'scripts.databases.manage',
  modelsView: 'models.view',
  modelsEdit: 'models.edit',
  modelsUpload: 'models.upload',
  modelsPublish: 'models.publish',
  modelsTest: 'models.test',
  auditView: 'audit.view',
  auditExport: 'audit.export',
  securityView: 'security.view',
  securityCertificatesManage: 'security.certificates.manage',
  haView: 'ha.view',
  haConfigManage: 'ha.config.manage',
  haSwitchover: 'ha.switchover',
  haMaintenance: 'ha.maintenance',
  haDisasterManage: 'ha.disaster.manage',
  haWritesView: 'ha.writes.view',
  maintenanceView: 'maintenance.view',
  maintenancePackagesUpload: 'maintenance.packages.upload',
  maintenanceUpdatePrepare: 'maintenance.update.prepare',
  maintenanceRollbackPrepare: 'maintenance.rollback.prepare',
  maintenanceUpdateApply: 'maintenance.update.apply',
  maintenanceRollbackApply: 'maintenance.rollback.apply',
  maintenanceWatchdogEdit: 'maintenance.watchdog.edit',
  maintenanceProjectBackupExport: 'maintenance.projectBackup.export',
  maintenanceProjectBackupRestore: 'maintenance.projectBackup.restore',
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

export type PermissionKey = (typeof PERMISSIONS)[keyof typeof PERMISSIONS]

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
