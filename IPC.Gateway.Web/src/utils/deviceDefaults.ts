import type { DeviceConfig, PlcConnection } from '../api'
import { cloneTag } from './tagDefaults'

export const protocolOptions = [
  { label: 'Virtual PLC', value: 'VirtualPlc', category: 'simulated' },
  { label: 'Modbus TCP', value: 'ModbusTcp', category: 'network' },
  { label: 'Modbus RTU', value: 'ModbusRtu', category: 'serial' },
  { label: 'DL/T 645-2007', value: 'Dlt6452007', category: 'meter' },
  { label: 'CJ/T 188-2004', value: 'Cjt1882004', category: 'meter' },
  { label: 'Siemens S7', value: 'SiemensS7', category: 'network' },
  { label: 'Rockwell CIP', value: 'RockwellCip', category: 'network' },
  { label: 'Mitsubishi MC', value: 'MitsubishiMc', category: 'network' },
  { label: 'Mitsubishi 1E', value: 'MitsubishiMc1E', category: 'network' },
  { label: 'Mitsubishi Serial', value: 'MitsubishiSerial', category: 'serial' },
  { label: 'Mitsubishi QL Serial', value: 'MitsubishiQlSerial', category: 'serial' },
  { label: 'Omron FINS', value: 'OmronFins', category: 'network' },
  { label: 'OPC UA', value: 'OpcUa', category: 'network' },
  { label: 'OPC DA', value: 'OpcDa', category: 'opc' },
  { label: 'Plugin Driver', value: 'Plugin', category: 'plugin' }
]

export const parityOptions = ['None', 'Odd', 'Even', 'Mark', 'Space']
export const stopBitsOptions = ['One', 'Two', 'OnePointFive', 'None']
export const wordOrderOptions = ['HighWordFirst', 'LowWordFirst']
export const transportOptions = ['Tcp', 'Udp']

export function createDefaultConnection(protocol = 'ModbusTcp'): PlcConnection {
  const connection: PlcConnection = {
    protocol,
    host: '',
    port: 0,
    rack: 0,
    slot: 0,
    timeoutMilliseconds: 3000,
    wordOrder: 'HighWordFirst',
    transport: 'Tcp',
    dataBits: 8,
    serialParity: 'None',
    serialStopBits: 'One',
    username: '',
    password: '',
    certificatePath: '',
    certificatePassword: '',
    certificateThumbprint: '',
    trustStorePath: '',
    validateServerCertificate: true,
    opcDaServerProgId: '',
    opcDaGroupName: 'IPC',
    driverId: '',
    driverOptionsJson: ''
  }
  applyProtocolDefaultsToConnection(connection, protocol)
  return connection
}

export function createDeviceDraft(): DeviceConfig {
  return {
    id: '',
    name: '',
    enabled: true,
    protocol: 'ModbusTcp',
    connection: createDefaultConnection('ModbusTcp'),
    defaultScanRateMs: 1000,
    failureRetryDelayMs: 1000,
    maxFailureRetryDelayMs: 30000,
    tags: [],
    groups: []
  }
}

export function cloneDevice(device: DeviceConfig): DeviceConfig {
  return normalizeDevice({
    ...device,
    connection: { ...(device.connection ?? createDefaultConnection(device.protocol)) },
    tags: (device.tags ?? []).map(cloneTag),
    groups: (device.groups ?? []).map(group => ({
      ...group,
      tags: (group.tags ?? []).map(cloneTag)
    }))
  })
}

export function normalizeDevice(device: DeviceConfig): DeviceConfig {
  const protocol = device.protocol || device.connection?.protocol || 'ModbusTcp'
  device.id = device.id || ''
  device.name = device.name || ''
  device.enabled = device.enabled ?? true
  device.protocol = protocol
  device.connection = { ...createDefaultConnection(protocol), ...(device.connection ?? {}), protocol }
  device.defaultScanRateMs = device.defaultScanRateMs || 1000
  device.failureRetryDelayMs = device.failureRetryDelayMs || 1000
  device.maxFailureRetryDelayMs = device.maxFailureRetryDelayMs || 30000
  device.tags = device.tags ?? []
  device.groups = device.groups ?? []
  return device
}

export function applyProtocolDefaults(device: DeviceConfig, protocol: string) {
  device.protocol = protocol
  device.connection = { ...createDefaultConnection(protocol), ...(device.connection ?? {}), protocol }
  applyProtocolDefaultsToConnection(device.connection, protocol)
}

export function protocolLabel(protocol: string) {
  return protocolOptions.find(item => item.value === protocol)?.label ?? protocol
}

export function isSerialProtocol(protocol: string) {
  return ['ModbusRtu', 'MitsubishiSerial', 'MitsubishiQlSerial', 'Dlt6452007', 'Cjt1882004'].includes(protocol)
}

export function isNetworkProtocol(protocol: string) {
  return ['ModbusTcp', 'SiemensS7', 'RockwellCip', 'MitsubishiMc', 'MitsubishiMc1E', 'OmronFins', 'OpcUa'].includes(protocol)
}

function applyProtocolDefaultsToConnection(connection: PlcConnection, protocol: string) {
  connection.protocol = protocol
  if (protocol === 'VirtualPlc') {
    connection.host = connection.host || 'default'
    connection.port = 0
    return
  }
  if (protocol === 'ModbusTcp') setNetworkDefaults(connection, '127.0.0.1', 502, 'Tcp')
  else if (protocol === 'SiemensS7') {
    setNetworkDefaults(connection, '127.0.0.1', 102, 'Tcp')
    connection.rack = connection.rack ?? 0
    connection.slot = connection.slot || 1
  } else if (protocol === 'RockwellCip') setNetworkDefaults(connection, '127.0.0.1', 44818, 'Tcp')
  else if (protocol === 'MitsubishiMc' || protocol === 'MitsubishiMc1E') setNetworkDefaults(connection, '127.0.0.1', 5000, 'Tcp')
  else if (protocol === 'OmronFins') setNetworkDefaults(connection, '127.0.0.1', 9600, 'Udp')
  else if (protocol === 'OpcUa') setNetworkDefaults(connection, 'opc.tcp://127.0.0.1', 4840, 'Tcp')
  else if (protocol === 'OpcDa') {
    connection.host = connection.host || 'localhost'
    connection.opcDaServerProgId = connection.opcDaServerProgId || 'Matrikon.OPC.Simulation.1'
    connection.opcDaGroupName = connection.opcDaGroupName || 'IPC'
  } else if (isSerialProtocol(protocol)) {
    connection.host = connection.host || 'COM1'
    connection.port = connection.port || 9600
    connection.dataBits = connection.dataBits || 8
    connection.serialParity = connection.serialParity || 'None'
    connection.serialStopBits = connection.serialStopBits || 'One'
  } else if (protocol === 'Plugin') {
    connection.driverId = connection.driverId || ''
    connection.driverOptionsJson = connection.driverOptionsJson || '{}'
  }
}

function setNetworkDefaults(connection: PlcConnection, host: string, port: number, transport: string) {
  connection.host = connection.host || host
  connection.port = connection.port || port
  connection.transport = connection.transport || transport
}
