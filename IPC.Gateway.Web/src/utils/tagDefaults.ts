import type { DeviceConfig, GroupConfig, TagAlarmConfig, TagConfig, ScalingConfig, DataCleaningConfig } from '../api'

const standardScalarDataTypeOptions = [
  'Bool',
  'Int16',
  'UInt16',
  'Int32',
  'UInt32',
  'Float',
  'Double',
  'String'
]

const fullScalarDataTypeOptions = [
  'Bool',
  'Int16',
  'UInt16',
  'Int32',
  'UInt32',
  'Int64',
  'UInt64',
  'Float',
  'Double',
  'String'
]

const cipScalarDataTypeOptions = [
  'Bool',
  'Int8',
  'UInt8',
  'Int16',
  'UInt16',
  'Int32',
  'UInt32',
  'Int64',
  'UInt64',
  'Float',
  'Double',
  'String'
]

const dnp3ScalarDataTypeOptions = cipScalarDataTypeOptions.filter(item => item !== 'String')

const compactScalarDataTypeOptions = [
  'Bool',
  'Int16',
  'UInt16',
  'Int32',
  'UInt32',
  'Float',
  'String'
]

const meterScalarDataTypeOptions = standardScalarDataTypeOptions.filter(item => item !== 'Bool')

const arrayDataTypeOptions = [
  'BoolArray',
  'Int16Array',
  'UInt16Array',
  'Int32Array',
  'UInt32Array',
  'Int64Array',
  'UInt64Array',
  'FloatArray',
  'DoubleArray'
]

const cipArrayDataTypeOptions = [
  'BoolArray',
  'Int8Array',
  'UInt8Array',
  'Int16Array',
  'UInt16Array',
  'Int32Array',
  'UInt32Array',
  'Int64Array',
  'UInt64Array',
  'FloatArray',
  'DoubleArray'
]

const arrayCapableProtocols = new Set([
  'RockwellCip',
  'EtherNetIp',
  'CanOpen',
  'BeckhoffAds',
  'MqttClient',
  'SiemensS7',
  'MitsubishiMc',
  'MitsubishiMc1E',
  'MitsubishiSerial',
  'MitsubishiQlSerial',
  'OmronFins',
  'ModbusTcp',
  'ModbusRtu',
  'ModbusAscii',
  'OpcUa',
  'OpcDa',
  'VirtualPlc'
])

const fullScalarProtocols = new Set([
  ...arrayCapableProtocols,
  'BacnetIp'
])

export interface DataTypeOptionGroup {
  label: string
  options: string[]
}

export function dataTypeOptionGroups(protocol: string): DataTypeOptionGroup[] {
  const usesByteTypes = protocol === 'RockwellCip' || protocol === 'EtherNetIp' || protocol === 'CanOpen' || protocol === 'BeckhoffAds'
  const scalarOptions = protocol === 'Dnp3'
    ? dnp3ScalarDataTypeOptions
    : usesByteTypes || protocol === 'Snmp' || protocol === 'MqttClient'
    ? cipScalarDataTypeOptions
    : fullScalarProtocols.has(protocol)
    ? fullScalarDataTypeOptions
    : protocol === 'RockwellPccc'
      ? compactScalarDataTypeOptions
      : protocol === 'Dlt6452007' || protocol === 'Cjt1882004' || protocol === 'Cjt1882018'
        ? meterScalarDataTypeOptions
        : standardScalarDataTypeOptions
  const groups: DataTypeOptionGroup[] = [
    { label: '标量类型', options: [...scalarOptions] }
  ]

  if (arrayCapableProtocols.has(protocol)) {
    groups.push({
      label: '数组类型',
      options: [...(usesByteTypes ? cipArrayDataTypeOptions : arrayDataTypeOptions)]
    })
  }
  if (protocol === 'ModbusTcp' || protocol === 'ModbusRtu' || protocol === 'ModbusAscii' || protocol === 'VirtualPlc') {
    groups.push({ label: '位类型', options: ['Coil', 'CoilArray'] })
  }
  return groups
}

export const accessModeOptions = ['ReadOnly', 'ReadWrite', 'WriteOnly']

export const meterProtocolOptions = [
  { label: 'DL/T 645-2007', value: 'Dlt6452007' },
  { label: 'CJ/T 188-2004', value: 'Cjt1882004' },
  { label: 'CJ/T 188-2018', value: 'Cjt1882018' }
]

const dlt645MeterTypes = ['电能表', '多功能电表', '智能电表']
const cjt188MeterTypes = ['水表', '热水表', '直饮水表', '中水表', '热量表', '冷量表', '冷热量表', '燃气表']

