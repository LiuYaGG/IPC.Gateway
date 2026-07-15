import type { DeviceConfig, PlcConnection } from '../api'
import { cloneTag } from './tagDefaults'

export const protocolOptions = [
  { label: 'Virtual PLC', value: 'VirtualPlc', category: 'simulated' },
  { label: 'Modbus TCP', value: 'ModbusTcp', category: 'network' },
  { label: 'Modbus RTU', value: 'ModbusRtu', category: 'serial' },
  { label: 'Modbus ASCII', value: 'ModbusAscii', category: 'serial' },
  { label: 'DL/T 645-2007', value: 'Dlt6452007', category: 'meter' },
  { label: 'CJ/T 188-2004', value: 'Cjt1882004', category: 'meter' },
  { label: 'CJ/T 188-2018', value: 'Cjt1882018', category: 'meter' },
  { label: 'Siemens S7', value: 'SiemensS7', category: 'network' },
  { label: 'Rockwell CIP', value: 'RockwellCip', category: 'network' },
  { label: 'EtherNet/IP', value: 'EtherNetIp', category: 'network' },
  { label: 'Beckhoff TwinCAT ADS', value: 'BeckhoffAds', category: 'network' },
  { label: 'SNMP v1/v2c/v3', value: 'Snmp', category: 'network' },
  { label: 'MQTT / Sparkplug B 南向', value: 'MqttClient', category: 'network' },
  { label: 'DNP3 TCP Master', value: 'Dnp3', category: 'network' },
  { label: 'Rockwell PCCC', value: 'RockwellPccc', category: 'network' },
  { label: 'Mitsubishi MC', value: 'MitsubishiMc', category: 'network' },
  { label: 'Mitsubishi 1E', value: 'MitsubishiMc1E', category: 'network' },
  { label: 'Mitsubishi Serial', value: 'MitsubishiSerial', category: 'serial' },
  { label: 'Mitsubishi QL Serial', value: 'MitsubishiQlSerial', category: 'serial' },
  { label: 'CANopen', value: 'CanOpen', category: 'serial' },
  { label: 'Omron FINS', value: 'OmronFins', category: 'network' },
  { label: 'BACnet/IP', value: 'BacnetIp', category: 'network' },
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
    opcUaSecurityPolicy: 'None',
    opcUaMessageSecurityMode: 'None',
    opcUaAutoTrustServerCertificate: false,
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
    channelId: '',
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
  device.channelId = device.channelId || ''
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
  applyProtocolDefaultsToConnection(device.connection, protocol, true)
}

export function protocolLabel(protocol: string) {
  return protocolOptions.find(item => item.value === protocol)?.label ?? protocol
}

export function isSerialProtocol(protocol: string) {
  return ['ModbusRtu', 'ModbusAscii', 'MitsubishiSerial', 'MitsubishiQlSerial', 'CanOpen'].includes(protocol)
}

export function isNetworkProtocol(protocol: string) {
  return ['ModbusTcp', 'SiemensS7', 'RockwellCip', 'EtherNetIp', 'RockwellPccc', 'BeckhoffAds', 'Snmp', 'MqttClient', 'Dnp3', 'MitsubishiMc', 'MitsubishiMc1E', 'OmronFins', 'BacnetIp', 'OpcUa', 'Dlt6452007', 'Cjt1882004', 'Cjt1882018'].includes(protocol)
}

export function defaultPortForProtocolTransport(protocol: string, transport = 'Tcp') {
  if (protocol === 'SiemensS7') return 102
  if (protocol === 'MitsubishiMc' || protocol === 'MitsubishiMc1E') {
    return transport.toLowerCase() === 'udp' ? 5000 : 5001
  }
  if (protocol === 'BacnetIp') return 47808
  if (protocol === 'RockwellCip' || protocol === 'EtherNetIp' || protocol === 'RockwellPccc') return 44818
  if (protocol === 'Dlt6452007' || protocol === 'Cjt1882004' || protocol === 'Cjt1882018') return 4001
  return undefined
}

