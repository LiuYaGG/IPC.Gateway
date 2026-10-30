import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import ts from 'typescript'

const source = await readFile(new URL('../src/utils/tagDefaults.ts', import.meta.url), 'utf8')
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 }
}).outputText
const { dataTypeOptionGroups, dataTypeDisplayName } = await import(
  `data:text/javascript;base64,${Buffer.from(compiled).toString('base64')}`
)
const options = protocol => dataTypeOptionGroups(protocol).flatMap(group => group.options)
for (const protocol of ['SiemensS7', 'RockwellCip', 'EtherNetIp', 'CanOpen', 'BeckhoffAds', 'OpcUa']) {
  const types = options(protocol)
  assert(types.includes('UInt8'), `${protocol}: UInt8 missing`)
  assert(types.includes('UInt8Array'), `${protocol}: UInt8Array missing`)
  assert(types.indexOf('Bool') < types.indexOf('UInt8'))
  assert(types.indexOf('UInt8') < types.indexOf('Int16'))
  assert(types.indexOf('BoolArray') < types.indexOf('UInt8Array'))
  assert(types.indexOf('UInt8Array') < types.indexOf('Int16Array'))
  assert.equal(new Set(types).size, types.length)
}
for (const protocol of ['Snmp', 'Dnp3']) {
  assert(options(protocol).includes('UInt8'))
  assert(!options(protocol).includes('UInt8Array'))
}
for (const protocol of [
  'ModbusTcp', 'ModbusRtu', 'ModbusAscii', 'MitsubishiMc', 'MitsubishiMc1E',
  'MitsubishiSerial', 'MitsubishiQlSerial', 'OmronFins', 'RockwellPccc',
  'BacnetIp', 'Dlt6452007', 'Cjt1882004', 'Cjt1882018', 'OpcDa', 'VirtualPlc', 'Plugin'
]) {
  assert(!options(protocol).includes('UInt8'), `${protocol}: unsupported UInt8 exposed`)
  assert(!options(protocol).includes('UInt8Array'), `${protocol}: unsupported UInt8Array exposed`)
}
assert.equal(dataTypeDisplayName('UInt8'), 'UInt8')
assert.equal(dataTypeDisplayName('UInt8Array'), 'UInt8Array')
assert.equal(dataTypeDisplayName('Byte'), 'Byte')
assert.equal(dataTypeDisplayName('ByteArray'), 'ByteArray')
assert.equal(dataTypeDisplayName('Int8'), 'Int8')
for (const mode of ['Text', 'SparkplugB', 'Json']) {
  const types = dataTypeOptionGroups('MqttClient', JSON.stringify({ mqttPayloadMode: mode })).flatMap(group => group.options)
  assert(types.includes('UInt8'))
  assert.equal(types.includes('UInt8Array'), mode === 'Json')
}
assert(!options('MqttClient').includes('UInt8Array'))
for (const protocol of ['SiemensS7', 'RockwellCip', 'EtherNetIp', 'BeckhoffAds', 'OpcUa']) {
  const types = options(protocol)
  for (const suffix of ['', 'Array']) {
    assert(types.includes(`Byte${suffix}`), `${protocol}: Byte${suffix} missing`)
    assert(types.indexOf(`Bool${suffix}`) < types.indexOf(`Byte${suffix}`))
    assert(types.indexOf(`Byte${suffix}`) < types.indexOf(`UInt8${suffix}`))
    if (types.includes(`Int8${suffix}`)) {
      assert(types.indexOf(`UInt8${suffix}`) < types.indexOf(`Int8${suffix}`))
      assert(types.indexOf(`Int8${suffix}`) < types.indexOf(`Int16${suffix}`))
    }
  }
}
for (const protocol of ['CanOpen', 'MqttClient', 'Snmp', 'Dnp3', 'ModbusTcp', 'ModbusRtu', 'ModbusAscii',
  'MitsubishiMc', 'MitsubishiMc1E', 'MitsubishiSerial', 'MitsubishiQlSerial', 'OmronFins',
  'RockwellPccc', 'BacnetIp', 'Dlt6452007', 'Cjt1882004', 'Cjt1882018', 'OpcDa', 'VirtualPlc', 'Plugin']) {
  assert(!options(protocol).includes('Byte'), `${protocol}: unsupported Byte exposed`)
  assert(!options(protocol).includes('ByteArray'), `${protocol}: unsupported ByteArray exposed`)
}
console.log('PASS byte type visibility, ordering and legacy configuration names')
