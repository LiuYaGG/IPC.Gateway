<template>
  <div class="connection-fields">
    <template v-if="connectionParameters.length">
      <div class="form-grid">
        <el-form-item
          v-for="parameter in connectionParameters"
          :key="parameter.key"
          :label="parameter.label || parameter.key"
          :required="parameter.required"
        >
          <el-input-number
            v-if="parameterType(parameter) === 'number'"
            :model-value="numberParameterValue(parameter)"
            :min="parameter.min ?? undefined"
            :max="parameter.max ?? undefined"
            :disabled="parameter.readOnly"
            @update:model-value="updateNumberParameter(parameter, $event)"
          />
          <el-select
            v-else-if="parameterType(parameter) === 'select'"
            :model-value="textParameterValue(parameter)"
            :disabled="parameter.readOnly"
            @update:model-value="updateTextParameter(parameter, $event)"
          >
            <el-option v-for="item in parameter.options" :key="item" :label="item" :value="item" />
          </el-select>
          <el-switch
            v-else-if="parameterType(parameter) === 'switch'"
            :model-value="switchParameterValue(parameter)"
            :disabled="parameter.readOnly"
            @update:model-value="updateSwitchParameter(parameter, $event)"
          />
          <el-input
            v-else
            :model-value="textParameterValue(parameter)"
            :type="parameterType(parameter) === 'password' ? 'password' : parameterType(parameter) === 'textarea' ? 'textarea' : 'text'"
            :placeholder="parameter.placeholder"
            :disabled="parameter.readOnly"
            :show-password="parameterType(parameter) === 'password'"
            @update:model-value="updateTextParameter(parameter, $event)"
          />
        </el-form-item>
      </div>
    </template>

    <template v-else-if="protocol === 'VirtualPlc'">
      <el-form-item label="虚拟源">
        <el-input v-model="device.connection.host" placeholder="default" />
      </el-form-item>
      <el-form-item label="超时(ms)">
        <el-input-number v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
      </el-form-item>
    </template>

    <template v-else-if="isNetworkProtocol(protocol)">
      <div class="form-grid">
        <el-form-item :label="networkHostLabel">
          <el-input v-model="device.connection.host" :placeholder="networkHostPlaceholder" />
        </el-form-item>
        <el-form-item :label="networkPortLabel">
          <el-input-number v-model="device.connection.port" :min="0" :max="65535" />
        </el-form-item>
        <el-form-item label="传输">
          <el-select v-model="device.connection.transport" :disabled="transportLocked">
            <el-option v-for="item in transportOptions" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="超时(ms)">
          <el-input-number v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
        </el-form-item>
      </div>

      <div v-if="protocol === 'SiemensS7'" class="form-grid">
        <el-form-item label="Rack">
          <el-input-number v-model="device.connection.rack" :min="0" :max="16" />
        </el-form-item>
        <el-form-item label="Slot">
          <el-input-number v-model="device.connection.slot" :min="0" :max="16" />
        </el-form-item>
      </div>

      <div v-if="['ModbusTcp', 'MitsubishiMc', 'MitsubishiMc1E'].includes(protocol)" class="form-grid">
        <el-form-item label="字序">
          <el-select v-model="device.connection.wordOrder">
            <el-option v-for="item in wordOrderOptions" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
      </div>

      <div v-if="protocol === 'OpcUa'" class="form-grid">
        <el-form-item label="用户名">
          <el-input v-model="device.connection.username" />
        </el-form-item>
        <el-form-item label="密码">
          <el-input v-model="device.connection.password" type="password" show-password />
        </el-form-item>
        <el-form-item label="客户端证书">
          <el-input v-model="device.connection.certificatePath" placeholder="Data/Certificates/device-client.pfx" />
        </el-form-item>
        <el-form-item label="证书密码">
          <el-input v-model="device.connection.certificatePassword" type="password" show-password />
        </el-form-item>
        <el-form-item label="证书指纹">
          <el-input v-model="device.connection.certificateThumbprint" placeholder="可选，用于校验证书" />
        </el-form-item>
        <el-form-item label="信任库路径">
          <el-input v-model="device.connection.trustStorePath" placeholder="Data/Certificates/trust" />
        </el-form-item>
        <el-form-item label="校验服务端证书">
          <el-switch v-model="device.connection.validateServerCertificate" />
        </el-form-item>
      </div>

      <el-form-item v-if="protocol === 'OmronFins'" label="FINS 参数">
        <el-input
          v-model="device.connection.driverOptionsJson"
          type="textarea"
          :autosize="{ minRows: 3, maxRows: 6 }"
          class="json-editor"
          placeholder='例如：{"sourceNode":1,"destinationNode":10,"network":0}'
        />
      </el-form-item>
    </template>

    <template v-else-if="isSerialProtocol(protocol)">
      <div class="form-grid">
        <el-form-item :label="serialHostLabel">
          <el-input v-model="device.connection.host" placeholder="COM1" />
        </el-form-item>
        <el-form-item :label="serialPortLabel">
          <el-input-number v-model="device.connection.port" :min="1200" :max="921600" />
        </el-form-item>
        <el-form-item label="数据位">
          <el-input-number v-model="device.connection.dataBits" :min="5" :max="8" />
        </el-form-item>
        <el-form-item label="校验">
          <el-select v-model="device.connection.serialParity">
            <el-option v-for="item in parityOptions" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="停止位">
          <el-select v-model="device.connection.serialStopBits">
            <el-option v-for="item in stopBitsOptions" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="超时(ms)">
          <el-input-number v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
        </el-form-item>
      </div>

      <div v-if="['ModbusRtu', 'MitsubishiSerial', 'MitsubishiQlSerial'].includes(protocol)" class="form-grid">
        <el-form-item label="字序">
          <el-select v-model="device.connection.wordOrder">
            <el-option v-for="item in wordOrderOptions" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
      </div>

      <el-alert
        v-if="protocol === 'Dlt6452007' || protocol === 'Cjt1882004'"
        title="表地址、数据标识、表类型仍在标签配置里维护。"
        type="info"
        :closable="false"
      />
    </template>

    <template v-else-if="protocol === 'OpcDa'">
      <div class="form-grid">
        <el-form-item label="主机">
          <el-input v-model="device.connection.host" placeholder="localhost" />
        </el-form-item>
        <el-form-item label="Server ProgID">
          <el-input v-model="device.connection.opcDaServerProgId" />
        </el-form-item>
        <el-form-item label="Group">
          <el-input v-model="device.connection.opcDaGroupName" />
        </el-form-item>
        <el-form-item label="超时(ms)">
          <el-input-number v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
        </el-form-item>
      </div>
    </template>

    <template v-else-if="protocol === 'Plugin'">
      <el-form-item label="驱动 ID">
        <el-input v-model="device.connection.driverId" placeholder="插件驱动标识" />
      </el-form-item>
      <el-form-item label="驱动参数 JSON">
        <el-input
          v-model="device.connection.driverOptionsJson"
          type="textarea"
          :autosize="{ minRows: 5, maxRows: 10 }"
          class="json-editor"
        />
      </el-form-item>
      <div class="form-grid">
        <el-form-item label="主机">
          <el-input v-model="device.connection.host" />
        </el-form-item>
        <el-form-item label="端口">
          <el-input-number v-model="device.connection.port" :min="0" :max="65535" />
        </el-form-item>
        <el-form-item label="传输">
          <el-select v-model="device.connection.transport">
            <el-option v-for="item in transportOptions" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
        <el-form-item label="超时(ms)">
          <el-input-number v-model="device.connection.timeoutMilliseconds" :min="100" :max="60000" />
        </el-form-item>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { DeviceConfig, GatewayConnectionParameterDefinition } from '../api'
