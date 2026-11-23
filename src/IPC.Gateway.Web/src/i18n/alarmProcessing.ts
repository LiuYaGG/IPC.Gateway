import { locale, tx } from './index'

const labels: Record<string, string> = {
  '发生时间': 'Start time',
  '待自动恢复': 'Awaiting recovery',
  '已记录': 'Recorded',
  '已发生': 'Occurred',
  '按动作配置': 'Per action settings',
  '已自动恢复': 'Automatically closed',
  '恢复值': 'Recovery value',
  '持续时长': 'Duration',
  '截至本次查询': 'As of this query',
  '重启后首次观测': 'First observed after restart',
  '恢复时间为重启后首次观测到恢复的时间，实际恢复时刻未知。': 'Recovery was first observed after restart. The exact recovery time is unknown.',
  '报警详情': 'Alarm details',
  '报警状态': 'Alarm status',
  '处理状态': 'Processing status',
  '全部处理状态': 'All processing statuses',
  '待确认': 'Awaiting acknowledgement',
  '已确认': 'Acknowledged',
  '已处理': 'Handled',
  '报警中': 'Active alarm',
  '已恢复': 'Recovered',
  '恢复状态未知': 'Recovery status unknown',
  '确认报警': 'Acknowledge alarm',
  '填写处理': 'Record handling',
  '填写处理记录': 'Record alarm handling',
  '报警处理记录': 'Alarm handling record',
  '确认备注（可选）': 'Acknowledgement note (optional)',
  '处理记录（必填）': 'Handling note (required)',
  '保存处理记录': 'Save handling note',
  '保存报警记录失败': 'Failed to save the alarm record.',
  '恢复时间': 'Recovery time',
  '确认': 'Acknowledge',
  '记录': 'Details',
}

export function alarmText(value: string): string {
  return locale.value === 'en-US' ? labels[value] ?? tx(value) : value
}
