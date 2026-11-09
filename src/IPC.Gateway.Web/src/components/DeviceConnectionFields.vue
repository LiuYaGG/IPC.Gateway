<template>
  <div class="connection-fields">
    <template v-if="connectionParameters.length">
      <section
        v-for="(parameterGroup, groupIndex) in connectionParameterGroups"
        :key="parameterGroup.name || `default-${groupIndex}`"
        class="connection-parameter-section"
      >
        <div v-if="parameterGroup.name" class="connection-parameter-section__title">{{ tx(parameterGroup.name) }}</div>
        <div class="form-grid">
          <a-form-item
            v-for="(parameter, parameterIndex) in parameterGroup.parameters"
            :key="parameter.key"
            :label="tx(parameter.label || parameter.key)"
            :required="parameter.required"
            :class="{ 'connection-parameter--wide': parameterType(parameter) === 'textarea' }"
          >
            <div class="connection-parameter__control">
              <gateway-number-input
                v-if="parameterType(parameter) === 'number'"
                :model-value="numberParameterValue(parameter)"
                :min="parameter.min ?? undefined"
                :max="parameter.max ?? undefined"
                :disabled="parameter.readOnly"
                @update:model-value="updateNumberParameter(parameter, $event)"
              />
              <gateway-select
                v-else-if="parameterType(parameter) === 'select'"
                :model-value="textParameterValue(parameter)"
                :disabled="parameter.readOnly"
                @update:model-value="updateTextParameter(parameter, $event)"
              >
                <a-select-option v-for="item in parameter.options" :key="item" :label="tx(item)" :value="item">
                  {{ item }}
                </a-select-option>
              </gateway-select>
              <a-switch
                v-else-if="parameterType(parameter) === 'switch'"
                :checked="switchParameterValue(parameter)"
                :disabled="parameter.readOnly"
                @update:checked="updateSwitchParameter(parameter, $event)"
              />
              <gateway-input
                v-else
                :model-value="textParameterValue(parameter)"
                :type="
                  parameterType(parameter) === 'password'
                    ? 'password'
                    : parameterType(parameter) === 'textarea'
                      ? 'textarea'
                      : 'text'
                "
                :rows="parameterType(parameter) === 'textarea' ? 2 : undefined"
                :placeholder="tx(parameter.placeholder)"
                :disabled="parameter.readOnly"
                :show-password="parameterType(parameter) === 'password'"
                :name="connectionInputName(groupIndex * 100 + parameterIndex)"
                :autocomplete="connectionInputAutocomplete(parameter)"
                @update:model-value="updateTextParameter(parameter, $event)"
              />
              <div v-if="parameter.helpText" class="connection-parameter__help">{{ tx(parameter.helpText) }}</div>
            </div>
          </a-form-item>
        </div>
      </section>
      <a-alert
        v-if="protocol === 'SiemensS7'"
        class="s7-compatibility-notice"
        :message="tx('经典 S7 兼容说明')"
        :description="
          tx(
            '支持 DB、M、I/Q、E/A、V 区绝对地址。S7-1200/1500 需要允许 PUT/GET，并将目标 DB 设为非优化访问；优化 DB、符号变量或安全通信请使用 OPC UA。'
          )
        "
        type="info"
        :closable="false"
        show-icon
      />
      <a-alert
        v-if="protocol === 'EtherNetIp' && etherNetIpIoMode === 'implicit'"
        class="protocol-compatibility-notice"
        :message="tx('Class 1 周期 I/O 地址')"
        :description="
          tx(
            '输入标签使用 Input:字节偏移[.位]，输出标签使用 Output:字节偏移[.位]。装配实例、数据长度、RPI 与实时格式必须和目标设备的 EDS/手册一致。'
          )
        "
        type="info"
        :closable="false"
        show-icon
      />
      <a-alert
        v-if="protocol === 'CanOpen'"
        class="protocol-compatibility-notice"
        :message="tx('CANopen 服务地址')"
        :description="
          tx(
            '支持 SDO、TPDO/RPDO、Heartbeat、EMCY、NMT、SYNC 和 TIME；PDO 字节布局需要与设备对象字典及映射参数保持一致。'
          )
        "
        type="info"
        :closable="false"
        show-icon
      />
      <a-alert
        v-if="protocol === 'Dlt6452007' || protocol === 'Cjt1882004' || protocol === 'Cjt1882018'"
        class="protocol-compatibility-notice"
        :message="tx('表地址、数据标识、表类型在标签配置中维护。')"
        type="info"
        :closable="false"
        show-icon
      />
    </template>
    <template v-else-if="protocol === 'VirtualPlc'">
      <a-form-item :label="tx('虚拟源')">
        <gateway-input v-model="device.connection.host" placeholder="default" />
      </a-form-item>
      <a-form-item :label="tx('超时(ms)')">
        <gateway-number-input v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
      </a-form-item>
    </template>
    <template v-else-if="isNetworkProtocol(protocol)">
      <div class="form-grid">
        <a-form-item :label="tx(networkHostLabel)">
          <gateway-input v-model="device.connection.host" :placeholder="tx(networkHostPlaceholder)" />
        </a-form-item>
        <a-form-item :label="tx(networkPortLabel)">
          <gateway-number-input v-model="device.connection.port" :min="0" :max="65535" />
        </a-form-item>
        <a-form-item :label="tx('传输')">
          <gateway-select v-model="device.connection.transport" :disabled="transportLocked">
            <a-select-option v-for="item in transportOptions" :key="item" :label="tx(item)" :value="item">
              {{ item }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('超时(ms)')">
          <gateway-number-input v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
        </a-form-item>
      </div>
      <div v-if="protocol === 'SiemensS7'" class="form-grid">
        <a-form-item label="Rack">
          <gateway-number-input v-model="device.connection.rack" :min="0" :max="16" />
        </a-form-item>
        <a-form-item label="Slot">
          <gateway-number-input v-model="device.connection.slot" :min="0" :max="16" />
        </a-form-item>
      </div>
      <div v-if="['ModbusTcp', 'MitsubishiMc', 'MitsubishiMc1E'].includes(protocol)" class="form-grid">
        <a-form-item :label="tx('字序')">
          <gateway-select v-model="device.connection.wordOrder">
            <a-select-option v-for="item in wordOrderOptions" :key="item" :label="tx(item)" :value="item">
              {{ item }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
      </div>
      <div v-if="protocol === 'OpcUa'" class="form-grid">
        <a-form-item :label="tx('用户名')">
          <gateway-input v-model="device.connection.username" name="opcua-auth-principal" autocomplete="off" />
        </a-form-item>
        <a-form-item :label="tx('密码')">
          <gateway-input
            v-model="device.connection.password"
            type="password"
            show-password
            name="opcua-auth-secret"
            autocomplete="new-password"
          />
        </a-form-item>
        <a-form-item :label="tx('安全策略')">
          <gateway-select v-model="device.connection.opcUaSecurityPolicy">
            <a-select-option v-for="item in opcUaSecurityPolicies" :key="item" :label="tx(item)" :value="item">
              {{ item }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('消息安全模式')">
          <gateway-select v-model="device.connection.opcUaMessageSecurityMode">
            <a-select-option v-for="item in opcUaMessageSecurityModes" :key="item" :label="tx(item)" :value="item">
              {{ item }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('自动信任证书')">
          <a-switch v-model:checked="device.connection.opcUaAutoTrustServerCertificate" />
        </a-form-item>
      </div>
      <a-form-item v-if="protocol === 'OmronFins'" :label="tx('FINS 参数')">
        <gateway-input
          v-model="device.connection.driverOptionsJson"
          type="textarea"
          :auto-size="{ minRows: 3, maxRows: 6 }"
          class="json-editor"
          :placeholder="tx('例如：{\u0022sourceNode\u0022:1,\u0022destinationNode\u0022:10,\u0022network\u0022:0}')"
        />
      </a-form-item>
      <a-alert
        v-if="protocol === 'Dlt6452007' || protocol === 'Cjt1882004' || protocol === 'Cjt1882018'"
        class="protocol-compatibility-notice"
        :message="tx('当前表计驱动使用串口服务器 TCP 透明传输；表地址、数据标识、表类型在标签配置中维护。')"
        type="info"
        :closable="false"
        show-icon
      />
    </template>
    <template v-else-if="isSerialProtocol(protocol)">
      <div class="form-grid">
        <a-form-item :label="tx(serialHostLabel)">
          <gateway-input v-model="device.connection.host" placeholder="COM1" />
        </a-form-item>
        <a-form-item :label="tx(serialPortLabel)">
          <gateway-number-input v-model="device.connection.port" :min="1200" :max="921600" />
        </a-form-item>
        <a-form-item :label="tx('数据位')">
          <gateway-number-input v-model="device.connection.dataBits" :min="5" :max="8" />
        </a-form-item>
        <a-form-item :label="tx('校验')">
          <gateway-select v-model="device.connection.serialParity">
            <a-select-option v-for="item in parityOptions" :key="item" :label="tx(item)" :value="item">
              {{ item }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('停止位')">
          <gateway-select v-model="device.connection.serialStopBits">
            <a-select-option v-for="item in stopBitsOptions" :key="item" :label="tx(item)" :value="item">
              {{ item }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('超时(ms)')">
          <gateway-number-input v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
        </a-form-item>
      </div>
      <div
        v-if="['ModbusRtu', 'ModbusAscii', 'MitsubishiSerial', 'MitsubishiQlSerial'].includes(protocol)"
        class="form-grid"
      >
        <a-form-item :label="tx('字序')">
          <gateway-select v-model="device.connection.wordOrder">
            <a-select-option v-for="item in wordOrderOptions" :key="item" :label="tx(item)" :value="item">
              {{ item }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
      </div>
      <a-form-item v-if="protocol === 'CanOpen'" :label="tx('CANopen 参数')">
        <gateway-input
          v-model="device.connection.driverOptionsJson"
          type="textarea"
          :auto-size="{ minRows: 3, maxRows: 6 }"
          class="json-editor"
          :placeholder="
            tx(
              '例如：{\u0022adapter\u0022:\u0022SLCAN\u0022,\u0022canBitRate\u0022:500000,\u0022defaultNodeId\u0022:1,\u0022maxBatchItems\u0022:32}'
            )
          "
        />
      </a-form-item>
      <a-alert
        v-if="protocol === 'Dlt6452007' || protocol === 'Cjt1882004' || protocol === 'Cjt1882018'"
        :message="tx('表地址、数据标识、表类型仍在标签配置里维护。')"
        type="info"
        :closable="false"
      />
    </template>
    <template v-else-if="protocol === 'OpcDa'">
      <div class="form-grid">
        <a-form-item :label="tx('主机')">
          <gateway-input v-model="device.connection.host" placeholder="localhost" />
        </a-form-item>
        <a-form-item label="Server ProgID"><gateway-input v-model="device.connection.opcDaServerProgId" /></a-form-item>
        <a-form-item label="Group"><gateway-input v-model="device.connection.opcDaGroupName" /></a-form-item>
        <a-form-item :label="tx('超时(ms)')">
          <gateway-number-input v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
        </a-form-item>
      </div>
    </template>
    <template v-else-if="protocol === 'Plugin'">
      <a-form-item :label="tx('驱动 ID')">
        <gateway-input v-model="device.connection.driverId" :placeholder="tx('插件驱动标识')" />
      </a-form-item>
      <a-form-item :label="tx('驱动参数 JSON')">
        <gateway-input
          v-model="device.connection.driverOptionsJson"
          type="textarea"
          :auto-size="{ minRows: 5, maxRows: 10 }"
          class="json-editor"
        />
      </a-form-item>
      <div class="form-grid">
        <a-form-item :label="tx('主机')"><gateway-input v-model="device.connection.host" /></a-form-item>
        <a-form-item :label="tx('端口')">
          <gateway-number-input v-model="device.connection.port" :min="0" :max="65535" />
        </a-form-item>
        <a-form-item :label="tx('传输')">
          <gateway-select v-model="device.connection.transport">
            <a-select-option v-for="item in transportOptions" :key="item" :label="tx(item)" :value="item">
              {{ item }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('超时(ms)')">
          <gateway-number-input v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
        </a-form-item>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { tx } from '../i18n'
import { computed, watch } from 'vue'
import type { DeviceConfig, GatewayConnectionParameterDefinition } from '../api'
import {
  defaultPortForProtocolTransport,
  isNetworkProtocol,
  isSerialProtocol,
  parityOptions,
  stopBitsOptions,
  transportOptions,
  wordOrderOptions
} from '../utils/deviceDefaults'
import { applyRockwellControllerProfile, type RockwellLogixProfileSettings } from '../utils/rockwellProfile'

const props = defineProps<{
  device: DeviceConfig
  protocol: string
  parameters?: GatewayConnectionParameterDefinition[]
}>()

let rememberedRockwellLogixSettings: RockwellLogixProfileSettings | undefined

const connectionParameters = computed(() => (props.parameters ?? []).filter((parameter) => parameter.key))
const etherNetIpIoMode = computed(() =>
  String(getParameterValueByKey('driverOptions.eipIoMode') || 'Explicit').toLowerCase()
)
const visibleConnectionParameters = computed(() =>
  connectionParameters.value.filter((parameter) => {
    if (parameter.key === 'driverOptions.cipRoutePath') {
      return String(getParameterValueByKey('driverOptions.cipRouteMode') || 'Slot').toLowerCase() === 'custom'
    }
    if ((props.protocol === 'RockwellCip' || props.protocol === 'EtherNetIp') && parameter.key === 'slot') {
      return String(getParameterValueByKey('driverOptions.cipRouteMode') || 'Slot').toLowerCase() === 'slot'
    }
    if (
      props.protocol === 'EtherNetIp' &&
      parameter.key.startsWith('driverOptions.eip') &&
      parameter.key !== 'driverOptions.eipIoMode'
    ) {
      return etherNetIpIoMode.value === 'implicit'
    }
    if (props.protocol === 'SiemensS7') {
      const customTsap =
        String(getParameterValueByKey('driverOptions.s7TsapMode') || 'RackSlot').toLowerCase() === 'custom'
      if (parameter.key === 'driverOptions.s7LocalTsap' || parameter.key === 'driverOptions.s7RemoteTsap')
        return customTsap
      if (parameter.key === 'rack' || parameter.key === 'slot' || parameter.key === 'driverOptions.s7ConnectionType')
        return !customTsap
    }
    return true
  })
)
const connectionParameterGroups = computed(() => {
  const groups: Array<{ name: string; parameters: GatewayConnectionParameterDefinition[] }> = []
  for (const parameter of visibleConnectionParameters.value) {
    const name = parameter.group || ''
    let group = groups.find((item) => item.name === name)
    if (!group) {
      group = { name, parameters: [] }
      groups.push(group)
    }
    group.parameters.push(parameter)
  }
  return groups
})
const opcUaSecurityPolicies = [
  'None',
  'Basic128Rsa15',
  'Basic256',
  'Basic256Sha256',
  'Aes128_Sha256_RsaOaep',
  'Aes256_Sha256_RsaPss'
]
const opcUaMessageSecurityModes = ['None', 'Sign', 'SignAndEncrypt']

const networkHostLabel = computed(() => {
  if (props.protocol === 'OpcUa') return 'Endpoint'
  if (props.protocol === 'OmronFins') return 'PLC 地址'
  if (props.protocol === 'BacnetIp') return 'BACnet 地址'
  return '主机 / 地址'
})

const networkHostPlaceholder = computed(() => {
  if (props.protocol === 'OpcUa') return 'opc.tcp://127.0.0.1'
  return '127.0.0.1'
})

const networkPortLabel = computed(() =>
  props.protocol === 'OmronFins' ? 'FINS端口' : props.protocol === 'BacnetIp' ? 'BACnet/IP 端口' : '端口'
)
const transportLocked = computed(
  () =>
    props.protocol === 'ModbusTcp' ||
    props.protocol === 'SiemensS7' ||
    props.protocol === 'RockwellCip' ||
    props.protocol === 'EtherNetIp' ||
    props.protocol === 'RockwellPccc' ||
    props.protocol === 'BeckhoffAds' ||
    props.protocol === 'Snmp' ||
    props.protocol === 'MqttClient' ||
    props.protocol === 'Dnp3' ||
    props.protocol === 'OpcUa' ||
    props.protocol === 'BacnetIp' ||
    props.protocol === 'Dlt6452007' ||
    props.protocol === 'Cjt1882004' ||
    props.protocol === 'Cjt1882018'
)
const serialHostLabel = computed(() => (props.protocol === 'CanOpen' ? 'CAN适配器串口' : '串口'))
const serialPortLabel = computed(() => (props.protocol === 'CanOpen' ? '适配器波特率' : '波特率'))
watch(
  () => [props.protocol, props.device.connection.transport] as const,
  ([protocol, transport]) => {
    const port = defaultPortForProtocolTransport(protocol, transport)
    if (port !== undefined) props.device.connection.port = port
  }
)

watch(
  () => props.device.connection.opcUaSecurityPolicy,
  (policy) => {
    if (props.protocol !== 'OpcUa') return
    if (!policy || policy === 'None') props.device.connection.opcUaMessageSecurityMode = 'None'
    else if (props.device.connection.opcUaMessageSecurityMode === 'None')
      props.device.connection.opcUaMessageSecurityMode = 'SignAndEncrypt'
  }
)

watch(
  () => props.device.connection.opcUaMessageSecurityMode,
  (mode) => {
    if (props.protocol !== 'OpcUa') return
    if (!mode || mode === 'None') props.device.connection.opcUaSecurityPolicy = 'None'
    else if (props.device.connection.opcUaSecurityPolicy === 'None')
      props.device.connection.opcUaSecurityPolicy = 'Basic256Sha256'
  }
)

function parameterType(parameter: GatewayConnectionParameterDefinition) {
  return (parameter.parameterType || 'text').toLowerCase()
}

function connectionInputName(index: number) {
  const protocolToken = (props.protocol || 'device').replace(/[^A-Za-z0-9_-]/g, '-')
  return `device-connection-${protocolToken}-${index}`
}

function connectionInputAutocomplete(parameter: GatewayConnectionParameterDefinition) {
  if (parameterType(parameter) === 'password') return 'new-password'
  const key = (parameter.key || '').toLowerCase()
  return key.includes('user') || key.includes('account') || key.includes('login') ? 'off' : undefined
}

function textParameterValue(parameter: GatewayConnectionParameterDefinition) {
  const value = getParameterValue(parameter)
  if (value === null || value === undefined || value === '') return parameter.defaultValue ?? ''
  return String(value)
}

function numberParameterValue(parameter: GatewayConnectionParameterDefinition) {
  const value = getParameterValue(parameter)
  const rawValue = value === null || value === undefined || value === '' ? parameter.defaultValue : value
  const number = Number(rawValue)
  return Number.isFinite(number) ? number : undefined
}

function switchParameterValue(parameter: GatewayConnectionParameterDefinition) {
  const value = getParameterValue(parameter)
  if (typeof value === 'boolean') return value
  const text = String(
    value === null || value === undefined || value === '' ? parameter.defaultValue : value
  ).toLowerCase()
  return text === 'true' || text === '1'
}

function updateTextParameter(parameter: GatewayConnectionParameterDefinition, value: string | number) {
  updateParameterValue(parameter, value)
}

function updateNumberParameter(parameter: GatewayConnectionParameterDefinition, value: number | undefined) {
  updateParameterValue(parameter, value)
}

function updateSwitchParameter(parameter: GatewayConnectionParameterDefinition, value: string | number | boolean) {
  updateParameterValue(parameter, value)
}

function getParameterValue(parameter: GatewayConnectionParameterDefinition) {
  const key = parameter.key
  if (key.startsWith('driverOptions.')) {
    const options = readDriverOptions()
    return options[key.slice('driverOptions.'.length)]
  }
  return (props.device.connection as unknown as Record<string, unknown>)[key]
}

function getParameterValueByKey(key: string) {
  if (key.startsWith('driverOptions.')) {
    const options = readDriverOptions()
    return options[key.slice('driverOptions.'.length)]
  }
  return (props.device.connection as unknown as Record<string, unknown>)[key]
}

function updateParameterValue(parameter: GatewayConnectionParameterDefinition, value: unknown) {
  const key = parameter.key
  if (key.startsWith('driverOptions.')) {
    const options = readDriverOptions()
    const optionKey = key.slice('driverOptions.'.length)
    if (optionKey === 'controllerProfile' && props.protocol === 'RockwellCip') {
      rememberedRockwellLogixSettings = applyRockwellControllerProfile(options, value, rememberedRockwellLogixSettings)
    } else {
      options[optionKey] = value
    }
    if (optionKey === 'controllerProfile' && props.protocol === 'SiemensS7') {
      applyS7ProfileDefaults(String(value), options)
    }
    props.device.connection.driverOptionsJson = JSON.stringify(options)
    return
  }
  ;(props.device.connection as unknown as Record<string, unknown>)[key] = value
}

function readDriverOptions() {
  try {
    const parsed = JSON.parse(props.device.connection.driverOptionsJson || '{}')
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

function applyS7ProfileDefaults(profile: string, options: Record<string, unknown>) {
  const normalized = profile.trim().toUpperCase()
  props.device.connection.rack = 0
  if (normalized === 'S7-200 SMART') props.device.connection.slot = 0
  else if (normalized === 'S7-300') props.device.connection.slot = 2
  else if (normalized === 'S7-400') props.device.connection.slot = 3
  else props.device.connection.slot = 1

  if (normalized === 'LOGO!') options.s7TsapMode = 'Custom'
  else if (!options.s7TsapMode || String(options.s7TsapMode).toLowerCase() === 'custom') options.s7TsapMode = 'RackSlot'
}
</script>
