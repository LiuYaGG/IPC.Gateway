<template>
  <el-form ref="formRef" label-width="120px" :model="model" :rules="rules" class="device-form" status-icon>
    <el-divider content-position="left">基础信息</el-divider>
    <el-form-item label="标签名称" prop="name" required>
      <el-input v-model="model.name" placeholder="例如：温度" />
    </el-form-item>
    <div class="form-grid">
      <el-form-item label="启用">
        <el-switch v-model="model.enabled" />
      </el-form-item>
      <el-form-item label="MQTT上报">
        <el-switch v-model="model.mqttPublishEnabled" />
      </el-form-item>
    </div>

    <el-divider content-position="left">采集配置</el-divider>
    <div class="form-grid">
      <el-form-item label="数据类型">
        <el-select v-model="model.dataType" filterable class="data-type-select">
          <el-option-group
            v-for="group in availableDataTypeGroups"
            :key="group.label"
            :label="group.label"
          >
            <el-option v-for="item in group.options" :key="item" :label="item" :value="item" />
          </el-option-group>
        </el-select>
      </el-form-item>
      <el-form-item label="访问模式">
        <el-select v-model="model.accessMode">
          <el-option v-for="item in accessModeOptions" :key="item" :label="item" :value="item" />
        </el-select>
      </el-form-item>
      <el-form-item label="采集周期(ms)" prop="scanRateMs">
        <el-input-number v-model="model.scanRateMs" :min="100" :max="3600000" />
      </el-form-item>
      <el-form-item label="失败重试(ms)" prop="failureRetryDelayMs">
        <el-input-number v-model="model.failureRetryDelayMs" :min="100" :max="3600000" />
      </el-form-item>
      <el-form-item label="元素数量" prop="elementCount">
        <el-input-number v-model="model.elementCount" :min="1" :max="256" />
      </el-form-item>
      <el-form-item label="元素偏移">
        <el-input-number v-model="model.elementOffset" :min="0" :max="65535" />
      </el-form-item>
    </div>
    <el-form-item label="地址" prop="address" :required="!isMeter">
      <el-input v-model="model.address" :placeholder="addressPlaceholder" />
    </el-form-item>

    <el-divider content-position="left">数值缩放</el-divider>
    <div class="form-grid">
      <el-form-item label="启用缩放">
        <el-switch v-model="model.scaling.enabled" />
      </el-form-item>
      <el-form-item label="倍率" prop="scaling.multiplier">
        <el-input-number
          v-model="model.scaling.multiplier"
          :disabled="!model.scaling.enabled"
          :controls="false"
          :step="0.1"
          placeholder="例如：10 或 0.1"
        />
      </el-form-item>
      <el-form-item label="偏移" prop="scaling.offset">
        <el-input-number
          v-model="model.scaling.offset"
          :disabled="!model.scaling.enabled"
          :controls="false"
          :step="0.1"
        />
      </el-form-item>
      <el-form-item label="小数位" prop="scaling.decimalPlaces">
        <el-input-number
          v-model="model.scaling.decimalPlaces"
          :disabled="!model.scaling.enabled"
          :min="0"
          :max="8"
          :step="1"
          step-strictly
          :controls="false"
        />
      </el-form-item>
    </div>
    <p class="form-help">
      {{ scalingPreview }}
    </p>

    <el-divider content-position="left">数据清洗</el-divider>
    <div class="form-grid">
      <el-form-item label="启用清洗">
        <el-switch v-model="model.cleaning.enabled" />
      </el-form-item>
      <el-form-item label="过滤保留上次值">
        <el-switch v-model="model.cleaning.preserveLastGoodOnFilter" :disabled="!model.cleaning.enabled" />
      </el-form-item>
      <el-form-item label="越界标记">
        <el-switch v-model="model.cleaning.outOfRangeEnabled" :disabled="!model.cleaning.enabled" />
      </el-form-item>
      <el-form-item label="越界范围">
        <div class="inline-number-pair">
          <el-input-number v-model="model.cleaning.minValue" :disabled="!model.cleaning.enabled || !model.cleaning.outOfRangeEnabled" :controls="false" placeholder="最小值" />
          <el-input-number v-model="model.cleaning.maxValue" :disabled="!model.cleaning.enabled || !model.cleaning.outOfRangeEnabled" :controls="false" placeholder="最大值" />
        </div>
      </el-form-item>
      <el-form-item label="死区过滤">
        <el-switch v-model="model.cleaning.deadbandEnabled" :disabled="!model.cleaning.enabled" />
      </el-form-item>
      <el-form-item label="死区值" prop="cleaning.deadband">
        <el-input-number v-model="model.cleaning.deadband" :disabled="!model.cleaning.enabled || !model.cleaning.deadbandEnabled" :min="0" :controls="false" />
      </el-form-item>
      <el-form-item label="重复值过滤">
        <el-switch v-model="model.cleaning.duplicateFilterEnabled" :disabled="!model.cleaning.enabled" />
      </el-form-item>
      <el-form-item label="毛刺过滤">
        <el-switch v-model="model.cleaning.spikeFilterEnabled" :disabled="!model.cleaning.enabled" />
      </el-form-item>
      <el-form-item label="毛刺阈值" prop="cleaning.spikeThreshold">
        <el-input-number v-model="model.cleaning.spikeThreshold" :disabled="!model.cleaning.enabled || !model.cleaning.spikeFilterEnabled" :min="0" :controls="false" />
      </el-form-item>
      <el-form-item label="毛刺窗口(s)">
        <el-input-number v-model="model.cleaning.spikeWindowSeconds" :disabled="!model.cleaning.enabled || !model.cleaning.spikeFilterEnabled" :min="0" :controls="false" />
      </el-form-item>
    </div>
    <div class="form-grid">
      <el-form-item label="单位换算">
        <el-switch v-model="model.cleaning.unitConversionEnabled" :disabled="!model.cleaning.enabled" />
      </el-form-item>
      <el-form-item label="源/目标单位">
        <div class="inline-number-pair">
          <el-input v-model="model.cleaning.sourceUnit" :disabled="!model.cleaning.enabled || !model.cleaning.unitConversionEnabled" placeholder="源单位" />
          <el-input v-model="model.cleaning.targetUnit" :disabled="!model.cleaning.enabled || !model.cleaning.unitConversionEnabled" placeholder="目标单位" />
        </div>
      </el-form-item>
      <el-form-item label="单位倍率" prop="cleaning.unitMultiplier">
        <el-input-number v-model="model.cleaning.unitMultiplier" :disabled="!model.cleaning.enabled || !model.cleaning.unitConversionEnabled" :controls="false" :step="0.1" />
      </el-form-item>
      <el-form-item label="单位偏移">
        <el-input-number v-model="model.cleaning.unitOffset" :disabled="!model.cleaning.enabled || !model.cleaning.unitConversionEnabled" :controls="false" :step="0.1" />
      </el-form-item>
    </div>
    <el-form-item label="枚举映射">
      <el-switch v-model="model.cleaning.enumMappingEnabled" :disabled="!model.cleaning.enabled" />
    </el-form-item>
    <div v-if="model.cleaning.enabled && model.cleaning.enumMappingEnabled" class="enum-mapping-list">
      <div v-for="(item, index) in model.cleaning.enumMappings" :key="index" class="enum-mapping-row">
        <el-input v-model="item.rawValue" placeholder="原始值，如 1" />
        <el-input v-model="item.cleanValue" placeholder="显示值，如 运行" />
        <el-input v-model="item.description" placeholder="说明" />
        <el-button type="danger" text @click="removeEnumMapping(index)">删除</el-button>
      </div>
      <el-button plain @click="addEnumMapping">新增映射</el-button>
    </div>
    <el-divider content-position="left">值处理脚本</el-divider>
    <div class="form-grid">
      <el-form-item label="启用脚本处理">
        <el-switch v-model="model.cleaning.valueScriptEnabled" :disabled="!model.cleaning.enabled" />
      </el-form-item>
      <el-form-item label="已发布脚本" prop="cleaning.valueScriptId">
        <el-select
          v-model="model.cleaning.valueScriptId"
          filterable
          clearable
          default-first-option
          :disabled="!model.cleaning.enabled || !model.cleaning.valueScriptEnabled"
          placeholder="选择或输入筛选"
          no-data-text="没有输入类型兼容的已发布标签清洗脚本"
          :filter-method="filterValueScripts"
          @change="applySelectedValueScript"
          @visible-change="handleValueScriptDropdownVisible"
        >
          <el-option
            v-for="script in compatibleValueScripts"
            :key="script.id"
            :label="`${script.name}（v${script.version}，${script.inputDataType} → ${script.outputDataType}）`"
            :value="script.id"
          />
        </el-select>
      </el-form-item>
      <el-form-item label="固定版本">
        <el-input-number v-model="model.cleaning.valueScriptVersion" :disabled="true" :controls="false" />
      </el-form-item>
      <el-form-item label="脚本超时(ms)">
        <el-input-number
          v-model="model.cleaning.valueScriptTimeoutMilliseconds"
          :disabled="!model.cleaning.enabled || !model.cleaning.valueScriptEnabled"
          :min="10"
          :max="5000"
          :controls="false"
        />
      </el-form-item>
      <el-form-item label="失败策略">
        <el-select v-model="model.cleaning.valueScriptFailurePolicy" :disabled="!model.cleaning.enabled || !model.cleaning.valueScriptEnabled">
          <el-option label="保留上次有效值" value="KeepLastGood" />
          <el-option label="使用本次原始值" value="UseOriginal" />
          <el-option label="标记坏质量" value="MarkBad" />
        </el-select>
      </el-form-item>
    </div>
    <p class="form-help">下拉框内可按脚本名称、说明、输入/输出类型或版本筛选；保存标签时会固定当前发布版本。</p>

    <template v-if="isMeter">
      <el-divider content-position="left">表计字段</el-divider>
      <div class="form-grid">
        <el-form-item label="协议" prop="protocol" required>
          <el-select v-model="model.protocol" filterable :disabled="!!deviceProtocol" @change="applyMeterDefaults">
            <el-option v-for="item in meterProtocolOptions" :key="item.value" :label="item.label" :value="item.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="表地址" prop="meterAddress" required>
          <el-input v-model="model.meterAddress" :placeholder="meterAddressHint" />
        </el-form-item>
        <el-form-item label="数据标识" prop="meterDataIdentifier" required>
          <el-input v-model="model.meterDataIdentifier" :placeholder="meterDataIdentifierHint" />
        </el-form-item>
        <el-form-item label="表类型">
          <el-select
            v-model="model.meterType"
            filterable
            allow-create
            default-first-option
            clearable
          >
            <el-option v-for="item in meterTypeItems" :key="item" :label="item" :value="item" />
          </el-select>
        </el-form-item>
      </div>
    </template>

    <el-divider content-position="left">业务信息</el-divider>
    <div class="form-grid">
      <el-form-item label="单位">
        <el-input v-model="model.unit" />
      </el-form-item>
      <el-form-item label="点位编码">
        <el-input v-model="model.pointCode" />
      </el-form-item>
      <el-form-item label="资产路径">
        <el-input v-model="model.assetPath" />
      </el-form-item>
      <el-form-item label="业务类型">
        <el-input v-model="model.businessType" />
      </el-form-item>
    </div>
    <el-form-item label="描述">
      <el-input v-model="model.description" type="textarea" :rows="3" />
    </el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import type { FormInstance, FormRules } from 'element-plus'