export function createGroupDraft(device: DeviceConfig): GroupConfig {
  return normalizeGroup({
    id: '',
    deviceId: device.id,
    name: '',
    enabled: true,
    scanRateMs: device.defaultScanRateMs || 1000,
    tags: []
  })
}

export function cloneGroup(group: GroupConfig): GroupConfig {
  return normalizeGroup({
    ...group,
    tags: (group.tags ?? []).map(cloneTag)
  })
}

export function normalizeGroup(group: GroupConfig): GroupConfig {
  group.id = group.id || ''
  group.deviceId = group.deviceId || ''
  group.name = group.name || ''
  group.enabled = group.enabled ?? true
  group.scanRateMs = group.scanRateMs || 1000
  group.tags = group.tags ?? []
  return group
}

export function createTagDraft(device: DeviceConfig, group?: GroupConfig): TagConfig {
  return normalizeTag({
    id: '',
    deviceId: device.id,
    groupId: group?.id ?? '',
    name: '',
    protocol: device.protocol,
    address: '',
    meterAddress: '',
    meterDataIdentifier: '',
    meterType: '',
    dataType: 'Int16',
    elementCount: 1,
    elementOffset: 0,
    enabled: true,
    mqttPublishEnabled: false,
    accessMode: 'ReadWrite',
    scanRateMs: group?.scanRateMs || device.defaultScanRateMs || 1000,
    failureRetryDelayMs: device.failureRetryDelayMs || 1000,
    unit: '',
    pointCode: '',
    assetPath: '',
    businessType: '',
    source: device.name,
    precision: -1,
    scaling: createDefaultScaling(),
    cleaning: createDefaultCleaning(),
    alarm: createDefaultAlarm(),
    description: '',
    isVirtual: false,
    virtualModel: createDefaultVirtualModel()
  })
}

export function cloneTag(tag: TagConfig): TagConfig {
  return normalizeTag({
    ...tag,
    scaling: { ...createDefaultScaling(), ...(tag.scaling ?? {}) },
    cleaning: normalizeCleaning({ ...createDefaultCleaning(), ...(tag.cleaning ?? {}) }),
    alarm: { ...createDefaultAlarm(), ...(tag.alarm ?? {}) },
    virtualModel: normalizeVirtualModel({ ...createDefaultVirtualModel(), ...(tag.virtualModel ?? {}) })
  })
}

export function normalizeTag(tag: TagConfig): TagConfig {
  tag.id = tag.id || ''
  tag.deviceId = tag.deviceId || ''
  tag.groupId = tag.groupId || ''
  tag.name = tag.name || ''
  tag.protocol = tag.protocol || ''
  tag.address = tag.address || ''
  tag.meterAddress = tag.meterAddress || ''
  tag.meterDataIdentifier = tag.meterDataIdentifier || ''
  tag.meterType = tag.meterType || ''
  tag.dataType = tag.dataType || 'Int16'
  tag.elementCount = tag.elementCount || 1
  tag.elementOffset = tag.elementOffset || 0
  tag.enabled = tag.enabled ?? true
  tag.mqttPublishEnabled = tag.mqttPublishEnabled ?? false
  tag.accessMode = tag.accessMode || 'ReadWrite'
  tag.scanRateMs = tag.scanRateMs || 1000
  tag.failureRetryDelayMs = tag.failureRetryDelayMs || 1000
  tag.unit = tag.unit || ''
  tag.pointCode = tag.pointCode || ''
  tag.assetPath = tag.assetPath || ''
  tag.businessType = tag.businessType || ''
  tag.source = tag.source || ''
  tag.precision = tag.precision ?? -1
  tag.scaling = { ...createDefaultScaling(), ...(tag.scaling ?? {}) }
  tag.cleaning = normalizeCleaning({ ...createDefaultCleaning(), ...(tag.cleaning ?? {}) })
  tag.alarm = { ...createDefaultAlarm(), ...(tag.alarm ?? {}) }
  tag.description = tag.description || ''
  tag.isVirtual = tag.isVirtual ?? false
  tag.virtualModel = normalizeVirtualModel({ ...createDefaultVirtualModel(), ...(tag.virtualModel ?? {}) })
  if (tag.isVirtual) {
    tag.address = ''
    tag.accessMode = 'ReadOnly'
    tag.source = 'ONNX'
  }
  return tag
}

/**
 * 创建虚拟模型标签默认配置。
 */
export function createDefaultVirtualModel() {
  return {
    modelId: '', modelVersion: 0, inputName: '', inputNames: '', outputName: '', outputIndex: 0,
    triggerMode: 'OnInputChanged' as const, intervalMilliseconds: 1000, debounceMilliseconds: 100,
    maxInputAgeMilliseconds: 10000, timeoutMilliseconds: 1000, failurePolicy: 'KeepLastGood' as const,
    fallbackValue: '', boolThreshold: 0.5, inputs: []
  }
}

