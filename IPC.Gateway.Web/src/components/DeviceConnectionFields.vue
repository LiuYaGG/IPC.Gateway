<template>
  <div class="connection-fields">
    <template v-if="protocol === 'VirtualPlc'">
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
import type { DeviceConfig } from '../api'
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
}>()

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
</script>
