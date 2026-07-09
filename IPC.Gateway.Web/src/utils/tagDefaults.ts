import type { DeviceConfig, GroupConfig, TagAlarmConfig, TagConfig, ScalingConfig, DataCleaningConfig } from '../api'

export const dataTypeOptions = [
  'Bool',
  'Byte',
  'Int16',
  'UInt16',
  'Int32',
  'UInt32',
  'Float',
  'Double',
  'String'
]

export const accessModeOptions = ['ReadOnly', 'ReadWrite', 'WriteOnly']

export const meterProtocolOptions = [
  { label: 'DL/T 645-2007', value: 'Dlt6452007' },
  { label: 'CJ/T 188-2004', value: 'Cjt1882004' }
]

const dlt645MeterTypes = ['电能表', '多功能电表', '智能电表']
const cjt188MeterTypes = ['水表', '热量表', '燃气表', '冷量表']

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
    description: ''
  })
}

export function cloneTag(tag: TagConfig): TagConfig {
  return normalizeTag({
    ...tag,
    scaling: { ...createDefaultScaling(), ...(tag.scaling ?? {}) },
    cleaning: normalizeCleaning({ ...createDefaultCleaning(), ...(tag.cleaning ?? {}) }),
    alarm: { ...createDefaultAlarm(), ...(tag.alarm ?? {}) }
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
  return tag
}

export function isMeterProtocol(protocol: string) {
  return protocol === 'Dlt6452007' || protocol === 'Cjt1882004'
}

export function meterTypeOptions(protocol: string) {
  if (protocol === 'Dlt6452007') return dlt645MeterTypes
  if (protocol === 'Cjt1882004') return cjt188MeterTypes
  return []
}

export function defaultMeterType(protocol: string) {
  return meterTypeOptions(protocol)[0] ?? ''
}

export function tagAddressPlaceholder(protocol: string) {
  if (protocol === 'ModbusTcp' || protocol === 'ModbusRtu') return '例如：40001、00001、3:40001'
  if (protocol === 'SiemensS7') return '例如：DB1.DBW0、M0.0'
  if (protocol === 'MitsubishiMc' || protocol === 'MitsubishiMc1E' || protocol === 'MitsubishiSerial' || protocol === 'MitsubishiQlSerial') return '例如：D100、M100、X0'
  if (protocol === 'CanOpen') return '例如：1:0x6041:0 或 0x6041:0'
  if (protocol === 'OmronFins') return '例如：D100、CIO100'
  if (protocol === 'BacnetIp') return '例如：analogInput:1、analogValue:2:presentValue、binaryOutput:3'
  if (protocol === 'Dlt6452007') return '可留空，优先使用表地址和数据标识'
  if (protocol === 'Cjt1882004') return '可留空，优先使用表地址、数据标识和表类型'
  if (protocol === 'OpcUa') return '例如：ns=2;s=Channel.Device.Tag'
  if (protocol === 'OpcDa') return '例如：Random.Int4'
  return '例如：D100'
}

export function meterAddressPlaceholder(protocol: string) {
  if (protocol === 'Dlt6452007') return '例如：000000000001'
  if (protocol === 'Cjt1882004') return '例如：000000000001'
  return '表计通信地址'
}

export function meterDataIdentifierPlaceholder(protocol: string) {
  if (protocol === 'Dlt6452007') return '例如：00010000'
  if (protocol === 'Cjt1882004') return '例如：901F、1F90'
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
    preserveLastGoodOnFilter: true
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