function applyProtocolDefaultsToConnection(connection: PlcConnection, protocol: string, overwriteNetwork = false) {
  connection.protocol = protocol
  if (protocol === 'VirtualPlc') {
    connection.host = connection.host || 'default'
    connection.port = 0
    return
  }
  if (protocol === 'ModbusTcp') {
    setNetworkDefaults(connection, '127.0.0.1', 502, 'Tcp', overwriteNetwork)
    connection.transport = 'Tcp'
  }
  else if (protocol === 'SiemensS7') {
    setNetworkDefaults(connection, '127.0.0.1', 102, 'Tcp', overwriteNetwork)
    connection.rack = overwriteNetwork ? 0 : connection.rack ?? 0
    connection.slot = overwriteNetwork ? 1 : connection.slot || 1
    const defaults = '{"controllerProfile":"Auto","s7TsapMode":"RackSlot","s7ConnectionType":"PG","s7LocalTsap":"0100","s7RemoteTsap":"0101","s7MaxItemsPerRequest":20}'
    connection.driverOptionsJson = overwriteNetwork ? defaults : connection.driverOptionsJson || defaults
  } else if (protocol === 'RockwellCip' || protocol === 'RockwellPccc') setNetworkDefaults(connection, '127.0.0.1', 44818, 'Tcp', overwriteNetwork)
  else if (protocol === 'EtherNetIp') {
    setNetworkDefaults(connection, '127.0.0.1', 44818, 'Tcp', overwriteNetwork)
    const defaults = '{"cipRouteMode":"Direct","cipRoutePath":"","cipMaxItemsPerRequest":20,"eipIoMode":"Explicit","eipOutputAssembly":100,"eipInputAssembly":101,"eipConfigurationAssembly":1,"eipOutputLength":0,"eipInputLength":0,"eipRpiMilliseconds":100,"eipOutputRealTimeFormat":"Header32Bit","eipInputRealTimeFormat":"Modeless","eipInputConnectionType":"PointToPoint","eipInputDataOffset":8,"eipOutputDataOffset":0,"eipIoStaleTimeoutMilliseconds":1000}'
    connection.driverOptionsJson = overwriteNetwork ? defaults : connection.driverOptionsJson || defaults
  }
  else if (protocol === 'BeckhoffAds') {
    setNetworkDefaults(connection, '127.0.0.1', 48898, 'Tcp', overwriteNetwork)
    const defaults = '{"amsNetId":"","adsPort":851,"adsStringLength":80,"adsMaxBatchItems":100}'
    connection.driverOptionsJson = overwriteNetwork ? defaults : connection.driverOptionsJson || defaults
  }
  else if (protocol === 'Snmp') {
    setNetworkDefaults(connection, '127.0.0.1', 161, 'Udp', overwriteNetwork)
    const defaults = '{"snmpVersion":"V2c","snmpCommunity":"public","snmpUserName":"","snmpAuthProtocol":"None","snmpAuthPassword":"","snmpPrivacyProtocol":"None","snmpPrivacyPassword":"","snmpContextName":"","snmpMaxOidsPerRequest":40}'
    connection.driverOptionsJson = overwriteNetwork ? defaults : connection.driverOptionsJson || defaults
  }
  else if (protocol === 'MqttClient') {
    setNetworkDefaults(connection, '127.0.0.1', 1883, 'Tcp', overwriteNetwork)
    const defaults = '{"mqttClientId":"IPC-Gateway-Southbound","mqttSubscribeFilter":"#","mqttPayloadMode":"Text","mqttUseTls":false,"mqttAllowUntrustedCertificates":false,"mqttQos":0,"mqttMaxValueAgeSeconds":0}'
    connection.driverOptionsJson = overwriteNetwork ? defaults : connection.driverOptionsJson || defaults
  }
  else if (protocol === 'Dnp3') {
    setNetworkDefaults(connection, '127.0.0.1', 20000, 'Tcp', overwriteNetwork)
    const defaults = '{"dnp3LocalAddress":1,"dnp3RemoteAddress":1024,"dnp3ScanGapLimit":32,"dnp3SelectBeforeOperate":true,"dnp3StartupIntegrity":true,"dnp3EnableUnsolicited":true,"dnp3EventScanIntervalSeconds":5,"dnp3IntegrityScanIntervalSeconds":900,"dnp3CacheMaxAgeMilliseconds":0,"dnp3TimeSyncMode":"None"}'
    connection.driverOptionsJson = overwriteNetwork ? defaults : connection.driverOptionsJson || defaults
  }
  else if (protocol === 'MitsubishiMc' || protocol === 'MitsubishiMc1E') setNetworkDefaults(connection, '127.0.0.1', defaultPortForProtocolTransport(protocol, 'Tcp') ?? 5001, 'Tcp', overwriteNetwork)
  else if (protocol === 'OmronFins') {
    setNetworkDefaults(connection, '127.0.0.1', 9600, 'Udp', overwriteNetwork)
    const defaults = '{"controllerProfile":"Auto","sourceNode":0,"destinationNode":0,"sourceNetwork":0,"network":0,"sourceUnit":0,"destinationUnit":0,"maxWordCount":240,"maxBitCount":480,"maxGapWords":4,"maxEmBank":24,"udpReadRetries":1}'
    connection.driverOptionsJson = overwriteNetwork ? defaults : connection.driverOptionsJson || defaults
  }
  else if (protocol === 'BacnetIp') setNetworkDefaults(connection, '127.0.0.1', 47808, 'Udp', overwriteNetwork)
  else if (protocol === 'Dlt6452007' || protocol === 'Cjt1882004' || protocol === 'Cjt1882018') {
    setNetworkDefaults(connection, '127.0.0.1', 4001, 'Tcp', overwriteNetwork)
  }
  else if (protocol === 'OpcUa') setNetworkDefaults(connection, 'opc.tcp://127.0.0.1', 49320, 'Tcp', overwriteNetwork)
  else if (protocol === 'OpcDa') {
    connection.host = connection.host || 'localhost'
    connection.opcDaServerProgId = overwriteNetwork ? 'Kepware.KEPServerEX.V6' : connection.opcDaServerProgId || 'Kepware.KEPServerEX.V6'
    connection.opcDaGroupName = connection.opcDaGroupName || 'IPC'
  } else if (isSerialProtocol(protocol)) {
    connection.host = overwriteNetwork ? 'COM1' : connection.host || 'COM1'
    connection.port = overwriteNetwork ? (protocol === 'CanOpen' ? 115200 : 9600) : connection.port || (protocol === 'CanOpen' ? 115200 : 9600)
    const ascii = protocol === 'ModbusAscii'
    connection.dataBits = overwriteNetwork ? (ascii ? 7 : 8) : connection.dataBits || (ascii ? 7 : 8)
    connection.serialParity = overwriteNetwork ? (ascii ? 'Even' : 'None') : connection.serialParity || (ascii ? 'Even' : 'None')
    connection.serialStopBits = overwriteNetwork ? 'One' : connection.serialStopBits || 'One'
    if (protocol === 'CanOpen') {
      connection.driverOptionsJson = overwriteNetwork
        ? '{"adapter":"SLCAN","canBitRate":500000,"defaultNodeId":1,"maxBatchItems":32,"probeNodeOnConnect":true,"startNodeOnConnect":false,"resetCommunicationOnConnect":false,"heartbeatTimeoutMilliseconds":3000,"pdoMaxAgeMilliseconds":3000,"syncIntervalMilliseconds":0}'
        : connection.driverOptionsJson || '{"adapter":"SLCAN","canBitRate":500000,"defaultNodeId":1,"maxBatchItems":32,"probeNodeOnConnect":true,"startNodeOnConnect":false,"resetCommunicationOnConnect":false,"heartbeatTimeoutMilliseconds":3000,"pdoMaxAgeMilliseconds":3000,"syncIntervalMilliseconds":0}'
    }
  } else if (protocol === 'Plugin') {
    connection.driverId = connection.driverId || ''
    connection.driverOptionsJson = connection.driverOptionsJson || '{}'
  }
}

function setNetworkDefaults(connection: PlcConnection, host: string, port: number, transport: string, overwrite = false) {
  connection.host = overwrite ? host : connection.host || host
  connection.port = overwrite ? port : connection.port || port
  connection.transport = overwrite ? transport : connection.transport || transport
}