/**
 * 规范化虚拟模型标签配置及输入缩放参数。
 */
function normalizeVirtualModel(config: ReturnType<typeof createDefaultVirtualModel> | any) {
  config.modelId = config.modelId || ''
  config.modelVersion = Math.max(0, Number(config.modelVersion) || 0)
  config.inputName = config.inputName || ''
  config.inputNames = config.inputNames || ''
  config.outputName = config.outputName || ''
  config.outputIndex = Math.max(0, Number(config.outputIndex) || 0)
  config.triggerMode = config.triggerMode === 'Interval' ? 'Interval' : 'OnInputChanged'
  config.intervalMilliseconds = Math.max(100, Number(config.intervalMilliseconds) || 1000)
  config.debounceMilliseconds = Math.max(0, Number(config.debounceMilliseconds) || 0)
  config.maxInputAgeMilliseconds = Math.max(100, Number(config.maxInputAgeMilliseconds) || 10000)
  config.timeoutMilliseconds = Math.max(10, Number(config.timeoutMilliseconds) || 1000)
  config.failurePolicy = ['KeepLastGood', 'UseFallback', 'MarkBad'].includes(config.failurePolicy) ? config.failurePolicy : 'KeepLastGood'
  config.fallbackValue = config.fallbackValue || ''
  config.boolThreshold = Number.isFinite(Number(config.boolThreshold)) ? Number(config.boolThreshold) : 0.5
  config.inputs = (config.inputs ?? []).map((item: any) => ({ featureName: item.featureName || '', tagPath: item.tagPath || '', multiplier: Number(item.multiplier) || 1, offset: Number(item.offset) || 0 }))
  return config
}

export function isMeterProtocol(protocol: string) {
  return protocol === 'Dlt6452007' || protocol === 'Cjt1882004' || protocol === 'Cjt1882018'
}

export function meterTypeOptions(protocol: string) {
  if (protocol === 'Dlt6452007') return dlt645MeterTypes
  if (protocol === 'Cjt1882004' || protocol === 'Cjt1882018') return cjt188MeterTypes
  return []
}

export function defaultMeterType(protocol: string) {
  return meterTypeOptions(protocol)[0] ?? ''
}

export function tagAddressPlaceholder(protocol: string) {
  if (protocol === 'RockwellCip') return '符号标签：Program:MainProgram.TagName、MyArray[0]'
  if (protocol === 'EtherNetIp') return '显式消息：@0x01/1/7、Assembly:100；隐式 I/O：Input:0.0、Output:0'
  if (protocol === 'BeckhoffAds') return '例如：MAIN.Counter、MAIN.Values[0]、GVL.Temperature'
  if (protocol === 'Snmp') return '例如：1.3.6.1.2.1.1.3.0（sysUpTime）'
  if (protocol === 'MqttClient') return 'Text：factory/line1/value；JSON：factory/line1/data|temperature；Sparkplug B：spBv1.0/group/DDATA/node/device|metric'
  if (protocol === 'Dnp3') return '例如：Binary:0、Analog:12、Counter:3、BinaryOutput:5'
  if (protocol === 'RockwellPccc') return '例如：N7:0、B3:0/1、T4:0.ACC'
  if (protocol === 'ModbusTcp' || protocol === 'ModbusRtu' || protocol === 'ModbusAscii') return '例如：40001、00001、3:40001'
  if (protocol === 'SiemensS7') return '例如：DB1.DBW0、M0.0、E0.0、A0.0、V100'
  if (protocol === 'MitsubishiMc' || protocol === 'MitsubishiMc1E' || protocol === 'MitsubishiSerial' || protocol === 'MitsubishiQlSerial') return '例如：D100、M100、X0'
  if (protocol === 'CanOpen') return 'SDO：1:0x6041:0；PDO：TPDO1:1:0.0、RPDO1:1:0；服务：Heartbeat:1、EMCY:1、NMT:1、SYNC、TIME'
  if (protocol === 'OmronFins') return '例如：D100、CIO100.00、W20、H10、E0_100、T0、C0、TU0'
  if (protocol === 'BacnetIp') return '例如：analogInput:1、analogValue:2:presentValue、binaryOutput:3'
  if (protocol === 'Dlt6452007') return '可留空，优先使用表地址和数据标识'
  if (protocol === 'Cjt1882004' || protocol === 'Cjt1882018') return '可留空，优先使用表地址、数据标识和表类型'
  if (protocol === 'OpcUa') return '例如：ns=2;s=Channel.Device.Tag'
  if (protocol === 'OpcDa') return '例如：Random.Int4'
  return '例如：D100'
}

