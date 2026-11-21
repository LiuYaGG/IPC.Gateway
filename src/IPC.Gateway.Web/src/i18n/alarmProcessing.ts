import { locale, tx } from './index'

const labels: Record<string, string> = {
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