import type { TagConfig } from '../api'
import { loadValueTransformCatalog, type ValueTransformCatalogItem } from '../scriptingApi'
import {
  accessModeOptions,
  dataTypeOptionGroups,
  defaultMeterType,
  isMeterProtocol,
  meterAddressPlaceholder,
  meterDataIdentifierPlaceholder,
  meterProtocolOptions,
  meterTypeOptions,
  tagAddressPlaceholder
} from '../utils/tagDefaults'

const props = defineProps<{
  model: TagConfig
  deviceProtocol: string
}>()

const formRef = ref<FormInstance>()
const valueScripts = ref<ValueTransformCatalogItem[]>([])
const valueScriptFilterKeyword = ref('')
const activeProtocol = computed(() => props.model.protocol || props.deviceProtocol)
const availableDataTypeGroups = computed(() => dataTypeOptionGroups(activeProtocol.value))
const isMeter = computed(() => isMeterProtocol(activeProtocol.value))
const addressPlaceholder = computed(() => tagAddressPlaceholder(activeProtocol.value))
const meterAddressHint = computed(() => meterAddressPlaceholder(activeProtocol.value))
const compatibleValueScripts = computed(() => {
  const keyword = valueScriptFilterKeyword.value.trim().toLocaleLowerCase()
  return valueScripts.value.filter(script => {
    if (script.scope === 'RuleEngine' || !isCompatibleInputType(script.inputDataType, props.model.dataType)) return false
    if (!keyword) return true
    const searchableText = [
      script.name,
      script.description,
      script.inputDataType,
      script.outputDataType,
      `v${script.version}`,
      String(script.version)
    ].join(' ').toLocaleLowerCase()
    return searchableText.includes(keyword)
  })
})