export function meterAddressPlaceholder(protocol: string) {
  if (protocol === 'Dlt6452007') return '例如：000000000001'
  if (protocol === 'Cjt1882004' || protocol === 'Cjt1882018') return '例如：000000000001'
  return '表计通信地址'
}

export function meterDataIdentifierPlaceholder(protocol: string) {
  if (protocol === 'Dlt6452007') return '例如：00010000'
  if (protocol === 'Cjt1882004' || protocol === 'Cjt1882018') return '例如：901F、1F90'
  return '数据标识'
}

function createDefaultScaling(): ScalingConfig {
  return {
    enabled: false,
    multiplier: 1,
    offset: 0,
    clampEnabled: false,
    minValue: 0,
    maxValue: 0,
    decimalPlaces: 2
  }
}

function createDefaultCleaning(): DataCleaningConfig {
  return {
    enabled: false,
    outOfRangeEnabled: false,
    minValue: 0,
    maxValue: 100,
    deadbandEnabled: false,
    deadband: 0,
    duplicateFilterEnabled: false,
    spikeFilterEnabled: false,
    spikeThreshold: 0,
    spikeWindowSeconds: 0,
    enumMappingEnabled: false,
    enumMappings: [],
    unitConversionEnabled: false,
    sourceUnit: '',
    targetUnit: '',
    unitMultiplier: 1,
    unitOffset: 0,
    preserveLastGoodOnFilter: true,
    valueScriptEnabled: false,
    valueScriptId: '',
    valueScriptVersion: 0,
    valueScriptTimeoutMilliseconds: 100,
    valueScriptFailurePolicy: 'KeepLastGood'
  }
}

function normalizeCleaning(cleaning: DataCleaningConfig): DataCleaningConfig {
  cleaning.enabled = cleaning.enabled ?? false
  cleaning.outOfRangeEnabled = cleaning.outOfRangeEnabled ?? false
  cleaning.minValue = Number.isFinite(Number(cleaning.minValue)) ? Number(cleaning.minValue) : 0
  cleaning.maxValue = Number.isFinite(Number(cleaning.maxValue)) ? Number(cleaning.maxValue) : 100
  cleaning.deadbandEnabled = cleaning.deadbandEnabled ?? false
  cleaning.deadband = Math.max(0, Number(cleaning.deadband) || 0)
  cleaning.duplicateFilterEnabled = cleaning.duplicateFilterEnabled ?? false
  cleaning.spikeFilterEnabled = cleaning.spikeFilterEnabled ?? false
  cleaning.spikeThreshold = Math.max(0, Number(cleaning.spikeThreshold) || 0)
  cleaning.spikeWindowSeconds = Math.max(0, Number(cleaning.spikeWindowSeconds) || 0)
  cleaning.enumMappingEnabled = cleaning.enumMappingEnabled ?? false
  cleaning.enumMappings = (cleaning.enumMappings ?? []).map(item => ({
    rawValue: item.rawValue ?? '',
    cleanValue: item.cleanValue ?? '',
    description: item.description ?? ''
  }))
  cleaning.unitConversionEnabled = cleaning.unitConversionEnabled ?? false
  cleaning.sourceUnit = cleaning.sourceUnit ?? ''
  cleaning.targetUnit = cleaning.targetUnit ?? ''
  cleaning.unitMultiplier = Number.isFinite(Number(cleaning.unitMultiplier)) && Number(cleaning.unitMultiplier) !== 0
    ? Number(cleaning.unitMultiplier)
    : 1
  cleaning.unitOffset = Number.isFinite(Number(cleaning.unitOffset)) ? Number(cleaning.unitOffset) : 0
  cleaning.preserveLastGoodOnFilter = cleaning.preserveLastGoodOnFilter ?? true
  cleaning.valueScriptEnabled = cleaning.valueScriptEnabled ?? false
  cleaning.valueScriptId = cleaning.valueScriptId ?? ''
  cleaning.valueScriptVersion = Math.max(0, Number(cleaning.valueScriptVersion) || 0)
  cleaning.valueScriptTimeoutMilliseconds = Math.min(5000, Math.max(10, Number(cleaning.valueScriptTimeoutMilliseconds) || 100))
  cleaning.valueScriptFailurePolicy = cleaning.valueScriptFailurePolicy ?? 'KeepLastGood'
  return cleaning
}

function createDefaultAlarm(): TagAlarmConfig {
  return {
    enabled: false,
    lowLimit: 0,
    highLimit: 0,
    lowAlarmMessage: '',
    highAlarmMessage: '',
    warningDeviation: 0,
    lowWarningMessage: '',
    highWarningMessage: ''
  }
}
