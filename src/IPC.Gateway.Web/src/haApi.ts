import { HttpRequestError, request, type ApiResult } from './api'
import { locale, tx } from './i18n'

export interface HaMember {
  nodeId: string
  clientUrl: string
  peerUrl: string
  gatewayUrl: string
  certificateRef: string
}
export interface HaConfig {
  clusterId: string
  name: string
  businessUrl: string
  preferredNode: string
  renewSeconds: number
  leaseSeconds: number
  members: HaMember[]
}
export interface ConfigState {
  clusterSwitch?: HaClusterSwitch
  template?: HaConfig | null
  active?: HaConfig | null
  draft?: HaConfig | null
  draftVersion: number
  revision: number
  lastChange?: HaOperation
  nodeProgress?: {nodeId:string;configVersion?:string;commitIndex?:string;state:string}[]
}
export interface HaClusterSwitch {
  phase: 'Unconfigured' | 'Enabled' | 'Disabling' | 'Disabled' | 'Enabling' | 'Unknown'
  supported: boolean
  generation: string
  standaloneNode?: string | null
  operationId?: string | null
  message?: string | null
  reasonCode?: string | null
}
export interface HaCapabilities {
  mode: 'Standalone' | 'Cluster'
  clusterId?: string
  connectedNode: string
  localRole: string
  owner?: string
  epoch?: string
  sourceInstanceId?: string
  canWrite?: boolean
  reasonCodes?: string[]
  disaster?: HaDisasterGrant | null
}
export interface HaStatus extends HaCapabilities {
  clusterSwitch?: HaClusterSwitch
  authority: string
  businessUrl?: string
  lastKnownEpoch?: string
  historyReplica?: {state:string;checkedAt?:string;pendingBytes?:number;volume?:string}
  reachableMembers: number | null
  memberCount: number
  members: (HaMember & {
    role:string; arbiterRole?:string; reachable:boolean|null; ready:boolean; maintenance?:boolean
    configVersion?:string; observedAtUtc?:string; reasonCodes?:string[]
    businessEpoch?:string; grantedAtUtc?:string; businessReadyAtUtc?:string; firstSampleAtUtc?:string
  })[]
  canStartSwitchover: boolean
  observedAtUtc?: string
}
export interface HaCheck {
  passed: boolean
  precheckId?: string
  draftVersion?: number
  checks?: { nodeId: string; passed: boolean; message: string; details?:string[]|string }[]
  reasonCodes?: string[]
}
export interface HaOperation {
  operationId: string
  status: string
  message?: string
  reasonCode?: string
}
export interface HaDisasterGrant {
  operationId: string
  nodeId: string
  instanceId: string
  actor: string
  reason: string
  startedAtUtc: string
  expiresAtUtc: string
  active: boolean
}
export interface HaDisasterEvent {
  operationId: string
  nodeId: string
  eventType: string
  actor: string
  reason: string
  timestampUtc: string
  expiresAtUtc?: string | null
}
export interface HaDisasterStatus {
  active: boolean
  eligible: boolean
  blockedReason?: string | null
  localNode: 'A' | 'B'
  grant?: HaDisasterGrant | null
  peers: { nodeId: string; gatewayReachable: boolean; memberReachable: boolean }[]
  recentEvents: HaDisasterEvent[]
}
export interface HaDisasterTakeoverRequest {
  operationId: string
  reason: string
  confirmationNode: 'A' | 'B'
  peerIsolated: true
  durationMinutes: number
}
export interface HaDisasterExitRequest {
  operationId: string
  reason: string
}
export async function haGet<T>(path: string, signal?: AbortSignal) {
  const result = await request<ApiResult<T>>('/api/ha/' + path, { signal, cache: 'no-store' }).catch(translateError)
  if (!result.success) throw new Error(tx(result.errorMessage || '高可用请求失败'))
  return result.data
}
export async function haSend<T>(path: string, body: unknown, method = 'POST', signal?: AbortSignal) {
  const result = await request<ApiResult<T>>('/api/ha/' + path, { method, body: JSON.stringify(body), signal }).catch(translateError)
  if (!result.success) throw new Error(tx(result.errorMessage || '高可用请求失败'))
  return result.data
}
export function newHaId() {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6] & 15) | 64
  bytes[8] = (bytes[8] & 63) | 128
  const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('')
  return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-')
}
const names: Record<string, string> = {
  EmergencyActive:'人工灾难接管',LocalDisaster:'本机人工授权',
  DisasterTakeover:'人工灾难接管已启用',DisasterRenewal:'人工灾难接管已续期',DisasterExit:'人工灾难接管已退出',
  DisasterTakeoverRequested:'已请求人工灾难接管',DisasterRenewalRequested:'已请求续期人工接管',DisasterRejected:'人工灾难接管请求已拒绝',
  DisasterExpired:'人工灾难接管已到期',DisasterPeerRecovered:'其他节点恢复连通，人工灾难接管已撤销',DisasterMonitorFailed:'监测失败，人工灾难接管已撤销',
  Active:'主机', Standby:'备机', Demoted:'已降级', Standalone:'单机', Cluster:'集群', Leader:'仲裁领导者', ArbiterOnly:'仅仲裁',
  NotConfigured:'未配置', Synchronized:'已同步', CatchingUp:'同步中', Unavailable:'不可用', NotDispatched:'未下发',
  LocalJournal:'本机执行记录', Observed:'已观测', Partial:'部分已写入',
  Follower:'跟随成员', Candidate:'候选成员', Pending:'等待中', Running:'执行中', Completed:'已完成',
  None:'未开始', Preparing:'正在准备', Prepared:'已准备', Staged:'已暂存', Installed:'已写入', PendingRestart:'等待重启', Restarted:'已重启', Cancelled:'已取消', Canceled:'已取消',
  Succeeded:'已完成', Applied:'已应用', Failed:'失败', Rejected:'已拒绝', Confirmed:'设备已确认', Unknown:'未知', Committed:'多数派已提交',
  acquire:'取得主身份',release:'交还主身份',maintenance:'维护',transfer:'计划切换',config:'高可用配置',mode:'集群开关',permit:'PLC 写授权',transport:'组网迁移',
  'transport-lock':'组网迁移准备','transport-cancel':'取消组网迁移','transport-finish':'完成组网迁移',TransportMigration:'组网迁移',
  HA_TARGET_NOT_READY:'节点尚未就绪', HA_REASON_REQUIRED:'请填写切换原因。', HA_INVALID_TARGET:'目标节点无效。', HA_MAINTENANCE_BLOCKED:'维护后将无法保证多数派。',
  HA_NO_QUORUM:'无法取得多数派', HA_NOT_ACTIVE:'本机没有业务主身份', HA_STALE_EPOCH:'业务任期已变化',
  HA_NOT_READY:'节点尚未就绪', HA_STANDBY:'本机为备机', HA_STALE_CONFIG:'配置版本已变化', HA_DRAFT_CHANGED:'配置草稿已变化，请重新读取。'
}
const reasons: Record<string,[string,string]> = {
  HA_DISASTER_CONFIGURATION_INCOMPLETE:['已登记成员地址不完整，禁止人工灾难接管。','Enrolled member endpoints are incomplete. Manual disaster takeover is blocked.'],
  HA_DISASTER_TRANSPORT_PENDING:['组网变更尚未完成或等待重启，请先完成变更。','Network changes are incomplete or awaiting restart. Complete them before taking over.'],
  HA_DISASTER_PEER_REACHABLE:['其他成员或对端网关仍可达，禁止人工灾难接管。','Another member or the other gateway is reachable. Manual disaster takeover is blocked.'],
  HA_DISASTER_ALREADY_ACTIVE:['本机已有正常业务身份，无需人工灾难接管。','This node already has normal business ownership. Manual disaster takeover is unnecessary.'],
  HA_DISASTER_CONFIRMATION_REQUIRED:['请确认其他网关已物理停机或隔离，并输入本机节点 ID。','Confirm the other gateway is physically shut down or isolated, and enter this node ID.'],
  HA_DISASTER_INVALID_REQUEST:['请填写操作原因，并选择 15 至 1440 分钟的授权时长。','Enter a reason and select a grant duration between 15 and 1440 minutes.'],
  HA_DISASTER_NOT_DRAINED:['本机原业务或在途写入尚未退出，请等待后重新核实状态。','Previous operations or in-flight writes have not drained on this node. Wait, then check the status again.'],
  HA_DISASTER_EXPIRED:['人工灾难接管授权已到期。','Manual disaster takeover authorization expired.'],
  HA_DISASTER_PEER_RECOVERED:['其他节点恢复连通，人工授权已撤销。','Another node is reachable. Manual authorization was revoked.'],
  HA_DISASTER_QUORUM_RESTORED:['多数派已恢复，人工授权已撤销，由正常仲裁决定业务归属。','Quorum is restored. Manual authorization was revoked; normal arbitration decides ownership.'],
  HA_DISASTER_OPERATOR_EXIT:['管理员已退出人工灾难接管。','An administrator ended manual disaster takeover.'],
  HA_DISASTER_MONITOR_FAILED:['无法继续监测其他节点，人工授权已撤销。','Other nodes can no longer be monitored. Manual authorization was revoked.'],
  HA_CLUSTER_ID_IMMUTABLE:['已登记集群的 ID 不可修改，请保留原集群身份。','An enrolled cluster ID cannot be changed. Retain the existing cluster identity.'],
  HA_MEMBER_REPROVISION_REQUIRED:['仲裁地址或证书与已安装清单不同。请关闭热备，使用「组网地址与证书变更」准备并确认计划，重启三个成员后重新预检。','Arbiter endpoints or certificates differ from the installed manifest. Disable failover, prepare and confirm Network and certificate changes, then restart all three members and rerun precheck.'],
  HA_GATEWAY_UNREACHABLE:['网关管理地址不可达，请核对地址并启动对应网关。','The gateway management URL is unreachable. Check the address and start that gateway.'],
  HA_TRANSPORT_PENDING:['已有组网迁移尚未完成。请继续原计划；尚未提交的计划可以取消，已部分提交的计划必须重试完成。','A network migration is pending. Continue that plan. An uncommitted plan can be cancelled; a partially committed plan must be retried to completion.'],
  HA_TRANSPORT_DISABLE_REQUIRED:['请先关闭双机热备，并直连保留单机业务的节点执行组网迁移。','Disable failover first, then connect directly to the gateway reserved for standalone operation to migrate the network.'],
  HA_TRANSPORT_PLAN_CHANGED:['组网迁移计划或已登记成员配置已变化，请刷新并核对原计划。','The network migration plan or enrolled member configuration changed. Refresh and review the original plan.'],
  HA_TRANSPORT_PLAN_REQUIRED:['缺少已准备的组网迁移计划，请先准备并核对三个成员的迁移计划。','No prepared network migration plan was found. Prepare and review the plan for all three members first.'],
  HA_TRANSPORT_USE_CERTIFICATE_ROTATION:['新证书指纹没有匹配的本机私钥。请选择重新生成成员和网关证书，再准备迁移计划。','The new certificate fingerprint has no matching local private key. Select Generate new member and gateway certificates, then prepare the migration plan again.'],
  HA_TRANSPORT_CONFIG_READONLY:['仲裁服务无权更新配置目录。请更新部署脚本并重新安装服务，授予原配置目录所需的写入权限。','The arbiter service cannot write to its configuration directory. Update the deployment scripts and reinstall the service with write access to the existing configuration directory.'],
  HA_TRANSPORT_UPGRADE_REQUIRED:['三个仲裁成员尚未全部支持组网迁移。请先更新三个成员，再重试原计划。','Not all three arbiters support network migration. Update all three members, then retry the original plan.'],
  HA_TRANSPORT_DISASTER_ACTIVE:['本机正在使用人工灾难接管授权，请先退出人工接管，再执行组网迁移。','Manual disaster takeover is active on this node. End the manual takeover before migrating the network.'],
  HA_TRANSPORT_INVALID_PROOF:['迁移证书或三成员签名证明无效，配置未获授权。请核对已登记成员身份并重新准备。','A migration certificate or a signature from the three members is invalid. The configuration is not authorized. Verify the enrolled member identities and prepare the plan again.'],
  HA_TRANSPORT_INVALID_PLAN:['组网迁移配置无效，请核对集群身份、A/B/C 成员、地址、端口及租约设置。','The network migration configuration is invalid. Check the cluster identity, members A/B/C, URLs, ports and lease settings.'],
  HA_TRANSPORT_EMBEDDED_REQUIRED:['此成员缺少完整组网配置文件，无法执行网页迁移。请恢复与本机登记身份匹配的成员及网关配置。','This member lacks the complete cluster configuration files required for migration through the web interface. Restore the member and gateway configuration matching its enrolled identity.'],
  HA_TRANSPORT_ENV_OVERRIDE:['环境变量覆盖了仲裁成员清单或证书设置，网页迁移无法安全持久更新这些值。请先调整部署环境中的覆盖设置。','Environment variables override the arbiter membership or certificate settings. Web migration cannot persist these values safely. Adjust the deployment environment overrides first.'],
  HA_TRANSPORT_STORAGE_FAILED:['无法持久写入组网配置。请检查配置目录权限和磁盘容量，再重试原计划。','The network configuration could not be written durably. Check configuration directory permissions and available disk space, then retry the original plan.'],
  HA_TRANSPORT_STATE_DIRECTORY:['仲裁持久状态目录必须配置为绝对路径，请修正原部署的状态目录设置。','The arbiter persistent state directory must be an absolute path. Correct the state directory setting in the existing deployment.'],
  HA_TRANSPORT_COMMIT_NOT_REPLAYED:['本机尚未确认多数派提交的迁移记录，不能将新配置标记为生效。请等待成员同步后重试原计划。','This member has not yet confirmed the migration record committed by quorum. The new configuration cannot be marked active. Wait for member synchronization, then retry the original plan.'],
  HA_TRANSPORT_MODE_CHANGED:['集群开关状态、单机业务节点或迁移计划已变化。请刷新后确认集群仍已关闭，并核对原计划。','The cluster mode, standalone gateway or migration plan changed. Refresh, verify that failover is still disabled and review the original plan.'],
  HA_TRANSPORT_INVALID_PERMIT:['本机迁移许可与已提交计划、成员清单或签名证明不一致，无法加载变更。请核对原计划及持久迁移文件。','The local migration permit does not match the committed plan, membership or signed proofs. The change cannot be loaded. Check the original plan and persisted migration files.'],
  HA_TRANSPORT_PERMIT_REQUIRED:['缺少已提交的本机迁移许可，不能改写持久成员身份。请先完成原迁移计划的提交。','The committed local migration permit is missing. The persistent member identity cannot be changed. Complete the original migration plan commit first.'],
  HA_TRANSPORT_PROOFS_REQUIRED:['需要 A、B、C 三个不同成员对同一迁移计划的签名证明，请完成三成员准备。','Signed proofs from three distinct members A, B and C are required for the same migration plan. Complete preparation on all three members.'],
  HA_TRANSPORT_IDENTITY_MISMATCH:['本机持久仲裁身份与迁移前登记身份不一致，迁移已停止。请核对原成员数据目录和迁移计划。','The persistent arbiter identity differs from its identity before migration. Migration stopped. Check the original member data directory and migration plan.'],
  HA_TRANSPORT_GATEWAY_IDENTITY_MISMATCH:['本机网关持久身份与迁移前登记不一致，无法应用变更。请核对原网关身份文件和迁移计划。','The persistent gateway identity differs from its identity before migration. The change cannot be applied. Check the original gateway identity file and migration plan.'],
  HA_TRANSPORT_MISSING_GATEWAY_IDENTITY:['缺少本机已登记的网关持久身份，无法应用迁移。请恢复与原集群匹配的网关身份文件。','The enrolled persistent gateway identity is missing. Migration cannot be applied. Restore the gateway identity file matching the original cluster.'],
  HA_TRANSPORT_LOCAL_IDENTITY_CHANGED:['准备后本机成员身份发生变化，无法确认迁移计划。请重新读取成员身份并重新准备。','The local member identity changed after preparation. The migration plan cannot be confirmed. Reload the member identity and prepare the plan again.'],
  HA_TRANSPORT_KEY_REQUIRED:['本机签名证书缺少可用的 RSA 私钥，无法生成迁移证明。请恢复与已登记指纹匹配的证书和私钥。','The local signing certificate has no usable RSA private key, so migration proofs cannot be generated. Restore the certificate and private key matching the enrolled fingerprint.'],
  HA_CLUSTER_DISABLED:['请先开启集群开关。','Enable the cluster first.'],
  HA_MODE_UNAVAILABLE:['无法确认多数派或仲裁版本尚未更新。','Check quorum connectivity and upgrade the arbiter members.'],
  HA_MODE_CHANGED:['集群开关状态已变化，请重新确认。','Cluster mode changed. Review the operation again.'],
  HA_MODE_REJECTED:['集群开关变更已被拒绝。','The cluster mode change was rejected.'],
  HA_GATEWAY_IDENTITY:['无法核实网关身份，请检查登记的管理地址和证书。','The gateway identity could not be verified. Check its management URL and certificate.'],
  HA_SINGLE_GATEWAY_REQUIRED:['关闭热备要求只有当前业务网关可达，请先停止另一台网关。','Stop the other gateway service. Only the current gateway may be online when disabling failover.'],
  HA_PEER_NOT_PARKED:['对端尚未确认已停止业务。','The other gateway has not confirmed that it stopped operations.'],
  HA_PREFLIGHT_PASSED:['成员预检通过。','Member precheck passed.'],
  HA_MEMBER_CHECK_FAILED:['成员预检失败。','The member precheck failed.'],
  HA_MANIFEST_MISMATCH:['成员清单与安装登记不一致。','The member manifest differs from the enrollment.'],
  HA_STORAGE_UNAVAILABLE:['持久目录不可读写。','The persistent storage directory is unavailable.'],
  HA_PEER_CHECK_FAILED:['成员间通信预检失败。','Peer communication checks failed.'],
  HA_PEER_REJECTED:['远端成员拒绝连接。','The peer rejected the connection.'],
  HA_PEER_IDENTITY_MISMATCH:['成员身份或版本不匹配。','The peer identity or wire version differs.'],
  HA_PEER_UNREACHABLE:['成员间通信地址不可达。','The peer endpoint could not be reached.'],
  HA_TIMEOUT:['等待仲裁响应超时，请查询原操作。','Arbiter response timed out. Check the original operation.'],
  HA_COMMIT_UNKNOWN:['暂时无法确认提交结果。','Commit outcome is unknown.'],
  HA_CONFIG_CONFLICT:['生效配置版本已变化，请重新读取。','The active revision changed. Reload the configuration.'],
  HA_ENROLLMENT_REQUIRED:['请先完成集群登记。','Complete cluster enrollment first.'],
  HA_HANDOVER_REQUIRED:['请先完成业务交接。','Transfer gateway ownership first.'],
  HA_ID_REUSED:['操作号已用于其他请求。','The operation ID belongs to a different request.'],
  HA_INVALID_CONFIGURATION:['请检查集群身份、业务地址和租约设置。','Check the cluster identity, stable URL and lease settings.'],
  HA_INVALID_MEMBER:['请检查节点通信地址、端口和证书引用。','Check the member URLs, ports and certificate reference.'],
  HA_INVALID_MEMBERS:['必须配置 A、B、C 三个独立成员。','Three distinct members A, B and C are required.'],
  HA_MEMBER_UNAVAILABLE:['仲裁成员不可达。','The arbiter member is unavailable.'],
  HA_MEMBERS_NOT_READY:['成员尚未全部就绪。','Members have not completed readiness checks.'],
  HA_NOT_CONFIGURED:['尚未配置高可用集群。','The HA cluster has not been configured.'],
  HA_NOT_PROVISIONED:['尚未登记节点身份。','The node has not been enrolled.'],
  HA_NOT_LEADER:['仲裁领导者已变化。','The arbiter leader changed.'],
  HA_OPERATION_NOT_FOUND:['操作不存在。','The operation could not be found.'],
  HA_OPERATION_PENDING:['已有配置正在应用。','A configuration change is already in progress.'],
  HA_OWNER_ACTIVE:['当前主机仍持有有效租约。','The current owner still holds a valid lease.'],
  HA_PRECHECK_FAILED:['预检失败，请核对节点详情。','Precheck failed. See node details.'],
  HA_PRECHECK_REQUIRED:['请重新执行预检。','Run the precheck again.'],
  HA_STALE_STATE:['集群状态已变化，请刷新。','The cluster state changed. Refresh the page.'],
  HA_UNREGISTERED_MEMBER:['成员未登记。','The member is not registered.'],
  HA_WRONG_IDENTITY:['节点身份不匹配。','The node identity does not match.'],
  HA_CONFIGURATION_CHANGED:['项目版本已变化。','The project revision changed.'],
  HA_DATABASE_EPOCH_CONFLICT:['业务数据库任期不匹配。','The business database epoch does not match.'],
  HA_HISTORY_REPLICA_UNAVAILABLE:['历史库副本不可用。','The history replica is unavailable.'],
  HA_ALARM_IMPORT_AFTER_ACTIVATION:['已有业务任期，不能导入未登记的单机报警。','An active epoch already exists. Standalone alarms cannot be imported.'],
  HA_ALARM_THRESHOLD_CONFLICT:['节点的阈值报警状态冲突。','The nodes contain conflicting threshold alarm states.'],
  HA_ALARM_EVENT_CONFLICT:['同一报警在两个节点上的记录冲突。','The two nodes have conflicting copies of the same alarm.']
}
function translateError(error: unknown): never {
  if(error instanceof HttpRequestError && error.code && (error.code in reasons || error.code in names))
    throw new HttpRequestError(error.status,haLabel(error.code),error.code)
  throw error
}
export function haLabel(code?: string | null) {
  if(!code)return '—'
  const quorum=code.match(/^仲裁不足（(\d+)\/3），无法确认授主。多数派恢复后自动继续。$/)
  if(quorum && locale.value==='en-US')return `Only ${quorum[1]} of 3 voters are reachable. Ownership will be checked again when quorum returns.`
  const split=code.indexOf(':')
  const reason=reasons[split<0?code:code.slice(0,split)]
  return reason?reason[locale.value==='en-US'?1:0]+(split<0?'':code.slice(split)):tx(names[code] ?? code)
}
export function haCheckDetails(details?:string[]|string) {
  const values=typeof details==='string'?[details]:details || []
  return values.map(value=>value.replace(/HA_[A-Z_]+/g,code=>haLabel(code))).join(' · ')
}