onMounted(async () => {
  try {
    const response = await loadValueTransformCatalog()
    valueScripts.value = response.data
  } catch {
    valueScripts.value = []
  }
})

/**
 * 固定当前所选脚本的发布版本。
 */
function applySelectedValueScript(scriptId: string) {
  const script = valueScripts.value.find(item => item.id === scriptId)
  props.model.cleaning.valueScriptVersion = script?.version ?? 0
}

/**
 * 在原有脚本选择框内筛选目录，避免增加额外输入框挤压表单宽度。
 */
function filterValueScripts(keyword: string) {
  valueScriptFilterKeyword.value = keyword
}

/**
 * 下拉框关闭后清除临时筛选词，下一次打开时恢复完整兼容目录。
 */
function handleValueScriptDropdownVisible(visible: boolean) {
  if (!visible) valueScriptFilterKeyword.value = ''
}

/**
 * 判断脚本声明的输入类型能否接收当前标签的采集值。
 */
function isCompatibleInputType(inputType: string, tagType: string) {
  if (inputType === 'Object' || inputType === tagType) return true
  if (inputType === 'Bool' && ['Bool', 'Coil', 'DiscreteInput'].includes(tagType)) return true
  if (inputType === 'BoolArray' && ['BoolArray', 'CoilArray', 'DiscreteInputArray'].includes(tagType)) return true
  const numericTypes = ['Int8', 'UInt8', 'Int16', 'UInt16', 'Int32', 'UInt32', 'Int64', 'UInt64', 'Float', 'Double', 'Decimal']
  return numericTypes.includes(inputType) && numericTypes.includes(tagType)
}
const meterDataIdentifierHint = computed(() => meterDataIdentifierPlaceholder(activeProtocol.value))
const meterTypeItems = computed(() => meterTypeOptions(activeProtocol.value))
const rules: FormRules<TagConfig> = {
  name: [{ required: true, message: '请输入标签名称', trigger: 'blur' }],
  protocol: [{ validator: validateMeterProtocol, trigger: 'change' }],
  address: [{ validator: validateAddress, trigger: 'blur' }],
  meterAddress: [{ validator: validateMeterAddress, trigger: 'blur' }],
  meterDataIdentifier: [{ validator: validateMeterDataIdentifier, trigger: 'blur' }],
  scanRateMs: [{ validator: validateMin100, trigger: 'change' }],
  failureRetryDelayMs: [{ validator: validateMin100, trigger: 'change' }],
  elementCount: [{ validator: validateElementCount, trigger: 'change' }],
  'scaling.multiplier': [{ validator: validateScalingMultiplier, trigger: 'change' }],
  'scaling.offset': [{ validator: validateScalingOffset, trigger: 'change' }],
  'scaling.decimalPlaces': [{ validator: validateScalingDecimalPlaces, trigger: 'change' }],
  'cleaning.deadband': [{ validator: validateCleaningNonNegative, trigger: 'change' }],
  'cleaning.spikeThreshold': [{ validator: validateCleaningNonNegative, trigger: 'change' }],
  'cleaning.unitMultiplier': [{ validator: validateUnitMultiplier, trigger: 'change' }],
  'cleaning.valueScriptId': [{ validator: validateValueScriptSelection, trigger: 'change' }]
}

