<template>
  <a-form
    ref="formRef"
    :model="model"
    :rules="antRules"
    class="device-form"
    status-icon
    :label-col="{ style: { width: '120px' } }"
    :wrapper-col="{ style: { flex: 1 } }"
    label-align="left"
  >
    <a-divider orientation="left">{{ tx('基础信息') }}</a-divider>
    <a-form-item :label="tx('标签名称')" :name="['name']" required>
      <gateway-input v-model="model.name" :placeholder="tx('例如：温度')" />
    </a-form-item>
    <div class="form-grid">
      <a-form-item :label="tx('启用')"><a-switch v-model:checked="model.enabled" /></a-form-item>
      <a-form-item :label="tx('MQTT上报')"><a-switch v-model:checked="model.mqttPublishEnabled" /></a-form-item>
    </div>
    <a-divider orientation="left">{{ tx('采集配置') }}</a-divider>
    <div class="form-grid">
      <a-form-item :label="tx('数据来源')">
        <gateway-choice
          v-model="model.isVirtual"
          :options="[
            { label: tx('设备采集'), value: false },
            { label: tx('ONNX模型'), value: true }
          ]"
          @change="changeTagSource"
        />
      </a-form-item>
      <a-form-item :label="tx('数据类型')">
        <gateway-select v-model="model.dataType" searchable class="data-type-select">
          <a-select-opt-group v-for="group in availableDataTypeGroups" :key="group.label" :label="tx(group.label)">
            <a-select-option
              v-for="item in group.options"
              :key="item"
              :label="tx(dataTypeDisplayName(item))"
              :value="item"
            >
              {{ tx(dataTypeDisplayName(item)) }}
            </a-select-option>
          </a-select-opt-group>
        </gateway-select>
      </a-form-item>
      <a-form-item :label="tx('访问模式')">
        <gateway-select v-model="model.accessMode" :disabled="model.isVirtual">
          <a-select-option v-for="item in accessModeOptions" :key="item" :label="tx(item)" :value="item">
            {{ item }}
          </a-select-option>
        </gateway-select>
      </a-form-item>
      <a-form-item :label="tx('采集周期(ms)')" :name="['scanRateMs']">
        <gateway-number-input v-model="model.scanRateMs" :min="100" :max="3600000" />
      </a-form-item>
      <a-form-item :label="tx('失败重试(ms)')" :name="['failureRetryDelayMs']">
        <gateway-number-input v-model="model.failureRetryDelayMs" :min="100" :max="3600000" />
      </a-form-item>
      <a-form-item :label="tx('元素数量')" :name="['elementCount']">
        <gateway-number-input v-model="model.elementCount" :min="1" :max="256" />
      </a-form-item>
      <a-form-item :label="tx('元素偏移')">
        <gateway-number-input v-model="model.elementOffset" :min="0" :max="65535" />
      </a-form-item>
    </div>
    <a-form-item v-if="!model.isVirtual" :label="tx('地址')" :name="['address']" :required="!isMeter">
      <gateway-input v-model="model.address" :placeholder="tx(addressPlaceholder)" />
    </a-form-item>
    <template v-if="model.isVirtual">
      <a-divider orientation="left">{{ tx('ONNX 虚拟标签') }}</a-divider>
      <div class="form-grid">
        <a-form-item :label="tx('已发布模型')" :name="['virtualModel', 'modelId']" required>
          <gateway-select
            v-model="model.virtualModel.modelId"
            searchable
            :placeholder="tx('选择模型')"
            @change="applyVirtualModel"
          >
            <a-select-option
              v-for="item in publishedModels"
              :key="item.id"
              :label="`${item.name}（v${item.publishedVersion}）`"
              :value="item.id"
            >
              {{ `${item.name}（v${item.publishedVersion}）` }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('固定版本')">
          <gateway-number-input v-model="model.virtualModel.modelVersion" disabled :controls="false" />
        </a-form-item>
        <a-form-item :label="tx('输出张量')">
          <gateway-input v-model="model.virtualModel.outputName" :placeholder="tx('留空使用第一个输出')" />
        </a-form-item>
        <a-form-item :label="tx('输出序号')">
          <gateway-number-input v-model="model.virtualModel.outputIndex" :min="0" :controls="false" />
        </a-form-item>
        <a-form-item :label="tx('触发方式')">
          <gateway-select v-model="model.virtualModel.triggerMode">
            <a-select-option :label="tx('输入变化')" value="OnInputChanged">{{ tx('输入变化') }}</a-select-option>
            <a-select-option :label="tx('固定周期')" value="Interval">{{ tx('固定周期') }}</a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item v-if="model.virtualModel.triggerMode === 'Interval'" :label="tx('计算周期(ms)')">
          <gateway-number-input
            v-model="model.virtualModel.intervalMilliseconds"
            :min="100"
            :max="3600000"
            :controls="false"
          />
        </a-form-item>
        <a-form-item v-else :label="tx('防抖(ms)')">
          <gateway-number-input
            v-model="model.virtualModel.debounceMilliseconds"
            :min="0"
            :max="10000"
            :controls="false"
          />
        </a-form-item>
        <a-form-item :label="tx('输入有效期(ms)')">
          <gateway-number-input
            v-model="model.virtualModel.maxInputAgeMilliseconds"
            :min="100"
            :max="3600000"
            :controls="false"
          />
        </a-form-item>
        <a-form-item :label="tx('推理超时(ms)')">
          <gateway-number-input
            v-model="model.virtualModel.timeoutMilliseconds"
            :min="10"
            :max="30000"
            :controls="false"
          />
        </a-form-item>
        <a-form-item v-if="['Bool', 'Coil', 'DiscreteInput'].includes(model.dataType)" :label="tx('布尔阈值')">
          <gateway-number-input v-model="model.virtualModel.boolThreshold" :controls="false" />
        </a-form-item>
        <a-form-item :label="tx('失败策略')">
          <gateway-select v-model="model.virtualModel.failurePolicy">
            <a-select-option :label="tx('保留上次值并标坏')" value="KeepLastGood">
              {{ tx('保留上次值并标坏') }}
            </a-select-option>
            <a-select-option :label="tx('使用回退值')" value="UseFallback">{{ tx('使用回退值') }}</a-select-option>
            <a-select-option :label="tx('标记坏质量')" value="MarkBad">{{ tx('标记坏质量') }}</a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item v-if="model.virtualModel.failurePolicy === 'UseFallback'" :label="tx('回退值')">
          <gateway-input v-model="model.virtualModel.fallbackValue" />
        </a-form-item>
      </div>
      <a-form-item :label="tx('模型输入')" :name="['virtualModel', 'inputs']" required>
        <div class="model-input-list">
          <div v-for="(input, index) in model.virtualModel.inputs" :key="index" class="model-input-row">
            <gateway-input v-model="input.featureName" :placeholder="tx('特征名称')" />
            <gateway-select v-model="input.tagPath" searchable :placeholder="tx('选择输入标签')">
              <a-select-option v-for="tag in inputTagOptions" :key="tag.value" :label="tag.label" :value="tag.value">
                {{ tag.label }}
              </a-select-option>
            </gateway-select>
            <gateway-number-input v-model="input.multiplier" :controls="false" :placeholder="tx('倍率')" />
            <gateway-number-input v-model="input.offset" :controls="false" :placeholder="tx('偏移')" />
            <a-button @click="model.virtualModel.inputs.splice(index, 1)" type="link" danger>{{ tx('删除') }}</a-button>
          </div>
          <a-button @click="addVirtualInput">{{ tx('增加输入') }}</a-button>
        </div>
      </a-form-item>
      <p class="form-help">
        {{ tx('虚拟标签不会写入 PLC；模型在独立队列执行，结果可进入历史、MQTT、Web API 和规则引擎。') }}
      </p>
    </template>
    <a-divider orientation="left">{{ tx('数值缩放') }}</a-divider>
    <div class="form-grid">
      <a-form-item :label="tx('启用缩放')"><a-switch v-model:checked="model.scaling.enabled" /></a-form-item>
      <a-form-item :label="tx('倍率')" :name="['scaling', 'multiplier']">
        <gateway-number-input
          v-model="model.scaling.multiplier"
          :disabled="!model.scaling.enabled"
          :controls="false"
          :step="0.1"
          :placeholder="tx('例如：10 或 0.1')"
        />
      </a-form-item>
      <a-form-item :label="tx('偏移')" :name="['scaling', 'offset']">
        <gateway-number-input
          v-model="model.scaling.offset"
          :disabled="!model.scaling.enabled"
          :controls="false"
          :step="0.1"
        />
      </a-form-item>
      <a-form-item :label="tx('小数位')" :name="['scaling', 'decimalPlaces']">
        <gateway-number-input
          v-model="model.scaling.decimalPlaces"
          :disabled="!model.scaling.enabled"
          :min="0"
          :max="8"
          :step="1"
          integer
          :controls="false"
        />
      </a-form-item>
    </div>
    <p class="form-help">{{ tx(scalingPreview) }}</p>
    <a-divider orientation="left">{{ tx('数据清洗') }}</a-divider>
    <div class="form-grid">
      <a-form-item :label="tx('启用清洗')"><a-switch v-model:checked="model.cleaning.enabled" /></a-form-item>
      <a-form-item :label="tx('过滤保留上次值')">
        <a-switch v-model:checked="model.cleaning.preserveLastGoodOnFilter" :disabled="!model.cleaning.enabled" />
      </a-form-item>
      <a-form-item :label="tx('越界标记')">
        <a-switch v-model:checked="model.cleaning.outOfRangeEnabled" :disabled="!model.cleaning.enabled" />
      </a-form-item>
      <a-form-item :label="tx('越界范围')">
        <div class="inline-number-pair">
          <gateway-number-input
            v-model="model.cleaning.minValue"
            :disabled="!model.cleaning.enabled || !model.cleaning.outOfRangeEnabled"
            :controls="false"
            :placeholder="tx('最小值')"
          />
          <gateway-number-input
            v-model="model.cleaning.maxValue"
            :disabled="!model.cleaning.enabled || !model.cleaning.outOfRangeEnabled"
            :controls="false"
            :placeholder="tx('最大值')"
          />
        </div>
      </a-form-item>
      <a-form-item :label="tx('死区过滤')">
        <a-switch v-model:checked="model.cleaning.deadbandEnabled" :disabled="!model.cleaning.enabled" />
      </a-form-item>
      <a-form-item :label="tx('死区值')" :name="['cleaning', 'deadband']">
        <gateway-number-input
          v-model="model.cleaning.deadband"
          :disabled="!model.cleaning.enabled || !model.cleaning.deadbandEnabled"
          :min="0"
          :controls="false"
        />
      </a-form-item>
      <a-form-item :label="tx('重复值过滤')">
        <a-switch v-model:checked="model.cleaning.duplicateFilterEnabled" :disabled="!model.cleaning.enabled" />
      </a-form-item>
      <a-form-item :label="tx('毛刺过滤')">
        <a-switch v-model:checked="model.cleaning.spikeFilterEnabled" :disabled="!model.cleaning.enabled" />
      </a-form-item>
      <a-form-item :label="tx('毛刺阈值')" :name="['cleaning', 'spikeThreshold']">
        <gateway-number-input
          v-model="model.cleaning.spikeThreshold"
          :disabled="!model.cleaning.enabled || !model.cleaning.spikeFilterEnabled"
          :min="0"
          :controls="false"
        />
      </a-form-item>
      <a-form-item :label="tx('毛刺窗口(s)')">
        <gateway-number-input
          v-model="model.cleaning.spikeWindowSeconds"
          :disabled="!model.cleaning.enabled || !model.cleaning.spikeFilterEnabled"
          :min="0"
          :controls="false"
        />
      </a-form-item>
    </div>
    <div class="form-grid">
      <a-form-item :label="tx('单位换算')">
        <a-switch v-model:checked="model.cleaning.unitConversionEnabled" :disabled="!model.cleaning.enabled" />
      </a-form-item>
      <a-form-item :label="tx('源/目标单位')">
        <div class="inline-number-pair">
          <gateway-input
            v-model="model.cleaning.sourceUnit"
            :disabled="!model.cleaning.enabled || !model.cleaning.unitConversionEnabled"
            :placeholder="tx('源单位')"
          />
          <gateway-input
            v-model="model.cleaning.targetUnit"
            :disabled="!model.cleaning.enabled || !model.cleaning.unitConversionEnabled"
            :placeholder="tx('目标单位')"
          />
        </div>
      </a-form-item>
      <a-form-item :label="tx('单位倍率')" :name="['cleaning', 'unitMultiplier']">
        <gateway-number-input
          v-model="model.cleaning.unitMultiplier"
          :disabled="!model.cleaning.enabled || !model.cleaning.unitConversionEnabled"
          :controls="false"
          :step="0.1"
        />
      </a-form-item>
      <a-form-item :label="tx('单位偏移')">
        <gateway-number-input
          v-model="model.cleaning.unitOffset"
          :disabled="!model.cleaning.enabled || !model.cleaning.unitConversionEnabled"
          :controls="false"
          :step="0.1"
        />
      </a-form-item>
    </div>
    <a-form-item :label="tx('枚举映射')">
      <a-switch v-model:checked="model.cleaning.enumMappingEnabled" :disabled="!model.cleaning.enabled" />
    </a-form-item>
    <div v-if="model.cleaning.enabled && model.cleaning.enumMappingEnabled" class="enum-mapping-list">
      <div v-for="(item, index) in model.cleaning.enumMappings" :key="index" class="enum-mapping-row">
        <gateway-input v-model="item.rawValue" :placeholder="tx('原始值，如 1')" />
        <gateway-input v-model="item.cleanValue" :placeholder="tx('显示值，如 运行')" />
        <gateway-input v-model="item.description" :placeholder="tx('说明')" />
        <a-button @click="removeEnumMapping(index)" type="link" danger>{{ tx('删除') }}</a-button>
      </div>
      <a-button @click="addEnumMapping">{{ tx('新增映射') }}</a-button>
    </div>
    <a-divider orientation="left">{{ tx('值处理脚本') }}</a-divider>
    <div class="form-grid">
      <a-form-item :label="tx('启用脚本处理')">
        <a-switch v-model:checked="model.cleaning.valueScriptEnabled" :disabled="!model.cleaning.enabled" />
      </a-form-item>
      <a-form-item :label="tx('已发布脚本')" :name="['cleaning', 'valueScriptId']">
        <gateway-select
          v-model="model.cleaning.valueScriptId"
          searchable
          allow-clear
          :disabled="!model.cleaning.enabled || !model.cleaning.valueScriptEnabled"
          :placeholder="tx('选择或输入筛选')"
          :no-data-text="tx('没有输入类型兼容的已发布标签清洗脚本')"
          :filter-method="filterValueScripts"
          @change="applySelectedValueScript"
          @visible-change="handleValueScriptDropdownVisible"
        >
          <a-select-option
            v-for="script in compatibleValueScripts"
            :key="script.id"
            :label="tx(`${script.name}（v${script.version}，${script.inputDataType} → ${script.outputDataType}）`)"
            :value="script.id"
          >
            {{ `${script.name}（v${script.version}，${script.inputDataType} → ${script.outputDataType}）` }}
          </a-select-option>
        </gateway-select>
      </a-form-item>
      <a-form-item :label="tx('固定版本')">
        <gateway-number-input v-model="model.cleaning.valueScriptVersion" :disabled="true" :controls="false" />
      </a-form-item>
      <a-form-item :label="tx('脚本超时(ms)')">
        <gateway-number-input
          v-model="model.cleaning.valueScriptTimeoutMilliseconds"
          :disabled="!model.cleaning.enabled || !model.cleaning.valueScriptEnabled"
          :min="10"
          :max="5000"
          :controls="false"
        />
      </a-form-item>
      <a-form-item :label="tx('失败策略')">
        <gateway-select
          v-model="model.cleaning.valueScriptFailurePolicy"
          :disabled="!model.cleaning.enabled || !model.cleaning.valueScriptEnabled"
        >
          <a-select-option :label="tx('保留上次有效值')" value="KeepLastGood">
            {{ tx('保留上次有效值') }}
          </a-select-option>
          <a-select-option :label="tx('使用本次原始值')" value="UseOriginal">
            {{ tx('使用本次原始值') }}
          </a-select-option>
          <a-select-option :label="tx('标记坏质量')" value="MarkBad">{{ tx('标记坏质量') }}</a-select-option>
        </gateway-select>
      </a-form-item>
    </div>
    <p class="form-help">
      {{ tx('下拉框内可按脚本名称、说明、输入/输出类型或版本筛选；保存标签时会固定当前发布版本。') }}
    </p>
    <template v-if="isMeter">
      <a-divider orientation="left">{{ tx('表计字段') }}</a-divider>
      <div class="form-grid">
        <a-form-item :label="tx('协议')" :name="['protocol']" required>
          <gateway-select v-model="model.protocol" searchable :disabled="!!deviceProtocol" @change="applyMeterDefaults">
            <a-select-option
              v-for="item in meterProtocolOptions"
              :key="item.value"
              :label="tx(item.label)"
              :value="item.value"
            >
              {{ tx(item.label) }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
        <a-form-item :label="tx('表地址')" :name="['meterAddress']" required>
          <gateway-input v-model="model.meterAddress" :placeholder="tx(meterAddressHint)" />
        </a-form-item>
        <a-form-item :label="tx('数据标识')" :name="['meterDataIdentifier']" required>
          <gateway-input v-model="model.meterDataIdentifier" :placeholder="tx(meterDataIdentifierHint)" />
        </a-form-item>
        <a-form-item :label="tx('表类型')">
          <gateway-select v-model="model.meterType" searchable allow-clear mode="tags">
            <a-select-option v-for="item in meterTypeItems" :key="item" :label="tx(item)" :value="item">
              {{ item }}
            </a-select-option>
          </gateway-select>
        </a-form-item>
      </div>
    </template>
    <a-divider orientation="left">{{ tx('业务信息') }}</a-divider>
    <div class="form-grid">
      <a-form-item :label="tx('单位')"><gateway-input v-model="model.unit" /></a-form-item>
      <a-form-item :label="tx('点位编码')"><gateway-input v-model="model.pointCode" /></a-form-item>
      <a-form-item :label="tx('资产路径')"><gateway-input v-model="model.assetPath" /></a-form-item>
      <a-form-item :label="tx('业务类型')"><gateway-input v-model="model.businessType" /></a-form-item>
    </div>
    <a-form-item :label="tx('描述')">
      <gateway-input v-model="model.description" type="textarea" :rows="3" />
    </a-form-item>
  </a-form>
</template>

<script setup lang="ts">
import { formatMessage } from '../i18n'
import { tx } from '../i18n'
import { toAntRules, type FormRules } from '../utils/ui'
import { computed, onMounted, ref, watch } from 'vue'
import type { FormInstance } from 'ant-design-vue'
import type { ProjectConfig, TagConfig } from '../api'
import { loadValueTransformCatalog, type ValueTransformCatalogItem } from '../scriptingApi'
import { loadModels, type OnnxModelDefinition } from '../modelApi'
import {
  accessModeOptions,
  dataTypeOptionGroups,
  dataTypeDisplayName,
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
  project: ProjectConfig | null
}>()

const formRef = ref<FormInstance>()
const valueScripts = ref<ValueTransformCatalogItem[]>([])
const models = ref<OnnxModelDefinition[]>([])
const valueScriptFilterKeyword = ref('')
const activeProtocol = computed(() => props.model.protocol || props.deviceProtocol)
const virtualDataTypes = [
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
  'Double'
]
const availableDataTypeGroups = computed(() => {
  if (props.model.isVirtual) return [{ label: '虚拟标签标量类型', options: virtualDataTypes }]
  const device = props.project?.devices?.find((item) => item.id === props.model.deviceId)
  return dataTypeOptionGroups(activeProtocol.value, device?.connection?.driverOptionsJson)
})
const isMeter = computed(() => isMeterProtocol(activeProtocol.value))
const addressPlaceholder = computed(() => tagAddressPlaceholder(activeProtocol.value))
const meterAddressHint = computed(() => meterAddressPlaceholder(activeProtocol.value))
const compatibleValueScripts = computed(() => {
  const keyword = valueScriptFilterKeyword.value.trim().toLocaleLowerCase()
  return valueScripts.value.filter((script) => {
    if (script.scope === 'RuleEngine' || !isCompatibleInputType(script.inputDataType, props.model.dataType))
      return false
    if (!keyword) return true
    const searchableText = [
      script.name,
      script.description,
      script.inputDataType,
      script.outputDataType,
      `v${script.version}`,
      String(script.version)
    ]
      .join(' ')
      .toLocaleLowerCase()
    return searchableText.includes(keyword)
  })
})
const publishedModels = computed(() => models.value.filter((item) => item.publishedVersion > 0))
const inputTagOptions = computed(() => {
  const channelNames = new Map((props.project?.channels ?? []).map((item) => [item.id, item.name || item.id]))
  const options: Array<{ value: string; label: string }> = []
  for (const device of props.project?.devices ?? []) {
    for (const tag of device.tags ?? [])
      if (tag.enabled && tag.id !== props.model.id)
        options.push({
          value: `${device.channelId}/${device.id}//${tag.id}`,
          label: formatMessage('{0}-{1}-【设备直属】-{2}', [
            channelNames.get(device.channelId) || device.channelId,
            device.name,
            tag.name
          ])
        })
    for (const group of device.groups ?? [])
      for (const tag of group.tags ?? [])
        if (tag.enabled && tag.id !== props.model.id)
          options.push({
            value: `${device.channelId}/${device.id}/${group.id}/${tag.id}`,
            label: `${channelNames.get(device.channelId) || device.channelId}-${device.name}-【${group.name}】-${tag.name}`
          })
  }
  return options.sort((a, b) => a.label.localeCompare(b.label, 'zh-CN'))
})

onMounted(async () => {
  const [scriptsResult, modelsResult] = await Promise.allSettled([loadValueTransformCatalog(), loadModels()])
  valueScripts.value = scriptsResult.status === 'fulfilled' ? scriptsResult.value.data : []
  models.value = modelsResult.status === 'fulfilled' ? modelsResult.value.data : []
})

/**
 * 切换数据来源时固定虚拟标签为只读，并清除无效设备地址。
 */
function changeTagSource(isVirtual: boolean) {
  if (isVirtual) {
    props.model.address = ''
    props.model.accessMode = 'ReadOnly'
    props.model.source = 'ONNX'
  }
}

/**
 * 选择模型时固定当前发布版本，并按输入维度初始化标签绑定行。
 */
function applyVirtualModel(modelId: string) {
  const model = models.value.find((item) => item.id === modelId)
  props.model.virtualModel.modelVersion = model?.publishedVersion ?? 0
  const version = model?.versions.find((item) => item.version === model.publishedVersion)
  props.model.virtualModel.outputName = version?.outputs[0]?.name ?? ''
  const dims = version?.inputs[0]?.dimensions ?? [1]
  const count = Math.min(
    64,
    Math.max(1, dims.length > 1 ? dims.slice(1).reduce((a, b) => a * Math.max(1, b), 1) : Math.max(1, dims[0] || 1))
  )
  props.model.virtualModel.inputs = Array.from({ length: count }, (_, index) => ({
    featureName: `Feature${index + 1}`,
    tagPath: '',
    multiplier: 1,
    offset: 0
  }))
}

/**
 * 增加一个模型特征与标签路径绑定。
 */
function addVirtualInput() {
  props.model.virtualModel.inputs.push({
    featureName: `Feature${props.model.virtualModel.inputs.length + 1}`,
    tagPath: '',
    multiplier: 1,
    offset: 0
  })
}

/**
 * 固定当前所选脚本的发布版本。
 */
function applySelectedValueScript(scriptId: string) {
  const script = valueScripts.value.find((item) => item.id === scriptId)
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
  const numericTypes = [
    'Byte',
    'UInt8',
    'Int8',
    'Int16',
    'UInt16',
    'Int32',
    'UInt32',
    'Int64',
    'UInt64',
    'Float',
    'Double',
    'Decimal'
  ]
  return numericTypes.includes(inputType) && numericTypes.includes(tagType)
}
const meterDataIdentifierHint = computed(() => meterDataIdentifierPlaceholder(activeProtocol.value))
const meterTypeItems = computed(() => meterTypeOptions(activeProtocol.value))
const rules: FormRules<TagConfig> = {
  name: [{ required: true, message: '请输入标签名称', trigger: 'blur' }],
  // Conditional validators own these requirements; suppress Ant's extra default required rule.
  protocol: [{ required: false, validator: validateMeterProtocol, trigger: 'change' }],
  address: [{ required: false, validator: validateAddress, trigger: 'blur' }],
  meterAddress: [{ required: false, validator: validateMeterAddress, trigger: 'blur' }],
  meterDataIdentifier: [{ required: false, validator: validateMeterDataIdentifier, trigger: 'blur' }],
  scanRateMs: [{ validator: validateMin100, trigger: 'change' }],
  failureRetryDelayMs: [{ validator: validateMin100, trigger: 'change' }],
  elementCount: [{ validator: validateElementCount, trigger: 'change' }],
  'scaling.multiplier': [{ validator: validateScalingMultiplier, trigger: 'change' }],
  'scaling.offset': [{ validator: validateScalingOffset, trigger: 'change' }],
  'scaling.decimalPlaces': [{ validator: validateScalingDecimalPlaces, trigger: 'change' }],
  'cleaning.deadband': [{ validator: validateCleaningNonNegative, trigger: 'change' }],
  'cleaning.spikeThreshold': [{ validator: validateCleaningNonNegative, trigger: 'change' }],
  'cleaning.unitMultiplier': [{ validator: validateUnitMultiplier, trigger: 'change' }],
  'cleaning.valueScriptId': [{ validator: validateValueScriptSelection, trigger: 'change' }],
  'virtualModel.modelId': [{ required: false, validator: validateVirtualModel, trigger: 'change' }],
  'virtualModel.inputs': [{ required: false, validator: validateVirtualInputs, trigger: 'change' }]
}

const antRules = computed(() => toAntRules(rules, tx))

const scalingPreview = computed(() => {
  if (!props.model.scaling?.enabled) return '未启用时，标签值按设备原始值展示。'
  const multiplier = Number(props.model.scaling.multiplier)
  const offset = Number(props.model.scaling.offset)
  const decimalPlaces = Math.max(0, Math.min(8, Number(props.model.scaling.decimalPlaces ?? 2)))
  if (!Number.isFinite(multiplier) || !Number.isFinite(offset)) return '请输入合法的倍率和偏移。'
  const value = 10 * multiplier + offset
  return formatMessage('示例：原始值 10 会显示为 {0}。', [value.toFixed(decimalPlaces)])
})

watch(
  () => props.deviceProtocol,
  (protocol) => {
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
  if (props.model.isVirtual || isMeter.value || String(value ?? '').trim()) callback()
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

/**
 * 虚拟标签必须固定一个已发布模型版本。
 */
function validateVirtualModel(_rule: unknown, value: string, callback: (error?: Error) => void) {
  if (!props.model.isVirtual || (value?.trim() && props.model.virtualModel.modelVersion > 0)) return callback()
  callback(new Error('请选择一个已发布模型'))
}

/**
 * 虚拟标签至少需要一个完整的四段式输入标签路径。
 */
function validateVirtualInputs(_rule: unknown, value: Array<{ tagPath: string }>, callback: (error?: Error) => void) {
  if (!props.model.isVirtual) return callback()
  if (value?.length && value.every((item) => item.tagPath?.split('/').length === 4)) return callback()
  callback(new Error('请完整配置至少一个模型输入标签'))
}

defineExpose({ validate })
</script>

<style scoped>
.model-input-list {
  width: 100%;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.model-input-row {
  display: grid;
  grid-template-columns: minmax(105px, 0.65fr) minmax(260px, 1.8fr) minmax(85px, 0.55fr) minmax(85px, 0.55fr) auto;
  gap: 8px;
  align-items: center;
}
@media (max-width: 1100px) {
  .model-input-row {
    grid-template-columns: 1fr;
  }
}
</style>
