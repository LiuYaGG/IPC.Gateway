import { locale, tx } from './index'

const english: Record<string, string> = {
  '诊断目标': 'Target', '临时连接': 'Temporary connection', '参数来源': 'Connection source',
  '手动填写': 'Enter manually', '从已有设备带入': 'Copy from device',
  '如需凭据，请为本次连接重新填写。': 'If credentials are required, enter them for this connection.',
  '诊断操作': 'Operation', '连接测试': 'Connection test', '单点读': 'Single read', '批量读': 'Batch read', '单点写': 'Single write',
  '测试连接': 'Test connection', '读取点位': 'Read point', '执行批读': 'Read batch', '写入点位': 'Write point',
  'UDP 连接测试通过一次读取验证通信，请填写可读点位。': 'Enter a readable point to verify UDP communication with a read request.',
  '从设备标签带入': 'Copy from device tag', '输入标签名或地址筛选': 'Search by tag name or address',
  '批读点位': 'Batch points', '添加点位': 'Add point', '批量粘贴': 'Paste points', '元素个数 / 长度': 'Count / length',
  '按所选类型填写值': 'Enter a value for the selected type', '写后回读': 'Read back after writing',
  '写入需要可写驱动及诊断写入权限。': 'Writing requires a writable driver and diagnostic write permission.',
  '本次结果': 'Results', '执行诊断后查看结果': 'Run a test to see results', '通信明细': 'Communication details',
  '请求组': 'Request group', '错误范围': 'Error scope', '已截断': 'Truncated',
  '此步骤没有可展示的原始报文。': 'No raw frames are available for this step.',
  '报文记录已达到本次诊断上限。': 'The frame capture limit for this test was reached.',
  '展示本次全部报文': 'Show all captured frames', '等待诊断结果': 'Waiting for results', '添加到批读列表': 'Add to batch',
  '每行一个地址。可从表格粘贴四列：地址、数据类型、元素个数、元素偏移，以制表符分隔。': 'Enter one address per line. You can paste four tab-separated columns: address, data type, element count and offset.',
  '确认诊断写入': 'Confirm diagnostic write', '目标设备': 'Target device', '当前没有可用的诊断驱动。': 'No diagnostic drivers are available.',
  '确认后将按以下参数直接写入目标设备。诊断写入独立于项目标签配置。': 'Confirming writes directly to the device using the settings below. Diagnostic writes are independent of project tag settings.',
  '此设备的驱动当前不可用于诊断。': 'The driver for this device is unavailable.',
  '每次最多批读 32 个点位。': 'Each batch can contain up to 32 points.', '请填写有效地址，最多 256 个字符。': 'Enter an address of up to 256 characters.',
  '请选择当前驱动支持的数据类型。': 'Select a data type supported by the driver.', '元素个数必须为 1 至 4096 的整数。': 'The element count must be an integer from 1 to 4096.',
  '元素偏移必须为 0 至 65535 的整数。': 'The element offset must be an integer from 0 to 65535.', '请先粘贴点位地址。': 'Paste at least one address.',
  '每行最多四列。': 'Each line can contain up to four columns.', '请填写写入值。': 'Enter a value to write.',
  '连接超时必须在 100 至 10000 毫秒之间。': 'Connection timeout must be between 100 and 10000 ms.',
  '无法加载协议目录': 'Could not load the driver catalog', '协议诊断失败': 'Protocol diagnostics failed',
  '此驱动不支持读取。': 'This driver does not support reading.', '确认写入': 'Confirm write',
  '元素偏移': 'Element offset', '值': 'Value', '写入值': 'Write value'
}
const codes: Record<string, [string, string]> = {
  Connect: ['连接', 'Connect'], TransportOnly: ['传输层连接', 'Transport connection'], ProbeRead: ['读取验证', 'Read probe'],
  SingleRead: ['单点读取', 'Single read'], DriverBatch: ['驱动批读', 'Driver batch'], Sequential: ['逐项读取', 'Sequential reads'],
  SingleWrite: ['单点写入', 'Single write'], Read: ['读取', 'Read'], Write: ['写入', 'Write'], ReadBack: ['回读', 'Read back'],
  Complete: ['完成', 'Complete'], Success: ['成功', 'Success'], Failed: ['失败', 'Failed'], Partial: ['部分成功', 'Partial success'],
  Matched: ['一致', 'Matched'], Mismatch: ['不一致', 'Mismatch'], NotAttempted: ['未执行', 'Not attempted'],
  Address: ['地址', 'Address'], Device: ['设备', 'Device'], Tag: ['点位', 'Tag'], Batch: ['批次', 'Batch'],
  Transport: ['传输', 'Transport'], Session: ['会话', 'Session'], Unknown: ['未知', 'Unknown']
}
const validation: Record<string, string> = {
  'You do not have permission for this diagnostic operation.': '当前账号没有执行此项诊断的权限。',
  'Protocol diagnostics permission is required.': '当前账号没有协议诊断权限。',
  'A diagnostic is already running for this target or all diagnostic slots are busy.': '该目标正在诊断，或当前诊断任务已达上限，请稍后重试。',
  'A valid protocol and connection are required.': '请选择有效的协议并检查连接参数。',
  'The selected protocol driver is unavailable.': '所选协议驱动当前不可用。',
  'A valid target host or port name is required.': '请填写有效的主机地址或串口名称。',
  'Specify credentials in the authentication fields.': '请在身份验证字段中填写凭据。',
  'Invalid network port.': '网络端口必须为 1 至 65535 的整数。',
  'Timeout must be between 100 and 10000 ms.': '连接超时必须在 100 至 10000 毫秒之间。',
  'Driver options are too large.': '驱动参数超过大小限制。',
  'Driver options must be a JSON object.': '驱动参数需要使用 JSON 对象。',
  'Invalid diagnostic operation.': '无效的诊断操作。',
  'A probe address is required to test this connection.': '测试此连接需要填写可读取的探测地址。',
  'Invalid number of diagnostic addresses.': '诊断地址数量不正确。',
  'Invalid address or data type.': '无效的地址或数据类型。',
  'Invalid element count or offset.': '元素个数或偏移量超出范围。',
  'Explicit write confirmation is required.': '请确认此次诊断写入。',
  'Write value is missing or too large.': '请填写写入值，最多 32768 个字符。',
  'Diagnostic deadline or cancellation.': '诊断已超时或取消。',
  'Diagnostic deadline or cancellation. If the write was sent, its outcome is unknown. Read the address before retrying.': '诊断已超时或取消。写入指令可能已发出，请先回读该地址再决定是否重试。',
  'The driver did not establish a connection.': '驱动未能建立连接。',
  'This driver does not support reads.': '此驱动不支持读取。',
  'This driver does not support writes.': '此驱动不支持写入。',
  'The write was acknowledged, but the read-back did not confirm the requested value.': '设备已确认写入，回读结果与写入值不一致。',
  'The driver returned no result for this address.': '驱动未返回该地址的结果。',
  'The driver reported uncertain quality.': '驱动返回的值质量不确定。',
  'An enabled acquisition device is using this bus.': '已启用的采集设备正在使用此总线。'
}
export function diagnosticText(value: string) {
  const row = /^(\d+ · )(.+)$/.exec(value)
  if (row) return row[1] + (locale.value === 'en-US' ? english[row[2]] ?? tx(row[2]) : row[2])
  if (locale.value === 'en-US') return english[value] ?? tx(value)
  if (validation[value]) return validation[value]
  const timeout = /^Operation timed out or was cancelled \(configured timeout (\d+) ms\)\.$/.exec(value)
  if (timeout) return '通信操作超时或已取消，配置的超时时间为 ' + timeout[1] + ' 毫秒。'
  return tx(value)
}
export function diagnosticCode(value: string) {
  return codes[value]?.[locale.value === 'en-US' ? 1 : 0] ?? tx(value)
}