const scalingPreview = computed(() => {
  if (!props.model.scaling?.enabled) return '未启用时，标签值按设备原始值展示。'
  const multiplier = Number(props.model.scaling.multiplier)
  const offset = Number(props.model.scaling.offset)
  const decimalPlaces = Math.max(0, Math.min(8, Number(props.model.scaling.decimalPlaces ?? 2)))
  if (!Number.isFinite(multiplier) || !Number.isFinite(offset)) return '请输入合法的倍率和偏移。'
  const value = 10 * multiplier + offset
  return `示例：原始值 10 会显示为 ${value.toFixed(decimalPlaces)}。`
})

watch(
  () => props.deviceProtocol,
  protocol => {
    if (!props.model.protocol && protocol) props.model.protocol = protocol
    if (isMeterProtocol(props.model.protocol) && !props.model.meterType) {
      props.model.meterType = defaultMeterType(props.model.protocol)
    }
  },
  { immediate: true }
)

function applyMeterDefaults(protocol: string) {
  if (!props.model.meterType) props.model.meterType = defaultMeterType(protocol)
}

function addEnumMapping() {
  if (!props.model.cleaning.enumMappings) props.model.cleaning.enumMappings = []
  props.model.cleaning.enumMappings.push({
    rawValue: '',
    cleanValue: '',
    description: ''
  })
}

