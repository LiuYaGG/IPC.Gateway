import type { DeviceConfig, GatewayConnectionParameterDefinition, GatewayProtocolCatalogItem } from '../api'
import { applyProtocolDefaults } from './deviceDefaults'

export function applyCatalogProtocol(device: DeviceConfig, item: GatewayProtocolCatalogItem) {
  applyProtocolDefaults(device, item.protocol)
  device.connection.protocol = item.protocol
  device.connection.driverId = item.builtIn ? '' : item.driverId
  applyConnectionParameterDefaults(device.connection, item.parameters)
}

export function applyConnectionParameterDefaults(
  connection: DeviceConfig['connection'],
  parameters: GatewayConnectionParameterDefinition[]
) {
  for (const parameter of parameters) {
    if (!parameter.key || parameter.defaultValue === '') continue
    applyConnectionParameterDefault(connection, parameter)
  }
}

function applyConnectionParameterDefault(
  connection: DeviceConfig['connection'],
  parameter: GatewayConnectionParameterDefinition
) {
  const value = parameter.key === 'driverOptions.finsDataFormat'
    ? (connection.wordOrder === 'LowWordFirst' ? 'CDAB' : 'ABCD')
    : parseParameterDefault(parameter)
  if (parameter.key.startsWith('driverOptions.')) {
    const optionKey = parameter.key.slice('driverOptions.'.length)
    const options = readDriverOptionsJson(connection.driverOptionsJson)
    if (shouldApplyParameterDefault(options[optionKey], parameter)) {
      options[optionKey] = value
      connection.driverOptionsJson = JSON.stringify(options)
    }
    return
  }

  const connectionRecord = connection as unknown as Record<string, unknown>
  if (shouldApplyParameterDefault(connectionRecord[parameter.key], parameter)) {
    connectionRecord[parameter.key] = value
  }
}

function shouldApplyParameterDefault(currentValue: unknown, parameter: GatewayConnectionParameterDefinition) {
  if (currentValue === undefined || currentValue === null || currentValue === '') return true
  if (typeof currentValue === 'number' && currentValue === 0 && (parameter.min ?? 1) <= 0) return false
  return typeof currentValue === 'number' && currentValue === 0 && parameter.defaultValue !== '0'
}

function parseParameterDefault(parameter: GatewayConnectionParameterDefinition) {
  const type = (parameter.parameterType || '').toLowerCase()
  if (type === 'number') {
    const value = Number(parameter.defaultValue)
    return Number.isFinite(value) ? value : undefined
  }
  if (type === 'switch') {
    const value = parameter.defaultValue.toLowerCase()
    return value === 'true' || value === '1'
  }
  return parameter.defaultValue
}

function readDriverOptionsJson(json: string) {
  try {
    const parsed = JSON.parse(json || '{}')
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}