import {
  isNetworkProtocol,
  isSerialProtocol,
  parityOptions,
  stopBitsOptions,
  transportOptions,
  wordOrderOptions
} from '../utils/deviceDefaults'

const props = defineProps<{
  device: DeviceConfig
  protocol: string
  parameters?: GatewayConnectionParameterDefinition[]
}>()

const connectionParameters = computed(() => (props.parameters ?? []).filter(parameter => parameter.key))

const networkHostLabel = computed(() => {
  if (props.protocol === 'OpcUa') return 'Endpoint'
  if (props.protocol === 'OmronFins') return 'PLC 地址'
  return '主机 / 地址'
})

const networkHostPlaceholder = computed(() => {
  if (props.protocol === 'OpcUa') return 'opc.tcp://127.0.0.1'
  return '127.0.0.1'
})

const networkPortLabel = computed(() => props.protocol === 'OmronFins' ? 'FINS端口' : '端口')
const transportLocked = computed(() => props.protocol === 'SiemensS7' || props.protocol === 'RockwellCip' || props.protocol === 'OpcUa')
const serialHostLabel = computed(() => props.protocol === 'Dlt6452007' || props.protocol === 'Cjt1882004' ? '采集串口' : '串口')
const serialPortLabel = computed(() => props.protocol === 'Dlt6452007' || props.protocol === 'Cjt1882004' ? '表计波特率' : '波特率')
function parameterType(parameter: GatewayConnectionParameterDefinition) {
  return (parameter.parameterType || 'text').toLowerCase()
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
  const text = String(value === null || value === undefined || value === '' ? parameter.defaultValue : value).toLowerCase()
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

function updateParameterValue(parameter: GatewayConnectionParameterDefinition, value: unknown) {
  const key = parameter.key
  if (key.startsWith('driverOptions.')) {
    const options = readDriverOptions()
    options[key.slice('driverOptions.'.length)] = value
    props.device.connection.driverOptionsJson = JSON.stringify(options)
    return
  }
  ;(props.device.connection as unknown as Record<string, unknown>)[key] = value
}

function readDriverOptions() {
  try {
    const parsed = JSON.parse(props.device.connection.driverOptionsJson || '{}')
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : {}
  } catch {
    return {}
  }
}
</script>