function removeEnumMapping(index: number) {
  props.model.cleaning.enumMappings.splice(index, 1)
}

async function validate() {
  if (!formRef.value) return true
  try {
    await formRef.value.validate()
    return true
  } catch {
    return false
  }
}

function validateMeterProtocol(_rule: unknown, value: string, callback: (error?: Error) => void) {
  if (!isMeter.value || value) callback()
  else callback(new Error('请选择表计协议'))
}

function validateAddress(_rule: unknown, value: string, callback: (error?: Error) => void) {
  if (isMeter.value || String(value ?? '').trim()) callback()
  else callback(new Error('请输入标签地址'))
}

function validateMeterAddress(_rule: unknown, value: string, callback: (error?: Error) => void) {
  if (!isMeter.value || String(value ?? '').trim()) callback()
  else callback(new Error('请输入表地址'))
}

function validateMeterDataIdentifier(_rule: unknown, value: string, callback: (error?: Error) => void) {
  if (!isMeter.value || String(value ?? '').trim()) callback()
  else callback(new Error('请输入数据标识'))
}

function validateMin100(_rule: unknown, value: number, callback: (error?: Error) => void) {
  if (Number(value) >= 100) callback()
  else callback(new Error('请输入不小于 100 的毫秒值'))
}

function validateElementCount(_rule: unknown, value: number, callback: (error?: Error) => void) {
  if (Number(value) >= 1) callback()
  else callback(new Error('元素数量不能小于 1'))
}

function validateScalingMultiplier(_rule: unknown, value: number, callback: (error?: Error) => void) {
  if (!props.model.scaling?.enabled) {
    callback()
    return
  }

  const number = Number(value)
  if (!Number.isFinite(number)) callback(new Error('请输入合法倍率'))
  else if (number === 0) callback(new Error('倍率不能为 0'))
  else callback()
}

function validateScalingOffset(_rule: unknown, value: number, callback: (error?: Error) => void) {
  if (!props.model.scaling?.enabled || Number.isFinite(Number(value))) callback()
  else callback(new Error('请输入合法偏移'))
}

function validateScalingDecimalPlaces(_rule: unknown, value: number, callback: (error?: Error) => void) {
  if (!props.model.scaling?.enabled) {
    callback()
    return
  }

  const number = Number(value)
  if (Number.isInteger(number) && number >= 0 && number <= 8) callback()
  else callback(new Error('小数位必须是 0 到 8 的整数'))
}

function validateCleaningNonNegative(_rule: unknown, value: number, callback: (error?: Error) => void) {
  if (!props.model.cleaning?.enabled || Number(value) >= 0) callback()
  else callback(new Error('请输入不小于 0 的数值'))
}

function validateUnitMultiplier(_rule: unknown, value: number, callback: (error?: Error) => void) {
  if (!props.model.cleaning?.enabled || !props.model.cleaning.unitConversionEnabled) {
    callback()
    return
  }

  const number = Number(value)
  if (!Number.isFinite(number)) callback(new Error('请输入合法单位倍率'))
  else if (number === 0) callback(new Error('单位倍率不能为 0'))
  else callback()
}

/**
 * 启用脚本清洗时要求选择一个有效发布版本。
 */
function validateValueScriptSelection(_rule: unknown, value: string, callback: (error?: Error) => void) {
  if (!props.model.cleaning?.enabled || !props.model.cleaning.valueScriptEnabled) return callback()
  if (value?.trim() && props.model.cleaning.valueScriptVersion > 0) return callback()
  callback(new Error('请选择一个已发布的值处理脚本'))
}

defineExpose({ validate })
</script>
